using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>Object-level receiver/caster independence and the L02.7 test authoring contract.</summary>
public sealed class L027SurfaceLightingParticipationTests
{
    private static string Fixture(string relative)
    {
        string? folder = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && folder is not null; i++)
        {
            string path = Path.Combine(folder, relative);
            if (File.Exists(path)) return File.ReadAllText(path);
            folder = Directory.GetParent(folder)?.FullName;
        }
        throw new FileNotFoundException("Missing L02.7 fixture: " + relative);
    }

    [Fact]
    public void SilhouetteIsDarkEvenWhenDirectLightIsWhite()
    {
        var layer = SceneLightingLayerSet.Standard11().Gameplay;
        var surface = new LightingSurfaceOptions
        {
            LightingMode = LightingSurfaceMode.Silhouette,
            ReceivesLight = false, ReceivesShadows = false, CastsShadows = true
        };
        surface.Validate();
        LightRgb tint = LightingSurfaceShading.ResolveTint(surface, layer, LightRgb.White);
        Assert.InRange(tint.R, .01, .15);
        Assert.InRange(tint.G, .01, .15);
        Assert.InRange(tint.B, .01, .15);
        Assert.True(surface.NeedsIndependentPass);
    }

    [Fact]
    public void LitCasterKeepsReceiverLightingAndUsesSeparatePass()
    {
        var layer = SceneLightingLayerSet.Standard11().Gameplay;
        var surface = new LightingSurfaceOptions
        {
            LightingMode = LightingSurfaceMode.Lit,
            ReceivesLight = true, ReceivesShadows = true, CastsShadows = true
        };
        LightRgb sample = new(.8, .34, .2);
        Assert.Equal(sample, LightingSurfaceShading.ResolveTint(surface, layer, sample));
        Assert.True(surface.NeedsIndependentPass);
    }

    [Fact]
    public void UnlitDoesNotMeanInvisibleOrShadowCaster()
    {
        var layer = SceneLightingLayerSet.Standard11().Gameplay;
        var surface = new LightingSurfaceOptions
        {
            LightingMode = LightingSurfaceMode.Unlit,
            ReceivesLight = false, CastsShadows = false
        };
        Assert.Equal(LightRgb.White,
            LightingSurfaceShading.ResolveTint(surface, layer, LightRgb.Black));
        Assert.False(surface.CastsShadows);
    }

    [Fact]
    public void InheritedModesPreserveSelectiveForegroundLighting()
    {
        var fg = SceneLightingLayerSet.Standard11().Get("FG3");
        var surface = new LightingSurfaceOptions
        {
            LayerId = "FG3", Channel = LightChannel.Effects,
            LightingMode = LightingSurfaceMode.Inherit, ReceivesLight = true
        };
        LightRgb selectiveLight = new(.25, .55, .3);
        Assert.Equal(selectiveLight,
            LightingSurfaceShading.ResolveTint(surface, fg, selectiveLight));
        Assert.False(surface.NeedsIndependentPass);
    }

    [Fact]
    public void ExplicitLitCanOverrideAnUnlitDecorativeLayer()
    {
        var layer = new SceneLightingLayer
        {
            Id = "BG_TEST", Order = -1, Group = RenderLayerGroup.Background,
            Mode = RenderLayerLightingMode.Unlit
        };
        var surface = new LightingSurfaceOptions
        {
            LayerId = "BG_TEST", Channel = LightChannel.Backdrop,
            LightingMode = LightingSurfaceMode.Lit
        };
        LightRgb light = new(.3, .6, .8);
        Assert.Equal(light, LightingSurfaceShading.ResolveTint(surface, layer, light));
        Assert.True(surface.NeedsIndependentPass);
    }

    [Fact]
    public void OwnShadowHullDoesNotDarkenItsLitFrontFaceButOccludesOtherSurfaces()
    {
        var layer = SceneLightingLayerSet.Standard11().Gameplay;
        var art = new LightingArtPreset { Id = "fixture", Ambient = new LightRgb(.05, .05, .05) };
        var light = new RadialLight
        {
            Id = "lamp", Owner = "fixture", Position = new LightPoint(0, 2),
            Radius = 8, Intensity = 1, CastShadows = true,
            AffectsLayers = new[] { "PLAYER" }, OccluderLayers = new[] { "PLAYER" }
        };
        var hull = new LightingOccluder
        {
            Id = "crate", LayerId = "PLAYER",
            Vertices = new[]
            {
                new LightPoint(1, 1), new LightPoint(3, 1),
                new LightPoint(3, 3), new LightPoint(1, 3)
            }
        };
        LightPoint face = new(4, 2);
        LightRgb ownExcluded = LightingWorldSampler.Sample(face, LightChannel.Terrain, true,
            "crate", art, new[] { light }, new[] { hull }, layer);
        LightRgb obstructed = LightingWorldSampler.Sample(face, LightChannel.Terrain, true,
            null, art, new[] { light }, new[] { hull }, layer);
        Assert.True(ownExcluded.R > obstructed.R + .01);
    }

    [Fact]
    public void InvalidLightingModeIsRejectedInsteadOfSilentlyChangingAppearance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LightingSurfaceOptions { LightingMode = (LightingSurfaceMode)99 }.Validate());
        string source = Fixture(Path.Combine("Content", "Levels", "LightingFlatTest.ldtk"));
        JsonNode root = JsonNode.Parse(source)!;
        var level = root["levels"]!.AsArray().Single(x => (string?)x!["identifier"] == "LIGHTING_FLAT_TEST")!;
        var entity = level["layerInstances"]!.AsArray()
            .SelectMany(x => x!["entityInstances"]!.AsArray())
            .First(x => (string?)x!["__identifier"] == "TerrainArtwork")!;
        var modeField = entity["fieldInstances"]!.AsArray()
            .First(x => (string?)x!["__identifier"] == "LightingMode")!;
        modeField["__value"] = "InvalidLightingMode";
        Assert.Throws<ArgumentException>(() => LdtkLightingImporter.FromJson(
            root.ToJsonString(), "LIGHTING_FLAT_TEST"));
    }

    [Fact]
    public void FlatLevelContainsLitAndSilhouetteCratesThatBothCastShadows()
    {
        string source = Fixture(Path.Combine("Content", "Levels", "LightingFlatTest.ldtk"));
        var lighting = LdtkLightingImporter.FromJson(source, "LIGHTING_FLAT_TEST");
        var artwork = LdtkTerrainArtworkImporter.FromJson(source, "LIGHTING_FLAT_TEST");
        var crates = artwork.Artworks.Where(x => x.ImagePath.EndsWith("/Crate.png", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, crates.Length);
        var options = crates.Select(x => lighting.GetSurface(x.InstanceId)).ToArray();
        Assert.Contains(options, x => x.LightingMode == LightingSurfaceMode.Lit);
        Assert.Contains(options, x => x.LightingMode == LightingSurfaceMode.Silhouette);
        Assert.All(options, x => Assert.True(x.CastsShadows));
        Assert.All(crates, x => Assert.Contains(lighting.Occluders, h => h.Id == x.InstanceId));
        var panel = artwork.Artworks.Single(x => x.ImagePath.EndsWith("/Panel.png", StringComparison.Ordinal));
        Assert.Equal(LightingSurfaceMode.Inherit, lighting.GetSurface(panel.InstanceId).LightingMode);
        Assert.False(lighting.GetSurface(panel.InstanceId).CastsShadows);
    }

    [Fact]
    public void BothLdtkProjectsExposeEditableLightingModeToArtEntities()
    {
        foreach (string file in new[] { "LightingFlatTest.ldtk", "ConnectedWorldDemo.ldtk" })
        {
            using JsonDocument doc = JsonDocument.Parse(Fixture(Path.Combine("Content", "Levels", file)));
            JsonElement entities = doc.RootElement.GetProperty("defs").GetProperty("entities");
            foreach (JsonElement entity in entities.EnumerateArray())
            {
                if (entity.GetProperty("identifier").GetString() is not ("TerrainArtwork" or "ScenerySprite" or "PaintedSprite"))
                    continue;
                Assert.Contains(entity.GetProperty("fieldDefs").EnumerateArray(),
                    f => f.GetProperty("identifier").GetString() == "LightingMode");
            }
        }
    }
}
