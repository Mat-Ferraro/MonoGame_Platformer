using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using PlatformerSandbox.Gameplay.Content;
using Xunit;

namespace PlatformerSandbox.Tests.Gameplay.Content;

public sealed class LdtkVisualLayerTests
{
    private static string LoadFixture(string name)
    {
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 12 && directory is not null; depth++)
        {
            string candidate = Path.Combine(directory,
                "PlatformerSandbox.Tests", "Fixtures", name);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new FileNotFoundException("Test fixture unavailable: " + name);
    }

    private static string Sample() => LoadFixture("LdtkParallaxDemoBaseline.ldtk");
    private static JsonNode Root() => JsonNode.Parse(Sample())!;
    private static JsonNode Layer(JsonNode root, string name) =>
        root["levels"]![0]!["layerInstances"]!.AsArray()
            .First(v => v!["__identifier"]!.GetValue<string>() == name)!;
    private static JsonNode Definition(JsonNode root, string name) =>
        root["defs"]!["layers"]!.AsArray()
            .First(v => v!["identifier"]!.GetValue<string>() == name)!;

    [Fact]
    public void OriginalAuthoredLevel_ImportsWithoutArtwork()
    {
        var definition = LdtkVisualImporter.FromJson(
            LoadFixture("LdtkFirstAttemptBaseline.ldtk"));
        Assert.Equal("Level_0", definition.LevelId);
        Assert.Empty(definition.Layers);
        Assert.Equal(0, definition.TileCount);
    }

    [Fact]
    public void ParallaxDemo_LoadsLayersInBackToFrontOrder()
    {
        var result = LdtkVisualImporter.FromJson(Sample());
        Assert.Equal("PARALLAX_DEMO", result.LevelId);
        Assert.Equal(107, result.TileCount);
        Assert.Equal(new[] { "BG_Far", "BG_Mid", "BG_Near", "Terrain_Art", "FG_Near", "FG_Close" },
            result.Layers.Select(l => l.Name).ToArray());
        Assert.Equal(new[] {
                LdtkVisualPlane.Background, LdtkVisualPlane.Background, LdtkVisualPlane.Background,
                LdtkVisualPlane.Terrain, LdtkVisualPlane.Foreground, LdtkVisualPlane.Foreground
            }, result.Layers.Select(l => l.Plane).ToArray());
        Assert.Equal(-.8f, result.Layers[0].ScrollFactorX, 3);
        Assert.Equal(.35f, result.Layers[^1].ScrollFactorX, 3);
    }

    [Fact]
    public void ChangingVisualArt_DoesNotChangeAuthoritativeGeometry()
    {
        JsonNode root = Root();
        var before = LdtkLevelImporter.FromJson(root.ToJsonString());
        Layer(root, "FG_Close")["gridTiles"]!.AsArray().Clear();
        Definition(root, "BG_Mid")["parallaxFactorX"] = -.35;
        var after = LdtkLevelImporter.FromJson(root.ToJsonString());
        Assert.Equal(before.SolidCellCount, after.SolidCellCount);
        Assert.Equal(before.World.StaticColliders.Select(c => (c.Id, c.Bounds)),
            after.World.StaticColliders.Select(c => (c.Id, c.Bounds)));
        Assert.Equal(before.World.SpawnPoints, after.World.SpawnPoints);
    }

    [Fact]
    public void IndependentVisualLayersNeverBecomeColliders()
    {
        var world = LdtkLevelImporter.FromJson(Sample());
        var visual = LdtkVisualImporter.FromJson(Sample());
        Assert.True(visual.TileCount > world.SolidCellCount / 2);
        Assert.Equal(world.SolidCellCount,
            world.World.StaticColliders.Sum(c => (int)(c.Bounds.Width * c.Bounds.Height)));
    }

    [Theory]
    [InlineData(-.8f, 40.0)]
    [InlineData(0.0f, 200.0)]
    [InlineData(.35f, 270.0)]
    public void ScrollingFactors_ChangeCameraRelativeMovement(float factor,
        double expectedDelta)
    {
        var layer = LdtkVisualImporter.FromJson(Sample()).Layers[0]
            with { ScrollFactorX = factor, ScaleWithParallax = false };
        var tile = layer.Tiles[0];
        var first = LdtkVisualTransform.Project(layer, tile, 0, 0, 1280, 720);
        var moved = LdtkVisualTransform.Project(layer, tile, 200, 0, 1280, 720);
        Assert.Equal(expectedDelta, first.X - moved.X, 4);
        Assert.Equal(tile.WidthPx, moved.Width, 4);
    }

    [Fact]
    public void ScaledParallax_PreservesCenterAnchorAndScalesSprite()
    {
        var layer = LdtkVisualImporter.FromJson(Sample()).Layers[0]
            with { ScrollFactorX = -.5f, ScrollFactorY = -.25f,
                   ScaleWithParallax = true };
        var tile = layer.Tiles[0] with { PixelX = 640, PixelY = 360 };
        var centered = LdtkVisualTransform.Project(layer, tile, 0, 0, 1280, 720);
        Assert.Equal(640, centered.X, 4);
        Assert.Equal(360, centered.Y, 4);
        Assert.Equal(64, centered.Width, 4);
        Assert.Equal(96, centered.Height, 4);
    }

    [Fact]
    public void VerticalParallax_CanBeConfiguredIndependently()
    {
        var layer = LdtkVisualImporter.FromJson(Sample()).Layers[0];
        var tile = layer.Tiles[0];
        var first = LdtkVisualTransform.Project(layer, tile, 0, 0, 1280, 720);
        var moved = LdtkVisualTransform.Project(layer, tile, 0, 200, 1280, 720);
        Assert.Equal(110, first.Y - moved.Y, 4); // -0.45 -> 55% camera motion
    }

    [Fact]
    public void MissingTilesetDefinition_IsRejected()
    {
        var root = Root();
        root["defs"]!["tilesets"]!.AsArray().Clear();
        Assert.Throws<ArgumentException>(() => LdtkVisualImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void InvalidSourceRect_IsRejected()
    {
        var root = Root();
        Layer(root, "BG_Far")["gridTiles"]![0]!["src"]![0] = 500;
        Assert.Throws<ArgumentException>(() => LdtkVisualImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void InvalidParallaxFactor_IsRejected()
    {
        var root = Root();
        Definition(root, "BG_Far")["parallaxFactorX"] = -1.5;
        Assert.Throws<ArgumentException>(() => LdtkVisualImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void UnsupportedLayerNameWithTiles_IsRejected()
    {
        var root = Root();
        Layer(root, "BG_Far")["__identifier"] = "UnknownArtLayer";
        Assert.Throws<NotSupportedException>(() => LdtkVisualImporter.FromJson(root.ToJsonString()));
    }

    [Theory]
    [InlineData("../outside.png")]
    [InlineData("C:/Outside/file.png")]
    [InlineData("sprites.bmp")]
    [InlineData("sub//image.png")]
    public void UnsafeOrUnsupportedTilesetPath_IsRejected(string path)
    {
        Assert.Throws<ArgumentException>(() => LdtkVisualImporter.ValidateRelativePath(path));
    }

    [Fact]
    public void LayerOffsetsAndTileAlpha_AreIndependentOfPhysics()
    {
        var root = Root();
        Layer(root, "FG_Close")["__pxTotalOffsetX"] = 19;
        Layer(root, "FG_Close")["gridTiles"]![0]!["a"] = .42;
        Layer(root, "FG_Close")["gridTiles"]![0]!["f"] = 3;
        var visuals = LdtkVisualImporter.FromJson(root.ToJsonString());
        var layer = visuals.Layers[^1];
        Assert.Equal(19, layer.OffsetX);
        Assert.Equal(.42f, layer.Tiles[0].Opacity, 3);
        Assert.Equal(3, layer.Tiles[0].FlipBits);
        var world = LdtkLevelImporter.FromJson(root.ToJsonString());
        Assert.NotEmpty(world.World.StaticColliders);
    }

    [Fact]
    public void NonexistentLevelSelection_FailsRatherThanImportingAnotherLevel()
    {
        Assert.Throws<ArgumentException>(() =>
            LdtkVisualImporter.FromJson(Sample(), "NOT_A_LEVEL"));
    }
}
