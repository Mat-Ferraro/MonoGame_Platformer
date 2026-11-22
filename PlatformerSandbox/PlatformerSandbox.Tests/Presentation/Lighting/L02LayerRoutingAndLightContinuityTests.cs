using System;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L02LayerRoutingAndLightContinuityTests
{
    private static string Find(string relative)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            string path = Path.Combine(dir, relative);
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException("Missing L02 fixture: " + relative);
    }
    private static string LayersJson() => Find(Path.Combine("Content", "Lighting", "L02_SceneLayers.json"));
    private static string FlatJson() => Find(Path.Combine("Content", "Levels", "LightingFlatTest.ldtk"));
    private static RadialLight Light(string id = "lamp") => new()
    {
        Id = id, Owner = "level", Position = new LightPoint(10, 10), Radius = 8,
        Color = LightRgb.White, Intensity = 1.5, Channels = LightChannel.All
    };

    [Fact]
    public void DefaultStackIsFiveBackgroundsPlayerFiveForegroundsInFarToNearOrder()
    {
        SceneLightingLayerSet layers = SceneLightingLayerSet.Standard11();
        Assert.Equal(11, layers.Layers.Count);
        Assert.Equal(new[] { "BG5", "BG4", "BG3", "BG2", "BG1", "PLAYER",
            "FG1", "FG2", "FG3", "FG4", "FG5" }, layers.Layers.Select(x => x.Id));
        Assert.Equal(RenderLayerLightingMode.Silhouette, layers.Get("FG5").Mode);
        Assert.Equal(RenderLayerLightingMode.Lit, layers.Get("FG1").Mode);
    }

    [Fact]
    public void SceneCanReplaceDefaultWithArbitraryThreeLayerStack()
    {
        string json = """
        { "version":1, "scenes":[{"sceneId":"SMALL", "layers":[
          {"id":"FAR", "order":-1,"group":"Background","mode":"Unlit"},
          {"id":"ACTORS","order":0,"group":"Gameplay","mode":"Lit"},
          {"id":"NEAR","order":1,"group":"Foreground","mode":"Silhouette","ambientScale":0.03}
        ]}] }
        """;
        var catalog = SceneLightingLayerCatalog.FromJson(json);
        Assert.Equal(3, catalog.ForScene("SMALL").Layers.Count);
        Assert.Equal(11, catalog.ForScene("OTHER").Layers.Count);
        Assert.Equal("ACTORS", catalog.ForScene("SMALL").Gameplay.Id);
    }

    [Fact]
    public void DuplicatesUnknownLayersAndNoGameplayAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new SceneLightingLayerSet(new[]
        { new SceneLightingLayer { Id = "FG1", Order = 1, Group = RenderLayerGroup.Foreground } }));
        Assert.Throws<ArgumentException>(() => new SceneLightingLayerSet(new[]
        {
            new SceneLightingLayer { Id = "PLAYER", Order = 0, Group = RenderLayerGroup.Gameplay },
            new SceneLightingLayer { Id = "PLAYER", Order = 1, Group = RenderLayerGroup.Foreground }
        }));
        var scene = SceneLightingLayerSet.Standard11();
        Assert.Throws<ArgumentException>(() => LightLayerRouting.ValidateRouting(
            Light() with { AffectsLayers = new[] { "DOES_NOT_EXIST" } }, scene));
    }

    [Fact]
    public void AffectsLayersAreIndependentFromOccluderLayers()
    {
        var layers = SceneLightingLayerSet.Standard11();
        var lamp = Light() with
        {
            AffectsLayers = new[] { "BG1", "PLAYER" },
            OccluderLayers = new[] { "PLAYER" }
        };
        lamp.Validate();
        LightLayerRouting.ValidateRouting(lamp, layers);
        Assert.True(LightLayerRouting.Illuminates(lamp, layers.Get("BG1")));
        Assert.True(LightLayerRouting.Illuminates(lamp, layers.Get("PLAYER")));
        Assert.False(LightLayerRouting.Illuminates(lamp, layers.Get("FG3")));
        Assert.True(LightLayerRouting.Occludes(lamp, new LightingOccluder { LayerId = "PLAYER" }));
        Assert.False(LightLayerRouting.Occludes(lamp, new LightingOccluder { LayerId = "BG1" }));
    }

    [Fact]
    public void ForwardMaterialSamplingHonorsSelectedPlaneWithoutAffectingOtherPlanes()
    {
        var layers = SceneLightingLayerSet.Standard11();
        var light = Light() with { AffectsLayers = new[] { "PLAYER" } };
        var art = new LightingArtPreset { Id = "test", Ambient = new LightRgb(.1, .1, .1) };
        var p = light.Position;
        var samplePlayer = LightingWorldSampler.Sample(p, LightChannel.Terrain,
            false, null, art, new[] { light }, Array.Empty<LightingOccluder>(), layers.Get("PLAYER"));
        var sampleBackground = LightingWorldSampler.Sample(p, LightChannel.Backdrop,
            false, null, art, new[] { light }, Array.Empty<LightingOccluder>(), layers.Get("BG1"));
        Assert.True(samplePlayer.R > sampleBackground.R);
        Assert.Equal(.1, sampleBackground.R, 8);
    }

    [Fact]
    public void OcclusionOfOnePlaneDoesNotPreventOtherPlaneFromReceivingLight()
    {
        var layers = SceneLightingLayerSet.Standard11();
        var art = new LightingArtPreset { Id = "test", Ambient = new LightRgb(.1, .1, .1) };
        var light = Light() with { CastShadows = true, AffectsLayers = new[] { "PLAYER", "BG1" },
            OccluderLayers = new[] { "PLAYER" } };
        var blocker = new LightingOccluder
        {
            Id = "wall", LayerId = "BG1", Vertices = new[]
            { new LightPoint(11, 9), new LightPoint(12, 9),
              new LightPoint(12, 11), new LightPoint(11, 11) }
        };
        var result = LightingWorldSampler.Sample(new LightPoint(13, 10), LightChannel.Terrain,
            true, null, art, new[] { light }, new[] { blocker }, layers.Get("PLAYER"));
        Assert.True(result.R > .1); // BG1 geometry does not cast into PLAYER for this light
    }

    [Fact]
    public void LightRemainsVisibleToCameraAtEveryApproachDistance()
    {
        var light = Light() with { Radius = 6 };
        foreach (double centerX in new[] { 3d, 6d, 9d, 10d, 11d, 14d, 17d })
        {
            var viewport = new LightingViewport(1280, 720,
                new LightPoint(centerX, 10), 32);
            Assert.True(viewport.InfluencesViewport(light));
            Assert.True(LightLayerRouting.Illuminates(light,
                SceneLightingLayerSet.Standard11().Get("PLAYER")));
        }
    }

    [Fact]
    public void FlatTestAuthoredLightsAndArtUseExplicitLayerSettings()
    {
        var catalog = SceneLightingLayerCatalog.FromJson(LayersJson());
        var scene = LdtkLightingImporter.FromJson(FlatJson(), "LIGHTING_FLAT_TEST",
            catalog.ForScene("LIGHTING_FLAT_TEST"));
        Assert.Equal(11, scene.Layers.Layers.Count);
        Assert.Equal(4, scene.Lights.Count);
        Assert.Contains(scene.Lights, x => x.AffectsLayers is not null && x.AffectsLayers.Contains("FG3"));
        Assert.Contains(scene.Surfaces, x => x.Value.LayerId == "FG3" && x.Value.ReceivesLight);
        Assert.Contains(scene.Surfaces, x => x.Value.CastsShadows);
        Assert.Contains(scene.Surfaces, x => !x.Value.CastsShadows);
        Assert.Contains(scene.Lights, x => x.OccluderLayers is not null && x.OccluderLayers.Contains("PLAYER"));
    }

    [Fact]
    public void LightingConfigurationDoesNotChangeWorldCollisionOrSpawns()
    {
        var source = FlatJson();
        var imported = LdtkLevelImporter.FromJson(source, "LIGHTING_FLAT_TEST");
        var config = SceneLightingLayerCatalog.FromJson(LayersJson());
        _ = LdtkLightingImporter.FromJson(source, "LIGHTING_FLAT_TEST",
            config.ForScene("LIGHTING_FLAT_TEST"));
        var reread = LdtkLevelImporter.FromJson(source, "LIGHTING_FLAT_TEST");
        Assert.Equal(imported.ColliderCount, reread.ColliderCount);
        Assert.Equal(imported.World.SpawnPoints.Count, reread.World.SpawnPoints.Count);
    }
}
