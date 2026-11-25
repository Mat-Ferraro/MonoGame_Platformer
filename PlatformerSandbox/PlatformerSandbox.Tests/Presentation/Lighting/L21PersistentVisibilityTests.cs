using System;
using System.Linq;
using System.IO;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;
public sealed class L21PersistentVisibilityTests
{
    [Fact]
    public void CacheRequiresPriorFrameAndExactGeometryAndDepthPolicy()
    {
        var cache = new PersistentVisibilityGeometryCache();
        var light = new RadialLight { Id = "light", Position = new(0,0), Radius = 10, CastShadows = true };
        var wall = new LightingOccluder { Id = "wall", LitFaceLayer = SceneLightingLayerSet.Standard11().Gameplay,
            Vertices = new LightPoint[] {new(2,-1),new(3,-1),new(3,1),new(2,1)} };
        var polygon = ShadowVisibility.BuildPolygon(light,new[]{wall},128,true);
        cache.BeginFrame(); cache.Store(light,new[]{wall},128,true,polygon);
        Assert.False(cache.TryGet(light,new[]{wall},128,true,out _));
        cache.BeginFrame();
        Assert.True(cache.TryGet(light with { Intensity = .5, Color = new LightRgb(1,0,0) },
            new[]{wall with { Vertices = wall.Vertices.ToArray() }},128,true,out var hit));
        Assert.Equal(polygon.ToArray(),hit.ToArray());
        Assert.False(cache.TryGet(light with { VirtualZ = -1 },new[]{wall},128,true,out _));
        Assert.False(cache.TryGet(light with { Position = new(0,1) },new[]{wall},128,true,out _));
        Assert.False(cache.TryGet(light,new[]{wall},192,true,out _));
        Assert.False(cache.TryGet(light,new[]{wall},128,false,out _));
        wall.Vertices[0] = new(1,-1);
        Assert.False(cache.TryGet(light,new[]{wall},128,true,out _));
        cache.Clear();
        Assert.False(cache.TryGet(light,new[]{wall},128,true,out _));
    }
    [Fact]
    public void PerformanceLabUsesSameWorkloadAsVisualLab()
    {
        string? dir=AppContext.BaseDirectory;
        for (int i=0;i<14 && dir is not null;i++,dir=Directory.GetParent(dir)?.FullName)
        {
            var path=Path.Combine(dir,"Content","Levels","LightingPerformanceLab.ldtk");
            if(!File.Exists(path)) continue;
            var perf=LdtkLightingImporter.FromJson(File.ReadAllText(path),GameLevelIds.LightingPerformanceLab);
            var visual=LdtkLightingImporter.FromJson(File.ReadAllText(Path.Combine(dir,"Content","Levels","Phase21LightingLab.ldtk")),GameLevelIds.Phase21LightingLab);
            Assert.Equal(visual.Lights.Count,perf.Lights.Count);
            Assert.Equal(visual.Occluders.Count,perf.Occluders.Count);
            Assert.Equal(visual.Surfaces.Count,perf.Surfaces.Count);
            Assert.Equal(visual.Lights.Select(l=>l.Tag).OrderBy(x=>x),perf.Lights.Select(l=>l.Tag).OrderBy(x=>x));
            Assert.True(GameLevelIds.IsStandalonePlayableLevel(GameLevelIds.LightingPerformanceLab));
            return;
        }
        throw new FileNotFoundException("LightingPerformanceLab.ldtk");
    }
}
