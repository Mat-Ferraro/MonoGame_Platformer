using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Profiles;
using Xunit;

namespace PlatformerSandbox.Tests.Gameplay.Content;

public sealed class LdtkLevelImporterTests
{
    // Frozen regression asset is distinct from editable Content/Levels file.
    private static string FixtureJson()
    {
        string? path = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && path is not null; i++)
        {
            string candidate = Path.Combine(path,
                "PlatformerSandbox.Tests", "Fixtures", "LdtkFirstAttemptBaseline.ldtk");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            path = Directory.GetParent(path)?.FullName;
        }
        throw new FileNotFoundException("Missing PlatformerSandbox.Tests/Fixtures baseline LDtk file.");
    }

    private static JsonNode Root() => JsonNode.Parse(FixtureJson())!;
    private static JsonNode Layer(JsonNode root, string id) =>
        root["levels"]![0]!["layerInstances"]!.AsArray()
            .First(layer => layer!["__identifier"]!.GetValue<string>() == id)!;

    [Fact]
    public void UploadedLevel_ImportsWithKnownAuthoringFacts()
    {
        var level = LdtkLevelImporter.FromJson(FixtureJson());
        Assert.Equal("Level_0", level.World.LevelId);
        Assert.Equal(1280, level.WidthPx);
        Assert.Equal(640, level.HeightPx);
        Assert.Equal(32, level.GridSizePx);
        Assert.Equal(40, level.ColumnCount);
        Assert.Equal(20, level.RowCount);
        Assert.Equal(85, level.SolidCellCount);
        Assert.Equal(21, level.ColliderCount);
        Assert.Equal("START", level.StartSpawnId);
        Assert.Single(level.World.SpawnPoints);
    }

    [Fact]
    public void SpawnMarkerPlacesActualMotorFeetAboveSolidTerrain()
    {
        var level = LdtkLevelImporter.FromJson(FixtureJson());
        var b = level.World.ResolveSpawnBounds("START", 0);
        Assert.Equal(10.1, b.Left, 6);
        Assert.Equal(14.0, b.Bottom, 6);
        Assert.Equal(.8, b.Width, 6);
        Assert.Equal(1.6, b.Height, 6);
        Assert.All(level.World.StaticColliders,
            c => Assert.False(b.Left < c.Bounds.Right && b.Right > c.Bounds.Left &&
                b.Bottom < c.Bounds.Top && b.Top > c.Bounds.Bottom));
    }

    [Fact]
    public void MergedCollidersCoverExactlyTheOriginalSolidArea()
    {
        var level = LdtkLevelImporter.FromJson(FixtureJson());
        double area = level.World.StaticColliders.Sum(c => c.Bounds.Width * c.Bounds.Height);
        Assert.Equal(85.0, area, 6);
        Assert.Equal(level.World.StaticColliders.Count,
            level.World.StaticColliders.Select(c => c.Id).Distinct().Count());
    }

    [Fact]
    public void IdenticalJsonProducesIdenticalStableGeometryAndIds()
    {
        string json = FixtureJson();
        var a = LdtkLevelImporter.FromJson(json);
        var b = LdtkLevelImporter.FromJson(json);
        Assert.Equal(a.World.StaticColliders.Select(c => (c.Id, c.Bounds)),
            b.World.StaticColliders.Select(c => (c.Id, c.Bounds)));
    }

    [Fact]
    public void DifferentLevelIdentifierIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            LdtkLevelImporter.FromJson(FixtureJson(), "NO_SUCH_LEVEL"));
    }

    [Fact]
    public void ExternalLevelsAreNotSilentlyAccepted()
    {
        JsonNode root = Root(); root["externalLevels"] = true;
        Assert.Throws<NotSupportedException>(() => LdtkLevelImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void MissingCollisionLayerFailsBeforeWorldConstruction()
    {
        JsonNode root = Root();
        root["levels"]![0]!["layerInstances"]!.AsArray().Remove(Layer(root, "Collisions"));
        Assert.Throws<ArgumentException>(() => LdtkLevelImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void MissingSpawnIsRejected()
    {
        JsonNode root = Root();
        Layer(root, "Entities")["entityInstances"]!.AsArray().Clear();
        Assert.Throws<ArgumentException>(() => LdtkLevelImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void DuplicateSpawnIsRejectedInsteadOfPickingAnArbitraryOne()
    {
        JsonNode root = Root();
        var entities = Layer(root, "Entities")["entityInstances"]!.AsArray();
        entities.Add(entities[0]!.DeepClone());
        Assert.Throws<NotSupportedException>(() => LdtkLevelImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void SpawnOutsideLevelIsRejected()
    {
        JsonNode root = Root();
        Layer(root, "Entities")["entityInstances"]![0]!["px"]![0] = 9999;
        Assert.Throws<ArgumentException>(() => LdtkLevelImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void SpawnOverlappingSolidCellIsRejected()
    {
        JsonNode root = Root();
        // This marker's footprint intersects the vertical wall at row 1, column 3.
        Layer(root, "Entities")["entityInstances"]![0]!["px"]![0] = 96;
        Layer(root, "Entities")["entityInstances"]![0]!["px"]![1] = 32;
        Assert.Throws<ArgumentException>(() => LdtkLevelImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void UnknownIntGridValueIsRejected()
    {
        JsonNode root = Root();
        Layer(root, "Collisions")["intGridCsv"]![0] = 2;
        Assert.Throws<NotSupportedException>(() => LdtkLevelImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void ShortGridIsRejected()
    {
        JsonNode root = Root();
        Layer(root, "Collisions")["intGridCsv"]!.AsArray().RemoveAt(0);
        Assert.Throws<ArgumentException>(() => LdtkLevelImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void PhysicsLayerOffsetsAreRejected()
    {
        JsonNode root = Root();
        Layer(root, "Collisions")["__pxTotalOffsetX"] = 16;
        Assert.Throws<NotSupportedException>(() => LdtkLevelImporter.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void ImportedWorldUsesExistingAuthoritativeMotor()
    {
        var level = LdtkLevelImporter.FromJson(FixtureJson());
        WorldSession world = LdtkLevelGym.Create(level,
            MovementGymSession.PlayableAssistancePreset,
            SimulationFaultSensitivity.Strict, 1, 50);
        Assert.Single(world.PlayerSlots);
        Assert.Equal(level.World.ResolveSpawnBounds("START", 0), world.GetPlayer(1).Bounds);
        var result = world.Step(new[] { new WorldPlayerCommand(1, HorizontalDirection.Neutral) });
        Assert.Equal(1, result.WorldTick);
    }
}
