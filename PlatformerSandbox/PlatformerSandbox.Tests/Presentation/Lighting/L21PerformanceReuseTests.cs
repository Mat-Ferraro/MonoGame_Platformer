using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L21PerformanceReuseTests
{
    private static RadialLight Light => new() { Id = "area", Position = new(0,0), Radius = 12,
        Shape = LightShape.Tube, Direction = new(1,0), TubeLength = 4, CastShadows = true };
    private static LightingOccluder Wall => new() { Id = "wall", Vertices = new LightPoint[]
        { new(2,-1), new(3,-1), new(3,1), new(2,1) } };

    [Fact]
    public void CacheMatchesOnlySameSampleAndSameOrderedHullSnapshot()
    {
        var cache = new FrameVisibilityGeometryCache();
        var samples = AreaEmitterSampling.Samples(Light, 3);
        var wall = Wall;
        var polygon = ShadowVisibility.BuildPolygon(samples[0], new[] { wall }, 128, true);
        cache.Store(samples[0], new[] { wall }, 128, true, polygon);
        Assert.True(cache.TryGet(samples[0] with { }, new[] { wall }, 128, true, out var hit));
        Assert.Same(polygon, hit);
        Assert.False(cache.TryGet(samples[1], new[] { wall }, 128, true, out _));
        Assert.False(cache.TryGet(samples[0], new[] { wall }, 128, false, out _));
        Assert.False(cache.TryGet(samples[0], new[] { wall }, 192, true, out _));
        Assert.False(cache.TryGet(samples[0], new[] { wall with { LitFaceDepthZ = 1 } }, 128, true, out _));
        Assert.False(cache.TryGet(samples[0], System.Array.Empty<LightingOccluder>(), 128, true, out _));
        Assert.False(cache.TryGet(samples[0], new[] { wall, Wall }, 128, true, out _));
        cache.Clear();
        Assert.False(cache.TryGet(samples[0], new[] { wall }, 128, true, out _));
    }

    [Fact]
    public void OutOfRangePointSamplesHaveZeroSpatialInfluenceWithoutRenormalizing()
    {
        var samples = AreaEmitterSampling.Samples(Light with { Radius = 1 }, 9);
        var view = new LightingViewport(1000, 600, new(0,0), 100);
        var region = new ProductionMaterialRegion(635, 290, 20, 20);
        var retained = samples.Where(s => SpatialSurfaceLighting.Influence(s, view, region, 1) > 0).ToArray();
        Assert.InRange(retained.Length, 1, 8);
        Assert.All(retained, s => Assert.Equal(Light.Intensity / 9, s.Intensity, 10));
    }
}
