using System;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>Stage 15C: CPU telemetry and existing budget contract.
/// GPU visual/performance validation remains a DesktopGL manual acceptance.</summary>
public sealed class L15CProductionPerformanceTests
{
    [Fact]
    public void EmptyHistoryIsDefinedAndClearResetsCounters()
    {
        var samples = new LightingCpuTimeWindow(4);
        Assert.Equal(0, samples.Count);
        Assert.Equal(0, samples.Latest);
        Assert.Equal(0, samples.Mean);
        Assert.Equal(0, samples.Peak);
        Assert.Equal(0, samples.P95);
        samples.Add(2);
        samples.Add(8);
        samples.Clear();
        Assert.Equal(0, samples.Count);
        Assert.Equal(0, samples.Mean);
        Assert.Equal(0, samples.Peak);
        Assert.Equal(0, samples.P95);
        samples.Add(3);
        Assert.Equal(1, samples.Count);
        Assert.Equal(3, samples.Latest);
    }

    [Fact]
    public void RollingHistoryEvictsOldSpikesInsteadOfReportingLifetimePeak()
    {
        var samples = new LightingCpuTimeWindow(4);
        samples.Add(100);
        samples.Add(2);
        samples.Add(3);
        samples.Add(4);
        Assert.Equal(100, samples.Peak);
        Assert.Equal(100, samples.P95);
        samples.Add(5); // 100 is evicted.
        Assert.Equal(4, samples.Count);
        Assert.Equal(5, samples.Latest);
        Assert.Equal(3.5, samples.Mean, 12);
        Assert.Equal(5, samples.Peak);
        Assert.Equal(5, samples.P95);
        samples.Add(1);
        Assert.Equal(3.25, samples.Mean, 12);
        Assert.Equal(5, samples.Peak);
    }

    [Fact]
    public void P95UsesNearestRankAndDoesNotAlterInsertionOrder()
    {
        var samples = new LightingCpuTimeWindow(20);
        for (int i = 20; i >= 1; i--) samples.Add(i);
        Assert.Equal(19, samples.P95); // ceil(20 * .95) == 19
        Assert.Equal(20, samples.Peak);
        Assert.Equal(1, samples.Latest);
        Assert.Equal(10.5, samples.Mean, 12);
    }

    [Fact]
    public void InvalidDurationsAndCapacitiesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightingCpuTimeWindow(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightingCpuTimeWindow(3601));
        var samples = new LightingCpuTimeWindow(2);
        Assert.Throws<ArgumentOutOfRangeException>(() => samples.Add(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => samples.Add(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => samples.Add(-1));
        Assert.Equal(0, samples.Count);
    }

    [Fact]
    public void QualityPresetsBoundShadowGeometryBeforeStressLights()
    {
        var low = LightingFrameBudget.For(LightingQuality.Low);
        var balanced = LightingFrameBudget.For(LightingQuality.Balanced);
        var high = LightingFrameBudget.For(LightingQuality.High);
        Assert.Equal(0, low.MaxShadowLights);
        Assert.True(low.ShadowRadialSamples < balanced.ShadowRadialSamples);
        Assert.True(balanced.ShadowRadialSamples < high.ShadowRadialSamples);
        Assert.True(balanced.MaxLights < high.MaxLights);
        Assert.True(balanced.MaxShadowLights < high.MaxShadowLights);
        Assert.True(low.MaxLights > 0);
    }
}
