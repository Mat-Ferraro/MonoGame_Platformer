using System;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L19CMaterialShowcaseAmplificationTests
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
        throw new FileNotFoundException("Missing Phase 19C fixture: " + relative);
    }

    [Fact]
    public void MaterialShowcaseKeepsLegacyLightingIntegrationHidden()
    {
        Assert.Contains(GameLevelMenuCatalog.Items,
            item => item.Destination?.LevelId == GameLevelIds.MaterialShowcaseTest);
        Assert.DoesNotContain(GameLevelMenuCatalog.Items,
            item => item.Destination?.LevelId == GameLevelIds.LightingIntegrationTest);
        Assert.True(GameLevelIds.IsStandalonePlayableLevel(GameLevelIds.LightingIntegrationTest));
    }

    [Fact]
    public void AllAuthoredPointLightIntensitiesRespectImporterRange()
    {
        string json = File.ReadAllText(Find(Path.Combine("Content", "Levels", "MaterialShowcaseTest.ldtk")));
        using var document = System.Text.Json.JsonDocument.Parse(json);
        int checkedLights = 0;
        foreach (var layer in document.RootElement.GetProperty("levels")[0]
                     .GetProperty("layerInstances").EnumerateArray())
        foreach (var entity in layer.GetProperty("entityInstances").EnumerateArray())
        {
            if (entity.GetProperty("__identifier").GetString() != "LightSource") continue;
            checkedLights++;
            foreach (var field in entity.GetProperty("fieldInstances").EnumerateArray())
                if (field.GetProperty("__identifier").GetString() == "Intensity")
                    Assert.InRange(field.GetProperty("__value").GetDouble(), 0, 4);
        }
        Assert.Equal(3, checkedLights);
    }

    [Fact]
    public void MaterialShowcaseAmplifiesEmissiveStationsAndRetainsOpacityExamples()
    {
        var json = File.ReadAllText(Find(Path.Combine("Content", "Levels", "MaterialShowcaseTest.ldtk")));
        LdtkLightingScene scene = LdtkLightingImporter.FromJson(json, GameLevelIds.MaterialShowcaseTest);
        var surfaces = scene.Surfaces.Values.ToArray();

        Assert.True(surfaces.Count(s => s.EmissionIntensity >= 5) >= 3);
        Assert.Contains(surfaces, s => !s.CastsShadows && s.MaterialShading == ProductionSurfaceShading.Flat);

        var transparent = surfaces.Where(s => !s.CastsShadows).ToArray();
        Assert.True(transparent.Length >= 4);
    }
}
