using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using PlatformerSandbox.Gameplay.Content;
using Xunit;

namespace PlatformerSandbox.Tests.Gameplay.Content;

public sealed class LdtkTerrainArtworkImporterTests
{
    private static string Fixture(string name = "LdtkTerrainDemoBaseline.ldtk")
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            string path = Path.Combine(dir, "PlatformerSandbox.Tests", "Fixtures", name);
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException("LDtk terrain baseline fixture not found: " + name);
    }

    private static JsonNode Root() => JsonNode.Parse(Fixture())!;
    private static JsonArray Entities(JsonNode root) => root["levels"]![0]!["layerInstances"]!
        .AsArray().First(x => x!["__type"]!.GetValue<string>() == "Entities")!
        ["entityInstances"]!.AsArray();
    private static JsonNode FirstArt(JsonNode root) => Entities(root)
        .First(x => x!["__identifier"]!.GetValue<string>() == "TerrainArtwork")!;
    private static JsonNode Field(JsonNode entity, string name) => entity["fieldInstances"]!
        .AsArray().First(x => x!["__identifier"]!.GetValue<string>() == name)!;

    [Fact]
    public void Demo_ImportsNineIndependentlyPlacedPngs_AndHidesBackingByDefault()
    {
        var value = LdtkTerrainArtworkImporter.FromJson(Fixture());
        Assert.Equal("TERRAIN_DEMO", value.LevelId);
        Assert.False(value.DrawCollisionBackdrop);
        Assert.Equal(9, value.ArtworkCount);
        Assert.Equal(6, value.Artworks.Count(a => a.Plane == LdtkVisualPlane.Terrain));
        Assert.Equal(1, value.Artworks.Count(a => a.Plane == LdtkVisualPlane.Background));
        Assert.Equal(2, value.Artworks.Count(a => a.Plane == LdtkVisualPlane.Foreground));
        Assert.Contains(value.Artworks, a => a.WidthPx > 1000);
        Assert.All(value.Artworks, a => Assert.EndsWith(".png", a.ImagePath));
    }

    [Fact]
    public void OlderProjects_DefaultToVisibleGrayboxBackdrop()
    {
        var root = Root();
        root["levels"]![0]!["fieldInstances"]!.AsArray().Clear();
        var entities = Entities(root);
        for (int i = entities.Count - 1; i >= 0; i--)
            if (entities[i]!["__identifier"]!.GetValue<string>() == "TerrainArtwork")
                entities.RemoveAt(i);
        var result = LdtkTerrainArtworkImporter.FromJson(root.ToJsonString());
        Assert.True(result.DrawCollisionBackdrop);
        Assert.Empty(result.Artworks);
    }

    [Fact]
    public void ArtworkChanges_NeverAlterCollisionIdsGeometryOrSpawn()
    {
        var root = Root();
        var before = LdtkLevelImporter.FromJson(root.ToJsonString());
        FirstArt(root)["px"]![0] = 1900;
        FirstArt(root)["width"] = 500;
        Field(FirstArt(root), "Image")["__value"] = "TerrainArt/RockShelfC.png";
        Field(FirstArt(root), "RotationDeg")["__value"] = 45;
        var after = LdtkLevelImporter.FromJson(root.ToJsonString());
        Assert.Equal(before.World.StaticColliders.Select(c => (c.Id, c.Bounds)),
            after.World.StaticColliders.Select(c => (c.Id, c.Bounds)));
        Assert.Equal(before.World.SpawnPoints, after.World.SpawnPoints);
    }

    [Fact]
    public void ImportingArtwork_DoesNotModifyExistingTilesOrScenery()
    {
        var tiles = LdtkVisualImporter.FromJson(Fixture());
        var scenery = LdtkSceneryImporter.FromJson(Fixture());
        Assert.True(tiles.TileCount > 0);
        Assert.Equal(8, scenery.SpriteCount);
        Assert.Equal("TERRAIN_DEMO", tiles.LevelId);
    }

    [Fact]
    public void Sorting_UsesPlaneThenDepthThenStableInstanceIdentity()
    {
        var a = LdtkTerrainArtworkImporter.FromJson(Fixture()).Artworks;
        var ordered = a.OrderBy(x => x.Plane).ThenBy(x => x.Depth)
            .ThenBy(x => x.InstanceId, StringComparer.Ordinal);
        Assert.Equal(ordered, a);
    }

    [Fact]
    public void CameraProject_UsesIndependentParallaxFactors()
    {
        var a = LdtkTerrainArtworkImporter.FromJson(Fixture()).Artworks;
        var bg = a.First(x => x.Plane == LdtkVisualPlane.Background);
        var solid = a.First(x => x.Plane == LdtkVisualPlane.Terrain);
        var start = LdtkTerrainArtworkTransform.Project(bg, 0, 0);
        var after = LdtkTerrainArtworkTransform.Project(bg, 200, 100);
        Assert.Equal(bg.ScrollFactorX * 200, start.X - after.X, 4);
        Assert.Equal(bg.ScrollFactorY * 100, start.Y - after.Y, 4);
        Assert.Equal(1f, solid.ScrollFactorX);
        Assert.Equal(1f, solid.ScrollFactorY);
    }

    [Theory]
    [InlineData("../secret.png")]
    [InlineData("C:/Windows/system.png")]
    [InlineData("/absolute.png")]
    [InlineData("TerrainArt/test.jpg")]
    [InlineData("TerrainArt//file.png")]
    public void UnsafeOrNonPngPaths_AreRejected(string path)
    {
        JsonNode root = Root();
        Field(FirstArt(root), "Image")["__value"] = path;
        Assert.Throws<ArgumentException>(() => LdtkTerrainArtworkImporter.FromJson(root.ToJsonString()));
    }

    [Theory]
    [InlineData("Opacity", -0.01)]
    [InlineData("Opacity", 1.01)]
    [InlineData("RotationDeg", 361)]
    [InlineData("ParallaxX", -101)]
    [InlineData("ParallaxY", 201)]
    public void InvalidArtworkParameters_AreRejected(string name, double value)
    {
        var root = Root();
        Field(FirstArt(root), name)["__value"] = value;
        Assert.Throws<ArgumentException>(() => LdtkTerrainArtworkImporter.FromJson(root.ToJsonString()));
    }

    [Theory]
    [InlineData("Solid")]
    [InlineData("background")]
    public void InvalidPlane_IsRejected(string plane)
    {
        var root = Root();
        Field(FirstArt(root), "Plane")["__value"] = plane;
        Assert.Throws<ArgumentException>(() => LdtkTerrainArtworkImporter.FromJson(root.ToJsonString()));
    }

    [Theory]
    [InlineData("white")]
    [InlineData("#XYZXYZ")]
    [InlineData("#1234")]
    public void InvalidTint_IsRejected(string tint)
    {
        var root = Root();
        Field(FirstArt(root), "Tint")["__value"] = tint;
        Assert.Throws<ArgumentException>(() => LdtkTerrainArtworkImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void DuplicateArtworkIdentity_IsRejected()
    {
        var root = Root();
        var art = Entities(root).Where(e => e!["__identifier"]!.GetValue<string>() == "TerrainArtwork")
            .ToArray();
        art[1]!["iid"] = art[0]!["iid"]!.GetValue<string>();
        Assert.Throws<ArgumentException>(() => LdtkTerrainArtworkImporter.FromJson(root.ToJsonString()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8193)]
    public void InvalidDisplaySize_IsRejected(int width)
    {
        var root = Root();
        FirstArt(root)["width"] = width;
        Assert.Throws<ArgumentException>(() => LdtkTerrainArtworkImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void ArtOnlyLevel_RejectsRemovalOfAllArtwork()
    {
        var root = Root();
        var entities = Entities(root);
        for (int i = entities.Count - 1; i >= 0; i--)
            if (entities[i]!["__identifier"]!.GetValue<string>() == "TerrainArtwork")
                entities.RemoveAt(i);
        Assert.Throws<ArgumentException>(() => LdtkTerrainArtworkImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void InvalidBackdropType_IsRejected()
    {
        var root = Root();
        root["levels"]![0]!["fieldInstances"]![0]!["__value"] = "false";
        Assert.Throws<ArgumentException>(() => LdtkTerrainArtworkImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void MissingImage_IsRejected()
    {
        var root = Root();
        Field(FirstArt(root), "Image")["__value"] = "";
        Assert.Throws<ArgumentException>(() => LdtkTerrainArtworkImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void MissingSelectedLevel_IsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            LdtkTerrainArtworkImporter.FromJson(Fixture(), "DOES_NOT_EXIST"));
        Assert.Equal(9, LdtkTerrainArtworkImporter.FromJson(Fixture(), "TERRAIN_DEMO").ArtworkCount);
    }
}
