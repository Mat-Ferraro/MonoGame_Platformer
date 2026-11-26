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
    public void WorldSpaceFanSurvivesViewChangesAndBoundedCacheRecycling()
    {
        var cache = new PersistentVisibilityGeometryCache();
        var light = new RadialLight { Id = "static", Position = new(0,0), Radius = 10, CastShadows = true };
        var wall = new LightingOccluder { Id = "wall", LitFaceLayer = SceneLightingLayerSet.Standard11().Gameplay,
            Vertices = new LightPoint[] { new(2,-1),new(3,-1),new(3,1),new(2,1) } };
        var blockers = new[]{wall};
        var polygon = ShadowVisibility.BuildPolygon(light,blockers,128,true);
        cache.BeginFrame(); cache.Store(light,blockers,128,true,polygon);
        var viewA = new LightingViewport(1280,720,new LightPoint(0,0),32);
        var viewB = new LightingViewport(1280,720,new LightPoint(0,1),32);
        Assert.NotEqual(viewA.Project(polygon[0]), viewB.Project(polygon[0]));
        cache.BeginFrame();
        Assert.True(cache.TryGet(light,blockers,128,true,out var hit));
        Assert.Equal(polygon.ToArray(),hit.ToArray());
        // Exercise bucket recycling and exact geometry invalidation.
        for (int i=1;i<=160;i++) cache.Store(light with { Position = new LightPoint(-i,0) },blockers,128,true,polygon);
        cache.Clear(); cache.BeginFrame();
        cache.Store(light,blockers,128,true,polygon); cache.BeginFrame();
        Assert.True(cache.TryGet(light,blockers,128,true,out _));
        wall.Vertices[0]=new LightPoint(1,-1);
        Assert.False(cache.TryGet(light,blockers,128,true,out _));
    }

    [Fact]
    public void LabProfilesSeparateQualityChangesFromEquivalentOptimization()
    {
        var reference=PlatformerSandbox.Foundation.Performance.LightingQualityLabProfile.For(1);
        var full=PlatformerSandbox.Foundation.Performance.LightingQualityLabProfile.For(2);
        Assert.Equal(reference.Scale,full.Scale);
        Assert.False(reference.Optimized); Assert.True(full.Optimized);
        Assert.False(full.Adaptive);
        var experimental=PlatformerSandbox.Foundation.Performance.LightingQualityLabProfile.For(6);
        Assert.True(experimental.Adaptive); Assert.True(experimental.StaticFields);
        Assert.Equal(50,experimental.Scale);
    }

    [Fact]
    public void ManualQualityLabHasIndependentWideAuthoredSceneAndPreservesLightingPolicy()
    {
        string? dir = AppContext.BaseDirectory;
        for (int i=0;i<14 && dir is not null;i++,dir=Directory.GetParent(dir)?.FullName)
        {
            string levels=Path.Combine(dir,"Content","Levels");
            if(!File.Exists(Path.Combine(levels,"LightingPerformanceLab.ldtk"))) continue;
            var catalog=LdtkContentCatalog.Discover(levels);
            var original=catalog.Levels[GameLevelIds.LightingPerformanceLab];
            var alias=catalog.Levels[GameLevelIds.LightingQualityLab];
            Assert.Equal(original.Identifier,alias.Identifier);
            Assert.NotEqual(original.FilePath,alias.FilePath);
            Assert.True(catalog.Supports(new SceneDestination(GameLevelIds.LightingQualityLab)));
            Assert.Contains(GameLevelMenuCatalog.WithDiscoveredLevels(catalog.Levels.Keys), item => item.Destination?.LevelId==GameLevelIds.LightingQualityLab);
            var content=catalog.Prepare(new SceneDestination(GameLevelIds.LightingQualityLab));
            Assert.Equal(GameLevelIds.LightingPerformanceLab,content.Lighting!.LevelId);
            var benchmark=catalog.Prepare(new SceneDestination(GameLevelIds.LightingPerformanceLab));
            Assert.Equal(1280, benchmark.Imported.WidthPx);
            Assert.Equal(3840, content.Imported.WidthPx);
            Assert.Equal(1280, content.Imported.HeightPx);
            Assert.True(content.Lighting.Lights.Count > benchmark.Lighting!.Lights.Count);
            return;
        }
        throw new FileNotFoundException("LightingPerformanceLab.ldtk");
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
