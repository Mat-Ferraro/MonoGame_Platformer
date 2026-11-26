using System;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L19BMaterialShowcaseTests
{
    private static string Find(string relative)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            string path = Path.Combine(dir, relative);
            if (File.Exists(path)) return path;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException("Missing Phase 19B fixture: " + relative);
    }

    [Fact]
    public void MaterialShowcaseIsSelectableAndRetiredIntegrationStillPlayable()
    {
        Assert.True(GameLevelIds.IsStandalonePlayableLevel(GameLevelIds.MaterialShowcaseTest));
        Assert.Contains(GameLevelMenuCatalog.Items,
            item => item.Destination?.LevelId == GameLevelIds.MaterialShowcaseTest);
        Assert.DoesNotContain(GameLevelMenuCatalog.Items,
            item => item.Destination?.LevelId == GameLevelIds.LightingIntegrationTest);
        Assert.True(GameLevelIds.IsStandalonePlayableLevel(GameLevelIds.LightingIntegrationTest));
        Assert.Contains(GameLevelMenuCatalog.WithDiscoveredLevels(new[]
        {
            GameLevelIds.LightingIntegrationTest,
            GameLevelIds.MaterialShowcaseTest
        }), item => item.Destination?.LevelId == GameLevelIds.MaterialShowcaseTest);
    }

    [Fact]
    public void IsolatedFixtureSeparatesOpacityShadowAndEmission()
    {
        var json = File.ReadAllText(Find(Path.Combine("Content", "Levels", "MaterialShowcaseTest.ldtk")));
        LdtkLightingScene scene = LdtkLightingImporter.FromJson(json, GameLevelIds.MaterialShowcaseTest);
        Assert.Equal(3, scene.Lights.Count);
        Assert.Empty(scene.OccluderMotions);
        Assert.Equal(2, scene.Occluders.Count);
        Assert.Equal(0, scene.LightMotions.Count);

        var materials = scene.Surfaces.Values.ToArray();
        Assert.Contains(materials, s => s.MaterialShading == ProductionSurfaceShading.Flat &&
            s.EmissionIntensity > 3 && !s.CastsShadows);
        Assert.Contains(materials, s => s.MaterialShading == ProductionSurfaceShading.Flat &&
            s.EmissionIntensity == 0 && !s.CastsShadows);
        Assert.Contains(materials, s => s.MaterialShading == ProductionSurfaceShading.Legacy &&
            !s.ReceivesLight && !s.CastsShadows);
        Assert.Contains(materials, s => s.CastsShadows && s.MaterialShading == ProductionSurfaceShading.Flat);
    }

    [Fact]
    public void VisualOpacityIsIndependentFromCasterIdentity()
    {
        var solid = ProductionSurfaceSubmission.From("solid", new LightingSurfaceOptions
        {
            CastsShadows = true, MaterialShading = ProductionSurfaceShading.Flat
        }) with { VisualOpacity = .35 };
        solid.Validate();
        Assert.Equal(.35, solid.VisualOpacity);
        Assert.True(solid.CastsShadows);
        Assert.Equal("solid", solid.CasterId);

        var glass = ProductionSurfaceSubmission.From("glass", new LightingSurfaceOptions
        {
            CastsShadows = false, MaterialShading = ProductionSurfaceShading.Flat
        }) with { VisualOpacity = .48 };
        glass.Validate();
        Assert.Equal(.48, glass.VisualOpacity);
        Assert.False(glass.CastsShadows);
        Assert.Null(glass.CasterId);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (glass with { VisualOpacity = double.NaN }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (glass with { VisualOpacity = 1.1 }).Validate());
    }

    [Fact]
    public void AllNewMaterialArtworkAndEmissionMapsExist()
    {
        string file = Find(Path.Combine("Content", "Levels", "MaterialShowcaseTest.ldtk"));
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
        var level = document.RootElement.GetProperty("levels")[0];
        var layers = level.GetProperty("layerInstances");
        foreach (var layer in layers.EnumerateArray())
        foreach (var entity in layer.GetProperty("entityInstances").EnumerateArray())
        {
            if (entity.GetProperty("__identifier").GetString() != "TerrainArtwork") continue;
            foreach (var field in entity.GetProperty("fieldInstances").EnumerateArray())
            {
                string? id = field.GetProperty("__identifier").GetString();
                if (id is not ("Image" or "EmissionMap")) continue;
                string? path = field.GetProperty("__value").GetString();
                if (path is null) continue;
                Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(file)!,
                    path.Replace('/', Path.DirectorySeparatorChar))), path);
            }
        }
    }
}
