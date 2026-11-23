using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.Presentation;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>Production LDtk authoring + presentation runtime acceptance.
/// These tests do not claim GPU shader parity with the Lighting Basics lab.</summary>
public sealed class L15ProductionLightingBridgeTests
{
    private const string SceneId = "LIGHTING_INTEGRATION_TEST";
    private static string Find(string relative)
    {
        string? current = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && current is not null; i++)
        {
            string candidate = Path.Combine(current, relative);
            if (File.Exists(candidate)) return candidate;
            current = Directory.GetParent(current)?.FullName;
        }
        throw new FileNotFoundException("Stage 15 test fixture missing", relative);
    }
    private static string FilePath() => Find(Path.Combine("Content", "Levels", "LightingIntegrationTest.ldtk"));
    private static string Json() => File.ReadAllText(FilePath());
    private static LdtkLightingScene Scene() => LdtkLightingImporter.FromJson(Json(), SceneId);

    [Fact]
    public void SceneIsDiscoverableAndPreparesThroughNormalLdtkGameplayPipeline()
    {
        var catalog = LdtkContentCatalog.Discover(Path.GetDirectoryName(FilePath())!);
        Assert.DoesNotContain("LightingIntegrationTest.ldtk", catalog.RejectedProjects.Keys);
        var destination = new SceneDestination(SceneId, GameLevelIds.DefaultEntrance);
        Assert.True(catalog.Supports(destination));
        var prepared = catalog.Prepare(destination);
        Assert.Equal(SceneId, prepared.Imported.World.LevelId);
        Assert.NotNull(prepared.Lighting);
        Assert.Single(prepared.Imported.World.MovingPlatformTrajectories);
        Assert.Single(prepared.Imported.World.SpawnPoints);
    }

    [Fact]
    public void AuthoredStage15SceneIncludesIndependentPlanesAndShadowRouting()
    {
        var scene = Scene();
        Assert.Equal(4, scene.Lights.Count);
        Assert.True(scene.Occluders.Count >= 3);
        Assert.Contains(scene.Surfaces.Values, s => s.CastsShadows);
        Assert.Contains(scene.Surfaces.Values, s => !s.ReceivesLight);
        Assert.Contains(scene.Surfaces.Values, s => s.LayerId == "FG3");
        Assert.Contains(scene.Lights, l => l.CastShadows);
        Assert.All(scene.Lights, l => LightLayerRouting.ValidateRouting(l, scene.Layers));
    }

    [Fact]
    public void MotionConfigurationIsOptInAndDoesNotChangeOriginalFixture()
    {
        var scene = Scene();
        var pair = Assert.Single(scene.LightMotions);
        Assert.Equal(PresentationLightMotionAxis.Horizontal, pair.Value.Axis);
        Assert.Equal(3.0, pair.Value.DistanceWorld);
        Assert.Equal(.12, pair.Value.CyclesPerSecond);
        Assert.Contains(scene.Lights, l => l.Id == pair.Key);
        string original = File.ReadAllText(Find(Path.Combine("Content", "Levels", "LightingFlatTest.ldtk")));
        Assert.Empty(LdtkLightingImporter.FromJson(original, "LIGHTING_FLAT_TEST").LightMotions);
    }

    [Fact]
    public void PureMotionIsSmoothPeriodicAndRejectsInvalidValues()
    {
        var motion = new PresentationLightMotion(PresentationLightMotionAxis.Horizontal, 3, .25);
        var anchor = new LightPoint(10, 5);
        Assert.Equal(anchor, motion.At(anchor, 0));
        Assert.Equal(13, motion.At(anchor, 1).X, 10);
        Assert.Equal(7, motion.At(anchor, 3).X, 10);
        Assert.Equal(10, motion.At(anchor, 4).X, 10);
        Assert.Equal(anchor.Y, motion.At(anchor, 2).Y);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PresentationLightMotion(PresentationLightMotionAxis.Vertical, 0, 1).Validate());
    }

    [Theory]
    [InlineData("MotionAxis", "Diagonal")]
    [InlineData("MotionDistanceWorld", -2.0)]
    [InlineData("MotionCyclesPerSecond", 3.0)]
    public void InvalidMotionAuthoringIsRejected(string key, object invalid)
    {
        JsonNode root = JsonNode.Parse(Json())!;
        JsonArray entities = root["levels"]![0]!["layerInstances"]!.AsArray()
            .First(layer => layer!["__identifier"]!.GetValue<string>() == "Entities")!
            ["entityInstances"]!.AsArray();
        JsonNode light = entities.First(entity =>
            entity!["__identifier"]!.GetValue<string>() == "LightSource" &&
            entity["fieldInstances"]!.AsArray().Any(field =>
                field!["__identifier"]!.GetValue<string>() == "MotionAxis" &&
                field["__value"]!.GetValue<string>() == "Horizontal"))!;
        JsonNode field = light["fieldInstances"]!.AsArray().First(f =>
            f!["__identifier"]!.GetValue<string>() == key)!;
        field["__value"] = invalid is string badText ? JsonValue.Create(badText) :
            JsonValue.Create(Convert.ToDouble(invalid));
        Assert.ThrowsAny<ArgumentException>(() =>
            LdtkLightingImporter.FromJson(root.ToJsonString(), SceneId));
    }

    [Fact]
    public void PresenterAnimatesOnlyTheAuthoredMovingLightAndRespectsPauseAndReset()
    {
        LdtkLightingScene scene = Scene();
        string animatedId = Assert.Single(scene.LightMotions).Key;
        RadialLight anchor = Assert.Single(scene.Lights.Where(l => l.Id == animatedId));
        var presenter = new GameplayLightingPresenter();
        presenter.EnterWorld(11, scene);
        var view = new LightingViewport(1280, 720, anchor.Position, 32);
        RadialLight Snapshot() => Assert.Single(presenter.BuildVisible(view).Where(l => l.Id == animatedId));
        Assert.Equal(anchor.Position, Snapshot().Position);
        presenter.Update(.1, worldPaused: false);
        LightPoint moving = Snapshot().Position;
        Assert.True(moving.X > anchor.Position.X);
        Assert.Equal(anchor.Position.Y, moving.Y);
        presenter.Update(.1, worldPaused: true);
        Assert.Equal(moving, Snapshot().Position);
        Assert.Equal(anchor.Position, scene.Lights.Single(l => l.Id == animatedId).Position);
        presenter.EnterWorld(12, scene);
        Assert.Equal(anchor.Position, Snapshot().Position);
    }

    [Fact]
    public void SceneHasItsOwnDarkAndReadableProductionPresets()
    {
        var layers = SceneLightingLayerCatalog.FromJson(File.ReadAllText(Find(
            Path.Combine("Content", "Lighting", "L02_SceneLayers.json"))));
        var catalog = LightingGameCatalog.FromJson(File.ReadAllText(Find(
            Path.Combine("Content", "Lighting", "L456_GameplayLighting.json"))));
        Assert.Equal(11, layers.ForScene(SceneId).Layers.Count);
        Assert.Equal("FlatDramatic", catalog.GetDefaultPresetForScene(SceneId));
        Assert.Equal(new[] { "FlatDramatic", "FlatReadable" }, catalog.GetPresetIdsForScene(SceneId));
        var scene = LdtkLightingImporter.FromJson(Json(), SceneId, layers.ForScene(SceneId));
        Assert.All(scene.Lights, l => LightLayerRouting.ValidateRouting(l, scene.Layers));
    }
}
