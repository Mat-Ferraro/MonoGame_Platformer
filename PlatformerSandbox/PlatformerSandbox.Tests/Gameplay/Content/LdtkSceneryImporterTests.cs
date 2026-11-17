using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using PlatformerSandbox.Gameplay.Content;
using Xunit;

namespace PlatformerSandbox.Tests.Gameplay.Content;

public sealed class LdtkSceneryImporterTests
{
    private static string Fixture()
    {
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 12 && directory is not null; depth++)
        {
            string file = Path.Combine(directory, "PlatformerSandbox.Tests", "Fixtures",
                "LdtkSceneryDemoBaseline.ldtk");
            if (File.Exists(file)) return File.ReadAllText(file);
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new FileNotFoundException("LdtkSceneryDemoBaseline.ldtk fixture missing.");
    }

    private static JsonNode Root() => JsonNode.Parse(Fixture())!;
    private static JsonNode FirstScenery(JsonNode root) => root["levels"]![0]!["layerInstances"]!
        .AsArray().First(layer => layer!["__type"]!.GetValue<string>() == "Entities")!
        ["entityInstances"]!.AsArray().First(item => item!["__identifier"]!.GetValue<string>() == "ScenerySprite")!;
    private static JsonNode Field(JsonNode entity, string name) => entity["fieldInstances"]!.AsArray()
        .First(f => f!["__identifier"]!.GetValue<string>() == name)!;

    [Fact]
    public void SceneryDemo_ImportsEightLargeSpritesOnThreePlanes()
    {
        var data = LdtkSceneryImporter.FromJson(Fixture());
        Assert.Equal("SCENERY_DEMO", data.LevelId);
        Assert.Equal(8, data.SpriteCount);
        Assert.Equal(3, data.Sprites.Count(s => s.Plane == LdtkVisualPlane.Background));
        Assert.Equal(2, data.Sprites.Count(s => s.Plane == LdtkVisualPlane.Terrain));
        Assert.Equal(3, data.Sprites.Count(s => s.Plane == LdtkVisualPlane.Foreground));
        Assert.Contains(data.Sprites, s => s.WidthPx > 400);
    }

    [Fact]
    public void SourceAndDisplayBounds_AreIndependent()
    {
        var s = LdtkSceneryImporter.FromJson(Fixture()).Sprites[0];
        Assert.Equal(128, s.SourceWidth);
        Assert.Equal(128, s.SourceHeight);
        Assert.True(s.WidthPx > s.SourceWidth);
        Assert.Equal(.5f, s.PivotX);
        Assert.Equal(.5f, s.PivotY);
    }

    [Fact]
    public void BackgroundAndForeground_UseIndependentParallaxFactors()
    {
        var sprites = LdtkSceneryImporter.FromJson(Fixture()).Sprites;
        var bg = sprites.First(s => s.Plane == LdtkVisualPlane.Background);
        var fg = sprites.First(s => s.Plane == LdtkVisualPlane.Foreground);
        var bgBefore = LdtkSceneryTransform.Project(bg, 0, 0);
        var bgAfter = LdtkSceneryTransform.Project(bg, 200, 100);
        Assert.Equal(bg.ScrollFactorX * 200, bgBefore.X - bgAfter.X, 4);
        Assert.Equal(bg.ScrollFactorY * 100, bgBefore.Y - bgAfter.Y, 4);
        var fgBefore = LdtkSceneryTransform.Project(fg, 0, 0);
        var fgAfter = LdtkSceneryTransform.Project(fg, 200, 100);
        Assert.True(fgBefore.X - fgAfter.X > 200);
        Assert.Equal(fg.ScrollFactorY * 100, fgBefore.Y - fgAfter.Y, 4);
    }

    [Fact]
    public void DepthThenStableId_ControlsDeterministicDrawOrder()
    {
        var sprites = LdtkSceneryImporter.FromJson(Fixture()).Sprites;
        var expected = sprites.OrderBy(s => s.Plane).ThenBy(s => s.Depth)
            .ThenBy(s => s.InstanceId, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, sprites);
    }

    [Fact]
    public void SceneryEdits_NeverChangeWorldColliderIdsOrSpawn()
    {
        JsonNode root = Root();
        var before = LdtkLevelImporter.FromJson(root.ToJsonString());
        JsonNode entity = FirstScenery(root);
        entity["px"]![0] = 2240;
        entity["width"] = 700;
        Field(entity, "RotationDeg")["__value"] = 25;
        Field(entity, "Plane")["__value"] = "Foreground";
        Field(entity, "ParallaxX")["__value"] = 40;
        var after = LdtkLevelImporter.FromJson(root.ToJsonString());
        Assert.Equal(before.World.StaticColliders.Select(c => (c.Id, c.Bounds)),
            after.World.StaticColliders.Select(c => (c.Id, c.Bounds)));
        Assert.Equal(before.World.SpawnPoints, after.World.SpawnPoints);
    }

    [Fact]
    public void ArtTileOverride_IsUsedRatherThanStaleEditorPreview()
    {
        var root = Root();
        JsonNode entity = FirstScenery(root);
        entity["__tile"]!["srcRect"]![0] = 384;
        Field(entity, "Art")["__value"]!["srcRect"]![0] = 256;
        var data = LdtkSceneryImporter.FromJson(root.ToJsonString());
        var sprite = data.Sprites.First(s => s.InstanceId == entity["iid"]!.GetValue<string>());
        Assert.Equal(256, sprite.SourceX);
    }

    [Fact]
    public void MissingArtTile_IsRejected()
    {
        var root = Root();
        var entity = FirstScenery(root);
        entity["__tile"] = null;
        Field(entity, "Art")["__value"] = null;
        Assert.Throws<ArgumentException>(() => LdtkSceneryImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void InvalidTilesetSourceRange_IsRejected()
    {
        var root = Root();
        Field(FirstScenery(root), "Art")["__value"]!["srcRect"]![0] = 500;
        Assert.Throws<ArgumentException>(() => LdtkSceneryImporter.FromJson(root.ToJsonString()));
    }

    [Theory]
    [InlineData("Plane", "Unexpected")]
    [InlineData("Plane", "background")]
    public void UnknownPlane_IsRejected(string name, string invalid)
    {
        var root = Root();
        Field(FirstScenery(root), name)["__value"] = invalid;
        Assert.Throws<ArgumentException>(() => LdtkSceneryImporter.FromJson(root.ToJsonString()));
    }

    [Theory]
    [InlineData("Opacity", 1.5)]
    [InlineData("Opacity", -0.1)]
    [InlineData("ParallaxX", -101)]
    [InlineData("ParallaxY", 201)]
    [InlineData("RotationDeg", 361)]
    public void InvalidVisualParameters_AreRejected(string name, double invalid)
    {
        var root = Root();
        Field(FirstScenery(root), name)["__value"] = invalid;
        Assert.Throws<ArgumentException>(() => LdtkSceneryImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void InvalidDisplaySize_IsRejected()
    {
        var root = Root();
        FirstScenery(root)["width"] = 0;
        Assert.Throws<ArgumentException>(() => LdtkSceneryImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void MissingTilesetReference_IsRejected()
    {
        var root = Root();
        Field(FirstScenery(root), "Art")["__value"]!["tilesetUid"] = 999999;
        Assert.Throws<ArgumentException>(() => LdtkSceneryImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void DuplicateInstanceIdentity_IsRejected()
    {
        var root = Root();
        var entities = root["levels"]![0]!["layerInstances"]!.AsArray()
            .First(l => l!["__type"]!.GetValue<string>() == "Entities")!["entityInstances"]!.AsArray();
        JsonNode first = FirstScenery(root);
        JsonNode second = entities.Last()!;
        second["iid"] = first["iid"]!.GetValue<string>();
        Assert.Throws<ArgumentException>(() => LdtkSceneryImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void LegacyParallaxProject_WithoutSceneryStillImports()
    {
        JsonNode root = Root();
        var entities = root["levels"]![0]!["layerInstances"]!.AsArray()
            .First(l => l!["__type"]!.GetValue<string>() == "Entities")!["entityInstances"]!.AsArray();
        for (int i = entities.Count - 1; i >= 0; i--)
            if (entities[i]!["__identifier"]!.GetValue<string>() == "ScenerySprite")
                entities.RemoveAt(i);
        var scenery = LdtkSceneryImporter.FromJson(root.ToJsonString());
        Assert.Empty(scenery.Sprites);
    }

    [Fact]
    public void ExistingTileLayersStillImportInSameOrder()
    {
        var visual = LdtkVisualImporter.FromJson(Fixture());
        Assert.Equal(107, visual.TileCount);
        Assert.Equal("BG_Far", visual.Layers[0].Name);
        Assert.Equal("FG_Close", visual.Layers[^1].Name);
    }

    [Fact]
    public void LevelIdentifierSelect_IsValidated()
    {
        Assert.Throws<ArgumentException>(() => LdtkSceneryImporter.FromJson(Fixture(), "MISSING"));
        Assert.Equal(8, LdtkSceneryImporter.FromJson(Fixture(), "SCENERY_DEMO").SpriteCount);
    }
}
