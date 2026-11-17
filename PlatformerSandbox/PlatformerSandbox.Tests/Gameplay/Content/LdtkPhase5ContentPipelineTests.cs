using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Diagnostics;
using Xunit;

namespace PlatformerSandbox.Tests.Gameplay.Content;

public sealed class LdtkPhase5ContentPipelineTests
{
    private static string LevelsDirectory()
    {
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && directory is not null; i++)
        {
            string path = Path.Combine(directory, "Content", "Levels");
            if (File.Exists(Path.Combine(path, "ConnectedWorldDemo.ldtk"))) return path;
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new FileNotFoundException("Phase 5 editor content was not found under Content/Levels.");
    }

    [Fact]
    public void DiscoveryExposesLegacyAliasesAndEveryAuthoredCavernWithoutGame1Registration()
    {
        LdtkContentCatalog catalog = LdtkContentCatalog.Discover(LevelsDirectory());
        Assert.Empty(catalog.RejectedProjects);
        string[] expected =
        [
            GameLevelIds.LdtkTest, GameLevelIds.LdtkParallaxDemo,
            GameLevelIds.LdtkSceneryDemo, GameLevelIds.LdtkTerrainDemo,
            "CAVERN_A", "CAVERN_B", "CAVERN_C"
        ];
        Assert.True(catalog.Levels.Count >= expected.Length);
        foreach (string id in expected)
            Assert.True(catalog.Supports(new SceneDestination(id)), id);
        Assert.Equal("Level_0", catalog.Levels[GameLevelIds.LdtkTest].Identifier);
        Assert.Equal("PARALLAX_DEMO", catalog.Levels[GameLevelIds.LdtkParallaxDemo].Identifier);
        Assert.True(catalog.Supports(new SceneDestination("CAVERN_C", "EAST")));
        Assert.False(catalog.Supports(new SceneDestination("CAVERN_C", "NO_ENTRANCE")));

        var menu = GameLevelMenuCatalog.WithDiscoveredLevels(catalog.Levels.Keys);
        Assert.Contains(menu, item => item.Destination?.LevelId == "CAVERN_C");
        Assert.Equal(1, menu.Count(item => item.Destination?.LevelId == "CAVERN_C"));
        Assert.Equal(GameLevelIds.LdtkTest, menu[0].Destination?.LevelId);
        Assert.Equal(GameLevelIds.MovementGym,
            menu.First(item => item.Destination?.LevelId == GameLevelIds.MovementGym).Destination?.LevelId);
    }

    [Fact]
    public void ThirdEditorLevelHasCollisionsIndependentArtAndValidatedBidirectionalExits()
    {
        LdtkContentCatalog registry = LdtkContentCatalog.Discover(LevelsDirectory());
        LdtkPreparedContent third = registry.Prepare(new SceneDestination("CAVERN_C", "WEST"));
        LdtkPreparedContent second = registry.Prepare(new SceneDestination("CAVERN_B"));
        Assert.Equal("CAVERN_C", third.Imported.World.LevelId);
        Assert.Equal("WEST", third.Imported.StartSpawnId);
        Assert.NotEmpty(third.Imported.World.StaticColliders);
        Assert.True(third.Imported.SolidCellCount > second.Imported.SolidCellCount);
        Assert.Single(third.Imported.World.MovingPlatformTrajectories);
        Assert.Equal(12, third.Scenery.SpriteCount);
        Assert.Equal(9, third.Terrain.ArtworkCount);
        Assert.NotEqual(
            third.Scenery.Sprites.Where(s => s.TilesetPath == "IllustratedTerrainAtlas.png")
                .Select(s => s.PixelX).OrderBy(x => x).ToArray(),
            second.Scenery.Sprites.Where(s => s.TilesetPath == "IllustratedTerrainAtlas.png")
                .Select(s => s.PixelX).OrderBy(x => x).ToArray());
        Assert.Equal(3, second.Connected.Exits.Count);
        Assert.Contains(second.Connected.Exits.Values,
            e => e.Destination.LevelId == "CAVERN_C" && e.Destination.EntranceId == "WEST");
        Assert.Equal(2, third.Connected.Exits.Count);
        Assert.Contains(third.Connected.Exits.Values, e => e.Destination.LevelId == "CAVERN_A");
        Assert.Contains(third.Connected.Exits.Values, e => e.Destination.LevelId == "CAVERN_B");

        WorldSession world = LdtkLevelGym.Create(third.Imported,
            MovementGymSession.PlayableAssistancePreset,
            SimulationFaultSensitivity.Strict, 1, 1103);
        Assert.Equal(third.Imported.World.ResolveSpawnBounds("WEST", 0), world.GetPlayer(1).Bounds);
        Assert.Equal(1, world.Step(new[]
        {
            new WorldPlayerCommand(1, HorizontalDirection.Neutral)
        }).WorldTick);
    }

    [Fact]
    public void DiscoveryIsolatesInvalidFilesAndPreservesOtherSelectableLevels()
    {
        string dir = Path.Combine(Path.GetTempPath(), "Ldtk_Phase5_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.Copy(Path.Combine(LevelsDirectory(), "FirstAttempt.ldtk"),
                Path.Combine(dir, "FirstAttempt.ldtk"));
            File.WriteAllText(Path.Combine(dir, "Broken.ldtk"), "{ not valid json");
            LdtkContentCatalog catalog = LdtkContentCatalog.Discover(dir);
            Assert.Single(catalog.Levels);
            Assert.Single(catalog.RejectedProjects);
            Assert.True(catalog.Supports(new SceneDestination(GameLevelIds.LdtkTest)));
            Assert.False(catalog.Supports(new SceneDestination("BROKEN")));
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void InvalidResavedLevelFailsBeforePreparedSceneCanReplaceActiveWorld()
    {
        string dir = Path.Combine(Path.GetTempPath(), "Ldtk_Phase5_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string file = Path.Combine(dir, "ConnectedWorldDemo.ldtk");
            File.Copy(Path.Combine(LevelsDirectory(), "ConnectedWorldDemo.ldtk"), file);
            LdtkContentCatalog catalog = LdtkContentCatalog.Discover(dir);
            var destination = new SceneDestination("CAVERN_C", "WEST");
            LdtkPreparedContent old = catalog.Prepare(destination);
            WorldSession alreadyPlaying = LdtkLevelGym.Create(old.Imported,
                MovementGymSession.PlayableAssistancePreset,
                SimulationFaultSensitivity.Strict, 1, 1104);
            var root = JsonNode.Parse(File.ReadAllText(file))!;
            var exit = root["levels"]!.AsArray().First(l => l!["identifier"]!.GetValue<string>() == "CAVERN_C")!
                ["layerInstances"]!.AsArray().First(l => l!["__identifier"]!.GetValue<string>() == "Entities")!
                ["entityInstances"]!.AsArray().First(e => e!["__identifier"]!.GetValue<string>() == "LevelExit")!;
            exit["fieldInstances"]!.AsArray().First(f => f!["__identifier"]!.GetValue<string>() == "TargetLevel")!
                ["__value"] = "DOES_NOT_EXIST";
            File.WriteAllText(file, root.ToJsonString());
            Assert.Throws<ArgumentException>(() => catalog.Prepare(destination));
            Assert.Equal(0, alreadyPlaying.WorldTick);
            Assert.Equal(old.Imported.World.ResolveSpawnBounds("WEST", 0), alreadyPlaying.GetPlayer(1).Bounds);
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void UnknownGameplayEntityIsRejectedUntilAHandlerIsRegistered()
    {
        string source = File.ReadAllText(Path.Combine(LevelsDirectory(), "ConnectedWorldDemo.ldtk"));
        var root = JsonNode.Parse(source)!;
        var entities = root["levels"]![0]!["layerInstances"]!.AsArray()
            .First(l => l!["__identifier"]!.GetValue<string>() == "Entities")!
            ["entityInstances"]!.AsArray();
        JsonNode injected = entities.First(e => e!["__identifier"]!.GetValue<string>() == "WindZone")!.DeepClone();
        injected["iid"] = Guid.NewGuid().ToString();
        injected["__identifier"] = "CustomWind";
        injected["fieldInstances"]!.AsArray()
            .First(f => f!["__identifier"]!.GetValue<string>() == "TriggerId")!
            ["__value"] = 7500;
        entities.Add(injected);
        string json = root.ToJsonString();
        Assert.Throws<NotSupportedException>(() => LdtkConnectedWorldCatalog.FromJson(json));
        LdtkEntityImportRegistry registry = LdtkEntityImportRegistry.CreateDefault();
        registry.Register("CustomWind", (ctx, entity) =>
            ctx.AddWind(entity.PositiveId("TriggerId"), entity.Bounds,
                entity.Number("SpeedPercent", -150, 150) / 100.0));
        var imported = LdtkConnectedWorldCatalog.FromJson(json, registry);
        Assert.True(imported.Levels["CAVERN_A"].WindRegions.ContainsKey(7500));
    }
}
