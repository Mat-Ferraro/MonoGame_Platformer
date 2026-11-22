using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>Headless tests for L02.5 flat-scene artistic presets and fallback behavior.</summary>
public sealed class L025DramaticContrastPresetsTests
{
    private static string ReadFixture(params string[] parts)
    {
        string relative = Path.Combine(parts);
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && directory is not null; i++)
        {
            string path = Path.Combine(directory, relative);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new FileNotFoundException("Missing L02.5 content fixture: " + relative);
    }

    private static LightingGameCatalog Catalog() => LightingGameCatalog.FromJson(
        ReadFixture("Content", "Lighting", "L456_GameplayLighting.json"));

    private static SceneLightingLayerSet FlatLayers() => SceneLightingLayerCatalog.FromJson(
        ReadFixture("Content", "Lighting", "L02_SceneLayers.json"))
        .ForScene("LIGHTING_FLAT_TEST");

    [Fact]
    public void FlatTestDefaultsToDramaticAndCyclesOnlyTwoPresets()
    {
        LightingGameCatalog catalog = Catalog();
        Assert.Equal("FlatDramatic", catalog.GetDefaultPresetForScene("LIGHTING_FLAT_TEST"));
        Assert.Equal(new[] { "FlatDramatic", "FlatReadable" },
            catalog.GetPresetIdsForScene("LIGHTING_FLAT_TEST"));
        Assert.NotEqual(catalog.GetPreset("FlatDramatic"), catalog.GetPreset("FlatReadable"));
    }

    [Fact]
    public void CavernsAndUnknownScenesRetainOriginalPresets()
    {
        LightingGameCatalog catalog = Catalog();
        string[] defaults = { "Illustrated", "Moonlit", "Graphic", "Readable" };
        Assert.Equal(defaults, catalog.PresetIds);
        Assert.Equal(defaults, catalog.GetPresetIdsForScene("CAVERN_A"));
        Assert.Equal("Illustrated", catalog.GetDefaultPresetForScene("CAVERN_A"));
        Assert.DoesNotContain("FlatDramatic", catalog.GetPresetIdsForScene("CAVERN_C"));
    }

    [Fact]
    public void DramaticUsesDarkerAmbientStrongerLightAndTighterRadius()
    {
        var catalog = Catalog();
        LightingArtPreset dramatic = catalog.GetPreset("FlatDramatic");
        LightingArtPreset readable = catalog.GetPreset("FlatReadable");
        Assert.True(dramatic.Ambient.R < readable.Ambient.R * .5);
        Assert.True(dramatic.Ambient.G < readable.Ambient.G * .5);
        Assert.True(dramatic.LightMultiplier > readable.LightMultiplier);
        Assert.True(dramatic.LightRadiusScale < readable.LightRadiusScale);
        Assert.True(dramatic.HeroMinimumBrightness < readable.HeroMinimumBrightness);
    }

    [Fact]
    public void LayerAmbientIsIndependentlyControlledByEachPreset()
    {
        var catalog = Catalog();
        var layers = FlatLayers();
        LightingArtPreset dramatic = catalog.GetPreset("FlatDramatic");
        LightingArtPreset readable = catalog.GetPreset("FlatReadable");
        var p = new LightPoint(20, 20);
        LightRgb dramaticBackground = LightingWorldSampler.Sample(p, LightChannel.Backdrop,
            false, null, dramatic, Array.Empty<RadialLight>(), Array.Empty<LightingOccluder>(),
            layers.Get("BG1"));
        LightRgb readableBackground = LightingWorldSampler.Sample(p, LightChannel.Backdrop,
            false, null, readable, Array.Empty<RadialLight>(), Array.Empty<LightingOccluder>(),
            layers.Get("BG1"));
        LightRgb dramaticGameplay = LightingWorldSampler.Sample(p, LightChannel.Hero,
            false, null, dramatic, Array.Empty<RadialLight>(), Array.Empty<LightingOccluder>(),
            layers.Get("PLAYER"));
        LightRgb dramaticVines = LightingWorldSampler.Sample(p, LightChannel.Effects,
            false, null, dramatic, Array.Empty<RadialLight>(), Array.Empty<LightingOccluder>(),
            layers.Get("FG3"));
        Assert.True(dramaticBackground.R < readableBackground.R * .5);
        Assert.True(dramaticGameplay.R > dramaticBackground.R);
        Assert.True(dramaticVines.R < dramaticGameplay.R * .1);
    }

    [Fact]
    public void StrongLocalLightCreatesVisibleContrastInDarkPreset()
    {
        var art = Catalog().GetPreset("FlatDramatic");
        var layer = FlatLayers().Get("BG1");
        var light = new RadialLight
        {
            Id = "test_lamp", Owner = "scene", Position = new LightPoint(10, 10),
            Radius = 8, Intensity = 1.2, Color = new LightRgb(1, .8, .5),
            AffectsLayers = new[] { "BG1" }
        };
        LightRgb near = LightingWorldSampler.Sample(light.Position, LightChannel.Backdrop,
            false, null, art, new[] { light }, Array.Empty<LightingOccluder>(), layer);
        LightRgb far = LightingWorldSampler.Sample(new LightPoint(30, 30), LightChannel.Backdrop,
            false, null, art, new[] { light }, Array.Empty<LightingOccluder>(), layer);
        Assert.True(near.R > far.R * 3);
        Assert.True(near.G > far.G * 3);
    }

    [Fact]
    public void RadiusScaleShrinksLightPoolInBothModesOfForwardSampling()
    {
        var catalog = Catalog();
        var layer = FlatLayers().Get("PLAYER");
        var light = new RadialLight
        {
            Id = "radius_test", Owner = "scene", Position = new LightPoint(0, 0),
            Radius = 8, Intensity = 1, Color = LightRgb.White,
            AffectsLayers = new[] { "PLAYER" }
        };
        var point = new LightPoint(7, 0);
        foreach (string presetId in new[] { "FlatDramatic", "FlatReadable" })
        {
            var art = catalog.GetPreset(presetId);
            LightRgb result = LightingWorldSampler.Sample(point, LightChannel.Hero,
                false, null, art, new[] { light }, Array.Empty<LightingOccluder>(), layer);
            LightRgb ambient = LightingWorldSampler.Sample(point, LightChannel.Hero,
                false, null, art, Array.Empty<RadialLight>(), Array.Empty<LightingOccluder>(), layer);
            if (presetId == "FlatDramatic") Assert.Equal(ambient.R, result.R, 10);
            else Assert.True(result.R > ambient.R);
        }
    }

    [Fact]
    public void LightingChannelsRemainSeparateEvenInDramaticMode()
    {
        var art = Catalog().GetPreset("FlatDramatic");
        var layers = FlatLayers();
        var light = new RadialLight
        {
            Id = "selective", Owner = "scene", Position = new LightPoint(1, 1),
            Radius = 6, Intensity = 1, Color = LightRgb.White,
            AffectsLayers = new[] { "PLAYER" }
        };
        LightRgb fgWithLight = LightingWorldSampler.Sample(light.Position, LightChannel.Effects,
            false, null, art, new[] { light }, Array.Empty<LightingOccluder>(), layers.Get("FG3"));
        LightRgb fgAmbient = LightingWorldSampler.Sample(light.Position, LightChannel.Effects,
            false, null, art, Array.Empty<RadialLight>(), Array.Empty<LightingOccluder>(), layers.Get("FG3"));
        Assert.Equal(fgAmbient.R, fgWithLight.R, 10);
        Assert.Equal(fgAmbient.G, fgWithLight.G, 10);
    }

    [Fact]
    public void InvalidArtisticScalesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LightingArtPreset { Id = "bad", LightRadiusScale = .2 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LightingArtPreset { Id = "bad", HeroMinimumBrightness = .9 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LightingArtPreset { Id = "bad", LayerAmbientScales =
                new Dictionary<string, double> { ["BG1"] = double.NaN } }.Validate());
    }

    [Fact]
    public void UnknownOrDuplicatePresetSelectionsCannotBeLoaded()
    {
        string valid = ReadFixture("Content", "Lighting", "L456_GameplayLighting.json");
        // Replace only the scene selection's default; leave global Illustrated intact.
        string unknown = valid.Replace("\"defaultPreset\": \"FlatDramatic\"",
            "\"defaultPreset\": \"missing_preset\"");
        Assert.Throws<ArgumentException>(() => LightingGameCatalog.FromJson(unknown));
        string duplicate = valid.Replace("\"FlatDramatic\",\n        \"FlatReadable\"",
            "\"FlatDramatic\",\n        \"FlatDramatic\"");
        Assert.NotEqual(valid, duplicate);
        Assert.Throws<ArgumentException>(() => LightingGameCatalog.FromJson(duplicate));
    }

    [Fact]
    public void PresetsDoNotRewriteFlatLdtkLightPositionsOrSurfaceFlags()
    {
        var source = ReadFixture("Content", "Levels", "LightingFlatTest.ldtk");
        var layerCatalog = SceneLightingLayerCatalog.FromJson(
            ReadFixture("Content", "Lighting", "L02_SceneLayers.json"));
        var scene = LdtkLightingImporter.FromJson(source, "LIGHTING_FLAT_TEST",
            layerCatalog.ForScene("LIGHTING_FLAT_TEST"));
        Assert.Equal(4, scene.Lights.Count);
        Assert.Contains(scene.Surfaces.Values, x => x.CastsShadows);
        Assert.Contains(scene.Surfaces.Values, x => !x.ReceivesLight);
        Assert.Contains(scene.Lights, x => x.AffectsLayers is not null &&
            x.AffectsLayers.Contains("FG3"));
    }
}
