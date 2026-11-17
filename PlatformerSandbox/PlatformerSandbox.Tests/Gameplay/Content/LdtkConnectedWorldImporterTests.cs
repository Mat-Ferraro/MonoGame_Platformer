using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Profiles;
using Xunit;

namespace PlatformerSandbox.Tests.Gameplay.Content;

/// <summary>Headless integration contract for the editor-authored linked levels.</summary>
public sealed class LdtkConnectedWorldImporterTests
{
    private static string FixtureJson()
    {
        string? path = AppContext.BaseDirectory;
        for (int depth = 0; depth < 12 && path is not null; depth++)
        {
            string file = Path.Combine(path, "PlatformerSandbox.Tests", "Fixtures",
                "LdtkConnectedWorldBaseline.ldtk");
            if (File.Exists(file)) return File.ReadAllText(file);
            path = Directory.GetParent(path)?.FullName;
        }
        throw new FileNotFoundException("Missing connected-world LDtk regression fixture.");
    }
    private static JsonNode Root() => JsonNode.Parse(FixtureJson())!;
    private static JsonNode Entity(JsonNode root, string level, string kind, int occurrence = 0) =>
        root["levels"]!.AsArray().First(l => l!["identifier"]!.GetValue<string>() == level)!
            ["layerInstances"]!.AsArray()
            .First(l => l!["__identifier"]!.GetValue<string>() == "Entities")!
            ["entityInstances"]!.AsArray()
            .Where(e => e!["__identifier"]!.GetValue<string>() == kind)
            .Skip(occurrence).First()!;
    private static JsonNode Field(JsonNode entity, string field) =>
        entity["fieldInstances"]!.AsArray()
            .First(f => f!["__identifier"]!.GetValue<string>() == field)!;

    [Fact]
    public void BothLinkedLevelsImportWithRealAuthoredWorlds()
    {
        var catalog = LdtkConnectedWorldCatalog.FromJson(FixtureJson());
        Assert.Equal(2, catalog.Levels.Count);
        foreach (var level in catalog.Levels.Values)
        {
            Assert.Equal(136, level.Imported.SolidCellCount);
            Assert.Single(level.Imported.World.MovingPlatformTrajectories);
            Assert.Equal(4, level.Imported.World.Triggers.Count);
            Assert.True(level.Imported.World.HasSpawn("START"));
            Assert.True(level.Imported.World.HasSpawn("WEST"));
            Assert.True(level.Imported.World.HasSpawn("EAST"));
            Assert.True(level.Imported.World.HasSpawn("MID_CHECKPOINT"));
            Assert.Equal(2, level.Exits.Count);
            Assert.Single(level.WindRegions);
            Assert.Single(level.Checkpoints);
        }
    }

    [Fact]
    public void EveryExitResolvesToAnExistingLevelAndNamedEntrance()
    {
        var catalog = LdtkConnectedWorldCatalog.FromJson(FixtureJson());
        foreach (var level in catalog.Levels.Values)
            foreach (var exit in level.Exits.Values)
            {
                Assert.True(catalog.Supports(exit.Destination));
                Assert.NotEqual(level.LevelId, exit.Destination.LevelId);
                Assert.True(catalog.Require(exit.Destination).Imported.World.HasSpawn(
                    catalog.Require(exit.Destination).Entrances[exit.Destination.EntranceId]));
            }
    }

    [Fact]
    public void WindUsesSignedWalkingSpeedFractionsIncludingAssistance()
    {
        var catalog = LdtkConnectedWorldCatalog.FromJson(FixtureJson());
        Assert.Equal(-0.75, Assert.Single(catalog.Levels["CAVERN_A"].WindRegions.Values).WalkingSpeedFraction, 6);
        Assert.Equal(0.75, Assert.Single(catalog.Levels["CAVERN_B"].WindRegions.Values).WalkingSpeedFraction, 6);
    }

    [Fact]
    public void SpawnsDoNotIntersectStaticOrMovingColliders()
    {
        var catalog = LdtkConnectedWorldCatalog.FromJson(FixtureJson());
        foreach (var level in catalog.Levels.Values)
        {
            foreach (var spawn in level.Imported.World.SpawnPoints)
            {
                var b = level.Imported.World.ResolveSpawnBounds(spawn.Id, 0);
                foreach (var c in level.Imported.World.StaticColliders)
                    Assert.False(b.Left < c.Bounds.Right && b.Right > c.Bounds.Left &&
                        b.Bottom < c.Bounds.Top && b.Top > c.Bounds.Bottom);
            }
        }
    }

    [Fact]
    public void EntranceSpawnCanInitializeTheExistingAuthoritativeMotor()
    {
        var catalog = LdtkConnectedWorldCatalog.FromJson(FixtureJson());
        var linked = catalog.Require(new SceneDestination("CAVERN_A", "WEST"));
        var imported = linked.Imported with { StartSpawnId = linked.Entrances["WEST"] };
        var world = LdtkLevelGym.Create(imported,
            MovementGymSession.PlayableAssistancePreset,
            SimulationFaultSensitivity.Strict, 1, 190);
        Assert.Equal(imported.World.ResolveSpawnBounds("WEST", 0), world.GetPlayer(1).Bounds);
        Assert.Equal(1, world.Step(new[] {
            new WorldPlayerCommand(1, HorizontalDirection.Neutral) }).WorldTick);
    }

    [Fact]
    public void IdenticalImportsKeepStableColliderIdsAndRouteIdentities()
    {
        var a = LdtkConnectedWorldCatalog.FromJson(FixtureJson());
        var b = LdtkConnectedWorldCatalog.FromJson(FixtureJson());
        foreach (string levelId in a.Levels.Keys)
        {
            Assert.Equal(a.Levels[levelId].Imported.World.StaticColliders.Select(x => x.Id),
                b.Levels[levelId].Imported.World.StaticColliders.Select(x => x.Id));
            Assert.Equal(a.Levels[levelId].Exits.Keys.OrderBy(x => x),
                b.Levels[levelId].Exits.Keys.OrderBy(x => x));
        }
    }

    [Fact]
    public void IllustratedPiecesAreEditorVisibleWithoutAddingPhysics()
    {
        var catalog = LdtkConnectedWorldCatalog.FromJson(FixtureJson());
        var scenery = LdtkSceneryImporter.FromJson(FixtureJson(), "CAVERN_A");
        Assert.Equal(12, scenery.SpriteCount); // 8 legacy sprites + 4 editor-preview PaintedSprite pieces.
        Assert.Equal(4, scenery.Sprites.Count(s =>
            s.TilesetPath == "IllustratedTerrainAtlas.png"));
        Assert.Equal(9, LdtkTerrainArtworkImporter.FromJson(
            FixtureJson(), "CAVERN_A").ArtworkCount);
        Assert.Equal(136, catalog.Levels["CAVERN_A"].Imported.SolidCellCount);
    }

    [Fact]
    public void UnknownExitLevelFailsBeforeAnyWorldIsActivated()
    {
        JsonNode root = Root();
        Field(Entity(root, "CAVERN_A", "LevelExit"), "TargetLevel")["__value"] = "MISSING";
        Assert.Throws<ArgumentException>(() => LdtkConnectedWorldCatalog.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void UnknownExitEntranceFailsBeforeAnyWorldIsActivated()
    {
        JsonNode root = Root();
        Field(Entity(root, "CAVERN_A", "LevelExit"), "TargetEntrance")["__value"] = "MISSING";
        Assert.Throws<ArgumentException>(() => LdtkConnectedWorldCatalog.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void DuplicateTriggerIdsAreRejected()
    {
        JsonNode root = Root();
        int duplicate = Field(Entity(root, "CAVERN_A", "LevelExit"), "TriggerId")["__value"]!.GetValue<int>();
        Field(Entity(root, "CAVERN_A", "Checkpoint"), "TriggerId")["__value"] = duplicate;
        Assert.Throws<ArgumentException>(() => LdtkConnectedWorldCatalog.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void DuplicateEntranceNamesAreRejected()
    {
        JsonNode root = Root();
        Field(Entity(root, "CAVERN_A", "LevelEntrance", 1), "EntranceId")["__value"] = "WEST";
        Assert.Throws<ArgumentException>(() => LdtkConnectedWorldCatalog.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void OutOfRangeWindIsRejected()
    {
        JsonNode root = Root();
        Field(Entity(root, "CAVERN_A", "WindZone"), "SpeedPercent")["__value"] = 999.0;
        Assert.Throws<ArgumentException>(() => LdtkConnectedWorldCatalog.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void EntranceInsideCollisionIsRejected()
    {
        JsonNode root = Root();
        Entity(root, "CAVERN_A", "LevelEntrance")["px"]![0] = 580;
        Entity(root, "CAVERN_A", "LevelEntrance")["px"]![1] = 520;
        Assert.Throws<ArgumentException>(() => LdtkConnectedWorldCatalog.FromJson(root.ToJsonString()));
    }

    [Fact]
    public void MissingLevelIdentifierIsRejected()
    {
        var catalog = LdtkConnectedWorldCatalog.FromJson(FixtureJson());
        Assert.False(catalog.Supports(new SceneDestination("NO_SUCH_LEVEL")));
        Assert.Throws<ArgumentException>(() => catalog.Require(new SceneDestination("NO_SUCH_LEVEL")));
    }

    [Fact]
    public void DuplicateLevelIdentifiersAreRejected()
    {
        JsonNode root = Root();
        root["levels"]![1]!["identifier"] = "CAVERN_A";
        Assert.Throws<ArgumentException>(() => LdtkConnectedWorldCatalog.FromJson(root.ToJsonString()));
    }
}
