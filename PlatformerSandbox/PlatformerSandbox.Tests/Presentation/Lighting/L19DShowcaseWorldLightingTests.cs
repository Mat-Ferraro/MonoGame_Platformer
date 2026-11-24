using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L19DShowcaseWorldLightingTests
{
    private static string Find(string relative)
    {
        string? path = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && path is not null; i++)
        {
            string file = Path.Combine(path, relative);
            if (File.Exists(file)) return file;
            path = Directory.GetParent(path)?.FullName;
        }
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void ShowcaseHasThreeShadowCastingPointLightsAndTwoVisibleCasters()
    {
        string json = File.ReadAllText(Find(Path.Combine("Content", "Levels", "MaterialShowcaseTest.ldtk")));
        LdtkLightingScene scene = LdtkLightingImporter.FromJson(json, GameLevelIds.MaterialShowcaseTest);
        Assert.Equal(3, scene.Lights.Count);
        Assert.All(scene.Lights, light => Assert.True(light.CastShadows));
        Assert.Equal(2, scene.Occluders.Count);
        Assert.Contains(scene.Surfaces.Values, s => s.CastsShadows);
        Assert.Equal(0, scene.OccluderMotions.Count);
        Assert.Equal(0, scene.LightMotions.Count);
    }

    [Fact]
    public void ActualWallReceiversAreLitAndShadowSensitiveWhileGlassAndGhostDoNotCast()
    {
        string json = File.ReadAllText(Find(Path.Combine("Content", "Levels", "MaterialShowcaseTest.ldtk")));
        using JsonDocument doc = JsonDocument.Parse(json);
        var entities = doc.RootElement.GetProperty("levels")[0].GetProperty("layerInstances")
            .EnumerateArray().Where(l => l.GetProperty("__type").GetString() == "Entities")
            .SelectMany(l => l.GetProperty("entityInstances").EnumerateArray());
        int receivers = 0, translucent = 0, casts = 0;
        foreach (var entity in entities)
        {
            if (entity.GetProperty("__identifier").GetString() != "TerrainArtwork") continue;
            var fields = entity.GetProperty("fieldInstances").EnumerateArray().ToDictionary(
                field => field.GetProperty("__identifier").GetString()!,
                field => field.GetProperty("__value"));
            string image = fields["Image"].GetString() ?? "";
            if (image.Contains("L19D_") && image.Contains("Wall.png"))
            {
                receivers++;
                Assert.True(fields["ReceivesLight"].GetBoolean());
                Assert.True(fields["ReceivesShadows"].GetBoolean());
                Assert.False(fields["CastsShadows"].GetBoolean());
            }
            if (image.Contains("L19B_BlueGlass.png") || image.Contains("L19B_Ghost.png"))
            {
                translucent++;
                Assert.False(fields["CastsShadows"].GetBoolean());
                Assert.InRange(fields["Opacity"].GetDouble(), 0.35, 0.7);
            }
            if (fields["CastsShadows"].GetBoolean()) casts++;
        }
        Assert.Equal(3, receivers);
        Assert.Equal(2, translucent);
        Assert.Equal(2, casts);
    }
}
