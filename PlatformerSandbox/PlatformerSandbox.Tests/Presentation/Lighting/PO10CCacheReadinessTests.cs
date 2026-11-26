using System.Linq;
using PlatformerSandbox.Foundation.Performance;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class PO10CCacheReadinessTests
{
    [Fact]
    public void WarmupRequiresMinimumAndConsecutiveReadiness()
    {
        var policy = new BenchmarkWarmupPolicy(5,12,3);
        Assert.False(policy.Observe(true));
        Assert.False(policy.Observe(true));
        Assert.False(policy.Observe(true));
        Assert.False(policy.Observe(false));
        Assert.False(policy.Observe(true));
        Assert.False(policy.Observe(true));
        Assert.True(policy.Observe(true));
        Assert.Equal(7,policy.Observed);
        Assert.Equal("stable",policy.Status);
    }
    [Fact]
    public void UnstableWarmupHasBoundedExit()
    {
        var policy = new BenchmarkWarmupPolicy(3,5,3);
        for(int i=0;i<4;i++) Assert.False(policy.Observe(false));
        Assert.True(policy.Observe(false));
        Assert.Equal("max_reached",policy.Status);
        Assert.Equal(5,policy.Observed);
        Assert.True(policy.Observe(true));
        Assert.Equal(5,policy.Observed);
    }
    [Fact]
    public void AdaptiveFixtureExercisesAllFiveTiers()
    {
        var view = new LightingViewport(1280,720,new LightPoint(0,0),32);
        var lights = BenchmarkLightingFixtures.Create("adaptive",view);
        Assert.Equal(new[]{1,3,5,7,9},new AdaptiveAreaSamplePolicy().Apply(lights,32,9).Select(l=>l.AreaSampleCount));
        Assert.All(lights,l=>Assert.Equal(0,l.AreaSampleCount));
        Assert.Equal(5,lights.Count);
    }
    [Fact]
    public void StaticFixtureHasValueEqualUnroutedShadowFreeLights()
    {
        var view = new LightingViewport(1280,720,new LightPoint(0,0),32);
        var lights = BenchmarkLightingFixtures.Create("static",view);
        Assert.True(lights.SequenceEqual(BenchmarkLightingFixtures.Create("static",view)));
        Assert.All(lights,l=> { Assert.False(l.CastShadows); Assert.Null(l.AffectsLayers); Assert.Null(l.OccluderLayers); Assert.Equal(LightShape.Point,l.Shape); });
        Assert.False(lights.SequenceEqual(BenchmarkLightingFixtures.Create("static",view with { Focus=new LightPoint(.1,0) })));
    }
}
