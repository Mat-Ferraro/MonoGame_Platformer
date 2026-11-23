using System;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L17ProductionSoftShadowTests
{
    [Fact]
    public void HardRemainsDefaultAndSettingsCycleThroughBoundedWidths()
    {
        Assert.Equal(0f, ShadowEdgeSettings.WidthPixels(ShadowEdgePreset.Hard));
        var preset = ShadowEdgePreset.Hard;
        foreach (var expected in new[]
        {
            ShadowEdgePreset.Soft2Px, ShadowEdgePreset.Soft4Px,
            ShadowEdgePreset.Soft8Px, ShadowEdgePreset.Hard
        })
        {
            preset = ShadowEdgeSettings.Next(preset);
            Assert.Equal(expected, preset);
        }
        Assert.Equal("SOFT 8PX", ShadowEdgeSettings.Label(ShadowEdgePreset.Soft8Px));
    }

    [Fact]
    public void QualityWithoutProjectedShadowsDoesNotRequestSoftening()
    {
        Assert.Equal(0f, ShadowEdgeSettings.EffectiveWidth(ShadowEdgePreset.Soft8Px,
            LightingFrameBudget.For(LightingQuality.Low)));
        Assert.Equal(4f, ShadowEdgeSettings.EffectiveWidth(ShadowEdgePreset.Soft4Px,
            LightingFrameBudget.For(LightingQuality.Balanced)));
        Assert.Equal(8f, ShadowEdgeSettings.EffectiveWidth(ShadowEdgePreset.Soft8Px,
            LightingFrameBudget.For(LightingQuality.High)));
    }

    [Theory]
    [InlineData((ShadowEdgePreset)(-1))]
    [InlineData((ShadowEdgePreset)99)]
    public void InvalidPresetsAreRejected(ShadowEdgePreset preset)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShadowEdgeSettings.Next(preset));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShadowEdgeSettings.WidthPixels(preset));
        Assert.Throws<ArgumentOutOfRangeException>(() => ShadowEdgeSettings.Label(preset));
    }

    [Theory]
    [InlineData(2f)]
    [InlineData(4f)]
    [InlineData(8f)]
    public void GaussianBilinearKernelIsNormalizedAndWithinRadius(float radius)
    {
        ShadowGaussianKernel k = ShadowEdgeSettings.GaussianKernel(radius);
        Assert.InRange(k.TotalWeight, 0.999999, 1.000001);
        Assert.True(k.CenterWeight > 0);
        foreach (ShadowGaussianPair pair in new[] { k.Pair0, k.Pair1, k.Pair2, k.Pair3 })
        {
            Assert.True(pair.Weight >= 0);
            Assert.InRange(pair.Offset, 0f, radius);
        }
        // Constant visibility remains fully light or fully shadowed after
        // either horizontal or vertical pass; ambient is handled separately.
        Assert.InRange(k.Evaluate(_ => 1, 200), 0.999999, 1.000001);
        Assert.InRange(k.Evaluate(_ => 0, -100), -1e-7, 1e-7);
    }

    // CPU reference of GPU LINEAR texture sampling of a hard mask step.
    // The integer argument describes the source texel CENTER.
    private static double StepWithLinearTextureSampling(double x)
    {
        int a = (int)Math.Floor(x);
        double t = x - a;
        return (a >= 0 ? 1.0 : 0.0) * (1 - t) +
               (a + 1 >= 0 ? 1.0 : 0.0) * t;
    }

    [Fact]
    public void Soft8ProducesAContinuousRampInsteadOfTheOldSparseTapBands()
    {
        ShadowGaussianKernel k = ShadowEdgeSettings.GaussianKernel(8);
        // In 17A, neighboring samples were separated by 8px, producing
        // only a handful of levels. Every adjacent texel now changes smoothly.
        double previous = k.Evaluate(StepWithLinearTextureSampling, -8);
        for (int x = -7; x <= 7; x++)
        {
            double current = k.Evaluate(StepWithLinearTextureSampling, x);
            Assert.True(current > previous, $"Non-rising Gaussian at pixel {x}");
            Assert.InRange(current - previous, 0.0001, 0.20);
            previous = current;
        }
        Assert.Equal(0.5,
            k.Evaluate(StepWithLinearTextureSampling, -0.5), precision: 6);
    }

    [Theory]
    [InlineData(2f)]
    [InlineData(4f)]
    [InlineData(8f)]
    public void GaussianIsSymmetricAndDeepShadowDoesNotLeak(float radius)
    {
        ShadowGaussianKernel k = ShadowEdgeSettings.GaussianKernel(radius);
        for (int i = 0; i <= (int)radius; i++)
        {
            double left = k.Evaluate(StepWithLinearTextureSampling, -0.5 - i);
            double right = k.Evaluate(StepWithLinearTextureSampling, -0.5 + i);
            Assert.Equal(1, left + right, precision: 6);
        }
        Assert.InRange(k.Evaluate(StepWithLinearTextureSampling, -radius - 2), -1e-7, 1e-7);
        Assert.InRange(k.Evaluate(StepWithLinearTextureSampling, radius + 2), 0.999999, 1.000001);
    }

    [Fact]
    public void SoftKernelBilinearPairsMatchUnderlyingDiscreteGaussian()
    {
        const int radius = 8;
        double sigma = radius / 2.5;
        double[] raw = new double[radius + 1];
        double sum = 0;
        for (int i = 0; i <= radius; i++)
        {
            raw[i] = Math.Exp(-i * (double)i / (2 * sigma * sigma));
            sum += i == 0 ? raw[i] : 2 * raw[i];
        }
        for (int i = 0; i <= radius; i++) raw[i] /= sum;
        double Expected(int x) =>
            raw[0] * StepWithLinearTextureSampling(x) +
            EnumerablePairs(x);
        double EnumerablePairs(int x)
        {
            double result = 0;
            for (int i = 1; i <= radius; i++)
                result += raw[i] * (StepWithLinearTextureSampling(x + i) +
                    StepWithLinearTextureSampling(x - i));
            return result;
        }
        ShadowGaussianKernel optimized = ShadowEdgeSettings.GaussianKernel(radius);
        for (int x = -10; x <= 10; x++)
            Assert.Equal(Expected(x),
                optimized.Evaluate(StepWithLinearTextureSampling, x), precision: 6);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(3f)]
    [InlineData(12f)]
    public void FilterRejectsUnsupportedWidths(float width)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ShadowEdgeSettings.GaussianKernel(width));
    }

    [Fact]
    public void StaticAndMovingShadowGeometryRemainsTheSame()
    {
        var blocker = new LightingOccluder
        {
            Id = "pillar", LayerId = "WORLD",
            Vertices = [new(2, -1), new(3, -1), new(3, 1), new(2, 1)]
        };
        var light = new RadialLight
        {
            Id = "light", Owner = "test", Position = new(0, 0),
            Radius = 10, Intensity = 1, CastShadows = true
        };
        Assert.False(ShadowVisibility.CanIlluminate(light, new(5, 0), [blocker]));
        Assert.True(ShadowVisibility.CanIlluminate(light, new(5, 5), [blocker]));
        var moved = new LightingOccluder
        {
            Id = "pillar", LayerId = "WORLD",
            Vertices = [new(2, 3), new(3, 3), new(3, 5), new(2, 5)]
        };
        Assert.True(ShadowVisibility.CanIlluminate(light, new(5, 0), [moved]));
    }
}
