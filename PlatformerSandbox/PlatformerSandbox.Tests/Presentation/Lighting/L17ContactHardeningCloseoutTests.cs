using System;
using System.Collections.Generic;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L17ContactHardeningCloseoutTests
{
    private static RadialLight Light(LightPoint source) => new()
    {
        Id = "source", Owner = "test", Position = source, Radius = 10,
        CastShadows = true, Intensity = 1
    };

    private static LightingOccluder Rect(string id, double minX, double minY,
        double maxX, double maxY) => new()
    {
        Id = id, LayerId = "WORLD",
        Vertices = [new(minX, minY), new(maxX, minY),
            new(maxX, maxY), new(minX, maxY)]
    };

    private static ushort[] Profile(RadialLight light,
        IReadOnlyList<LightingOccluder> blockers)
    {
        var polygon = ShadowVisibility.BuildPolygon(light, blockers, 192);
        return ShadowContactHardening.BuildProfile(light.Position, polygon,
            light.Radius * 1.02);
    }

    [Fact]
    public void FrontHitDistanceIsDerivedFromHardVisibilityFan()
    {
        RadialLight light = Light(new(0, 0));
        ushort[] profile = Profile(light, [Rect("wall", 2, -1, 3, 1)]);
        Assert.Equal(ShadowContactHardening.ProfileResolution, profile.Length);
        double hitRight = ShadowContactHardening.ContactDistancePixels(profile[0], 10.2);
        Assert.InRange(hitRight, 1.99, 2.03);
        double hitUpper = ShadowContactHardening.ContactDistancePixels(profile[256], 10.2);
        Assert.InRange(hitUpper, 10.17, 10.201);
        // Texture sampling wraps at zero-angle instead of adding an invented seam.
        double rightOfSeam = ShadowContactHardening.ContactDistancePixels(profile[^1], 10.2);
        Assert.InRange(Math.Abs(rightOfSeam - hitRight), 0, .03);
    }

    [Fact]
    public void ExistingHardShadowVisibilityIsNotModified()
    {
        var light = Light(new(0, 0));
        var wall = Rect("wall", 2, -1, 3, 1);
        bool shadowed = ShadowVisibility.CanIlluminate(light, new(6, 0), [wall]);
        Profile(light, [wall]);
        Assert.False(shadowed);
        Assert.False(ShadowVisibility.CanIlluminate(light, new(6, 0), [wall]));
        Assert.True(ShadowVisibility.CanIlluminate(light, new(0, 6), [wall]));
    }

    [Fact]
    public void MovingAndRotatingGeometryChangesContactDistances()
    {
        RadialLight light = Light(new(0, 0));
        var close = Rect("moving", 2, -.5, 3, .5);
        var shifted = Rect("moving", 5, -.5, 6, .5);
        ushort[] a = Profile(light, [close]);
        ushort[] b = Profile(light, [shifted]);
        Assert.True(a[0] < b[0]);
        var triangle = new LightingOccluder
        {
            Id = "triangle", LayerId = "WORLD",
            Vertices = [new(2, -1), new(4, 0), new(2, 1)]
        };
        var rotatedTriangle = new LightingOccluder
        {
            Id = "triangle", LayerId = "WORLD",
            Vertices = [new(-1, -2), new(0, -4), new(1, -2)]
        };
        ushort[] c = Profile(light, [triangle]);
        ushort[] d = Profile(light, [rotatedTriangle]);
        Assert.True(c[0] < ushort.MaxValue / 2);
        Assert.True(d[0] > ushort.MaxValue - 250);
    }

    [Fact]
    public void SelfOccluderExclusionCannotArtificiallyClampRimProfile()
    {
        RadialLight light = Light(new(0, 0));
        var ownWall = Rect("self", 2, -1, 3, 1);
        var otherWall = Rect("other", 5, -1, 6, 1);
        ushort[] withOwn = Profile(light, [ownWall, otherWall]);
        ushort[] withoutOwn = Profile(light, [otherWall]);
        Assert.True(withOwn[0] < withoutOwn[0]);
        Assert.InRange(ShadowContactHardening.ContactDistancePixels(withoutOwn[0], 10.2),
            4.99, 5.04);
    }

    [Fact]
    public void ContactPenumbraHardensAtBlockerAndGrowsWithReceiverDistance()
    {
        double near = ShadowContactHardening.RadiusPixels(100, 100, 8, 8);
        double mid = ShadowContactHardening.RadiusPixels(130, 100, 8, 8);
        double far = ShadowContactHardening.RadiusPixels(210, 100, 8, 8);
        Assert.InRange(near, .74, .76);
        Assert.True(mid > near);
        Assert.True(far > mid);
        Assert.Equal(8, far);
        Assert.Equal(4, ShadowContactHardening.RadiusPixels(500, 100, 12, 4));
        Assert.Equal(0, ShadowContactHardening.RadiusPixels(500, 100, 12, 0));
        Assert.True(ShadowContactHardening.RadiusPixels(130, 100, 12, 8) > mid);
    }

    [Fact]
    public void ProfileHasBoundedCostAndRejectsInvalidInputs()
    {
        var empty = ShadowContactHardening.BuildProfile(new(0, 0),
            Array.Empty<LightPoint>(), 10);
        Assert.Equal(1024, empty.Length);
        Assert.All(empty, x => Assert.Equal(ushort.MaxValue, x));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShadowContactHardening.BuildProfile(new(0, 0), [], 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShadowContactHardening.BuildProfile(new(0, 0), [], 10, 2048));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShadowContactHardening.RadiusPixels(-1, 2, 8, 8));
    }

    [Fact]
    public void QualityAutoAndPerLightSourceSizeControlsAreBounded()
    {
        Assert.Equal(ShadowEdgePreset.Hard,
            ShadowContactControls.QualityPreset(LightingQuality.Low));
        Assert.Equal(ShadowEdgePreset.Soft4Px,
            ShadowContactControls.QualityPreset(LightingQuality.Balanced));
        Assert.Equal(ShadowEdgePreset.Soft8Px,
            ShadowContactControls.QualityPreset(LightingQuality.High));
        Assert.Equal(ShadowFilterMode.ContactHardening,
            ShadowContactControls.Next(ShadowFilterMode.UniformGaussian));
        Assert.Equal(ShadowQualityPolicy.FollowLightingQuality,
            ShadowContactControls.Next(ShadowQualityPolicy.Manual));
        float source = 2;
        foreach (float expected in new[] { 4f, 8f, 12f, 2f })
        {
            source = ShadowContactControls.NextEmitter(source);
            Assert.Equal(expected, source);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShadowContactControls.NextEmitter(20));
    }
}
