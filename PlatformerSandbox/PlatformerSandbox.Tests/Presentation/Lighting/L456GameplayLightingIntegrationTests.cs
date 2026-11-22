using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Abilities;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.Presentation;
using PlatformerSandbox.Gameplay.World;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>Headless scene authoring, quality budgets, shadow policies and event lifecycle.</summary>
public sealed class L456GameplayLightingIntegrationTests
{
    private static string Find(string relative)
    {
        string? folder = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && folder is not null; i++)
        {
            string file = Path.Combine(folder, relative);
            if (File.Exists(file)) return file;
            folder = Directory.GetParent(folder)?.FullName;
        }
        throw new FileNotFoundException("Missing lighting test asset: " + relative);
    }
    private static string CavernJson() => File.ReadAllText(Find(
        Path.Combine("Content", "Levels", "ConnectedWorldDemo.ldtk")));
    private static string SettingsJson() => File.ReadAllText(Find(
        Path.Combine("Content", "Lighting", "L456_GameplayLighting.json")));
    private static LightingGameCatalog Settings() => LightingGameCatalog.FromJson(SettingsJson());
    private static RadialLight Lamp(string id, double x, double y, int priority = 0,
        bool shadow = true, string owner = "level") => new()
    {
        Id = id, Position = new(x, y), Color = new(1, .7, .4), Radius = 5,
        Intensity = 1.4, Priority = priority, CastShadows = shadow, Owner = owner
    };
    private static LightingViewport View() => new(1280, 720, new(0, 0), 32);
    private static JsonNode Entity(JsonNode root, string level, string kind, int index = 0) =>
        root["levels"]!.AsArray().First(x => x!["identifier"]!.GetValue<string>() == level)!
            ["layerInstances"]!.AsArray().First(x => x!["__identifier"]!.GetValue<string>() == "Entities")!
            ["entityInstances"]!.AsArray().Where(x => x!["__identifier"]!.GetValue<string>() == kind)
            .ElementAt(index)!;
    private static void Field(JsonNode entity, string field, JsonNode? value)
    {
        entity["fieldInstances"]!.AsArray()
            .First(x => x!["__identifier"]!.GetValue<string>() == field)!["__value"] = value;
    }
    private static WorldGameplayEvent Event(long world, long tick, int sequence,
        WorldGameplayEventKind kind = WorldGameplayEventKind.AbilityActivated) =>
        new(world, tick, sequence, kind, 1, new Vector2D(4, 3),
            AbilityId: CharacterAbilityId.Pulse);

    [Fact]
    public void CombinedCavernsImportTwoLightSourcesAndShadowCastingArtwork()
    {
        string json = CavernJson();
        foreach (string id in new[] { "CAVERN_A", "CAVERN_B", "CAVERN_C" })
        {
            LdtkLightingScene scene = LdtkLightingImporter.FromJson(json, id);
            Assert.Equal(2, scene.Lights.Count);
            Assert.True(scene.Occluders.Count >= 1);
            Assert.Contains(scene.Surfaces.Values, x => x.CastsShadows);
            Assert.All(scene.Lights, light => light.Validate());
            Assert.Equal(scene.Surfaces.Count, scene.SurfaceCenters.Count);
        }
        // Lighting entities are presentation data, not movement collision or triggers.
        var connected = LdtkConnectedWorldCatalog.FromJson(json);
        Assert.Equal(3, connected.Levels.Count);
        Assert.All(connected.Levels.Values, level => Assert.Single(level.WindRegions));
    }

    [Fact]
    public void LegacyLdtkWithoutLightEntitiesOrLightingFieldsRemainsLoadable()
    {
        string json = File.ReadAllText(Find(Path.Combine(
            "PlatformerSandbox.Tests", "Fixtures", "LdtkConnectedWorldBaseline.ldtk")));
        LdtkLightingScene scene = LdtkLightingImporter.FromJson(json, "CAVERN_A");
        Assert.Empty(scene.Lights);
        Assert.Empty(scene.Occluders);
    }

    [Fact]
    public void LightPositionUsesLevelPixelHeightAndCenter()
    {
        var light = LdtkLightingImporter.FromJson(CavernJson(), "CAVERN_A").Lights[0];
        Assert.Equal((356.0 + 12) / 32, light.Position.X, 10);
        Assert.Equal((1280.0 - 422 - 12) / 32, light.Position.Y, 10);
        Assert.Equal(6.8, light.Radius, 10);
    }

    [Theory]
    [InlineData("RadiusWorld", -1)]
    [InlineData("RadiusWorld", 61)]
    [InlineData("Intensity", -1)]
    [InlineData("Intensity", 5)]
    [InlineData("Priority", 200)]
    public void InvalidLightValuesFailImport(string field, double value)
    {
        JsonNode json = JsonNode.Parse(CavernJson())!;
        Field(Entity(json, "CAVERN_A", "LightSource"), field, JsonValue.Create(value));
        Assert.ThrowsAny<ArgumentException>(() =>
            LdtkLightingImporter.FromJson(json.ToJsonString(), "CAVERN_A"));
    }

    [Fact]
    public void InvalidLightingColorAndBooleanFailImport()
    {
        JsonNode json = JsonNode.Parse(CavernJson())!;
        Field(Entity(json, "CAVERN_A", "LightSource"), "Color", JsonValue.Create("blue"));
        Assert.ThrowsAny<ArgumentException>(() =>
            LdtkLightingImporter.FromJson(json.ToJsonString(), "CAVERN_A"));
        json = JsonNode.Parse(CavernJson())!;
        Field(Entity(json, "CAVERN_A", "PaintedSprite"), "CastsShadows", JsonValue.Create(1));
        Assert.ThrowsAny<ArgumentException>(() =>
            LdtkLightingImporter.FromJson(json.ToJsonString(), "CAVERN_A"));
    }

    [Fact]
    public void DuplicateAuthoredIdentifiersAreRejected()
    {
        JsonNode json = JsonNode.Parse(CavernJson())!;
        Entity(json, "CAVERN_A", "LightSource", 1)["iid"] =
            Entity(json, "CAVERN_A", "LightSource", 0)["iid"]!.GetValue<string>();
        Assert.ThrowsAny<ArgumentException>(() =>
            LdtkLightingImporter.FromJson(json.ToJsonString(), "CAVERN_A"));
    }

    [Fact]
    public void AuthoredShadowFlagsRemainIndependent()
    {
        JsonNode json = JsonNode.Parse(CavernJson())!;
        JsonNode sprite = Entity(json, "CAVERN_A", "PaintedSprite", 1);
        Field(sprite, "CastsShadows", JsonValue.Create(false));
        Field(sprite, "ReceivesLight", JsonValue.Create(true));
        Field(sprite, "ReceivesShadows", JsonValue.Create(false));
        Field(sprite, "ContactShadow", JsonValue.Create(true));
        var scene = LdtkLightingImporter.FromJson(json.ToJsonString(), "CAVERN_A");
        string iid = sprite["iid"]!.GetValue<string>();
        LightingSurfaceOptions options = scene.GetSurface(iid);
        Assert.False(options.CastsShadows);
        Assert.True(options.ReceivesLight);
        Assert.False(options.ReceivesShadows);
        Assert.True(options.ContactShadow);
        Assert.DoesNotContain(scene.Occluders, hull => hull.Id == iid);
    }

    [Fact]
    public void ShadowReceiveAndCastingFlagsChooseIndependentComposition()
    {
        // A non-shadow-receiving textured object must not be darkened by a
        // backdrop mask merely because it is not an occluder.
        Assert.False(new LightingSurfaceOptions().NeedsIndependentPass);
        Assert.True(new LightingSurfaceOptions { ReceivesShadows = false }.NeedsIndependentPass);
        Assert.True(new LightingSurfaceOptions { ReceivesLight = false }.NeedsIndependentPass);
        Assert.True(new LightingSurfaceOptions { CastsShadows = true }.NeedsIndependentPass);
        Assert.True(new LightingSurfaceOptions { ContactShadow = true }.NeedsIndependentPass);
    }

    [Fact]
    public void ASelectedTexturedArtworkCreatesFiniteShadowHull()
    {
        var scene = LdtkLightingImporter.FromJson(CavernJson(), "CAVERN_A");
        var hull = Assert.Single(scene.Occluders);
        Assert.Equal(4, hull.Vertices.Length);
        hull.Validate();
        Assert.True(Math.Abs(ShadowGeometry.SignedDoubleArea(hull.Vertices)) > .01);
    }

    [Fact]
    public void GameplayLightingPresetsAreEditableAndDistinct()
    {
        LightingGameCatalog catalog = Settings();
        Assert.Contains("Illustrated", catalog.PresetIds);
        Assert.Contains("Moonlit", catalog.PresetIds);
        Assert.Contains("Graphic", catalog.PresetIds);
        Assert.Contains("Readable", catalog.PresetIds);
        Assert.NotEqual(catalog.GetPreset("Illustrated").Ambient,
            catalog.GetPreset("Moonlit").Ambient);
        Assert.True(catalog.GetPreset("Readable").Ambient.R >
            catalog.GetPreset("Moonlit").Ambient.R);
    }

    [Fact]
    public void DuplicatePresetIdsAndInvalidBrightnessAreRejected()
    {
        JsonNode json = JsonNode.Parse(SettingsJson())!;
        json["presets"]![1]!["id"] = "Illustrated";
        Assert.ThrowsAny<ArgumentException>(() => LightingGameCatalog.FromJson(json.ToJsonString()));
        json = JsonNode.Parse(SettingsJson())!;
        json["presets"]![0]!["lightMultiplier"] = 9;
        Assert.ThrowsAny<ArgumentException>(() => LightingGameCatalog.FromJson(json.ToJsonString()));
    }

    [Fact]
    public void ParticleBindingsAreOptionalAndHaveKnownRealEffectIds()
    {
        var settings = Settings();
        Assert.NotNull(settings.ForParticle("fx.zoneEmbers"));
        Assert.Null(settings.ForParticle("fx.zoneDust"));
        Assert.False(settings.ForParticle("fx.zoneEmbers")!.CastShadows);
        string effects = File.ReadAllText(Find(Path.Combine("Content", "Particles", "P04_GameplayEffects.json")));
        var particleCatalog = PlatformerSandbox.Foundation.Presentation.Particles.ParticleCompositeCatalog.FromJson(effects);
        foreach (LightingParticleBinding binding in settings.ParticleLights)
            Assert.True(particleCatalog.TryGet(binding.EffectId, out _));
    }

    [Fact]
    public void QualityProfilesDegradeShadowsBeforeLights()
    {
        LightingFrameBudget low = LightingFrameBudget.For(LightingQuality.Low);
        LightingFrameBudget med = LightingFrameBudget.For(LightingQuality.Balanced);
        LightingFrameBudget high = LightingFrameBudget.For(LightingQuality.High);
        Assert.Equal(0, low.MaxShadowLights);
        Assert.True(low.MaxLights < med.MaxLights && med.MaxLights < high.MaxLights);
        Assert.True(med.MaxShadowLights < high.MaxShadowLights);
        Assert.InRange(high.ShadowRadialSamples, 32, 512);
    }

    [Fact]
    public void PlannerHonorsPriorityAndShadowBudgets()
    {
        var list = Enumerable.Range(0, 9)
            .Select(i => Lamp("light_" + i, 0, 0, priority: i, shadow: true)).ToArray();
        LightingFrameBudget budget = LightingFrameBudget.For(LightingQuality.Balanced);
        var visible = LightingFramePlanner.Select(list, View(), budget, false);
        Assert.Equal(budget.MaxLights, visible.Count);
        Assert.Equal("light_8", visible[0].Id);
        Assert.Equal(budget.MaxShadowLights, visible.Count(x => x.CastShadows));
    }

    [Fact]
    public void PlannerSuppressesOptionalFlashIntensityForAccessibility()
    {
        RadialLight flash = Lamp("flash", 0, 0, 5, owner: "flash");
        var usual = LightingFramePlanner.Select([flash], View(),
            LightingFrameBudget.For(LightingQuality.Balanced), false);
        var reduced = LightingFramePlanner.Select([flash], View(),
            LightingFrameBudget.For(LightingQuality.Balanced), true);
        Assert.Equal(usual[0].Intensity * .25, reduced[0].Intensity, 10);
    }

    [Fact]
    public void WorldPointSampleIsShadowedByABlockerAndNotByItself()
    {
        LightingArtPreset preset = Settings().GetPreset("Illustrated");
        LightingOccluder box = new() { Id = "artwork", Vertices =
            [new(1, -1), new(2, -1), new(2, 1), new(1, 1)] };
        RadialLight light = Lamp("lamp", 0, 0);
        LightRgb shadow = LightingWorldSampler.Sample(new(3, 0), LightChannel.Terrain,
            true, null, preset, [light], [box]);
        LightRgb unshadowed = LightingWorldSampler.Sample(new(3, 0), LightChannel.Terrain,
            false, null, preset, [light], [box]);
        Assert.True(unshadowed.R > shadow.R);
        LightRgb caster = LightingWorldSampler.Sample(new(1.5, 0), LightChannel.Terrain,
            true, "artwork", preset, [light], [box]);
        Assert.True(caster.R > shadow.R);
    }

    [Fact]
    public void TransientLightsRequireConfirmedMatchingWorldEvents()
    {
        var scene = LdtkLightingImporter.FromJson(CavernJson(), "CAVERN_A");
        var runtime = new GameplayLightingPresenter();
        runtime.EnterWorld(91, scene);
        runtime.Present(Event(92, 1, 1));
        Assert.Equal(0, runtime.ActiveFlashes);
        runtime.Present(Event(91, 1, 1));
        Assert.Equal(1, runtime.ActiveFlashes);
        runtime.Present(Event(91, 1, 1));
        Assert.Equal(1, runtime.ActiveFlashes);
        runtime.Present(Event(91, 1, 2, WorldGameplayEventKind.AbilityRejected));
        Assert.Equal(1, runtime.ActiveFlashes);
    }

    [Fact]
    public void PausingFreezesFlashesAndResumingExpiresThem()
    {
        var runtime = new GameplayLightingPresenter();
        runtime.EnterWorld(91, LdtkLightingImporter.FromJson(CavernJson(), "CAVERN_A"));
        runtime.Present(Event(91, 1, 1));
        runtime.Update(5, worldPaused: true);
        Assert.Equal(1, runtime.ActiveFlashes);
        runtime.Update(.1, worldPaused: false);
        Assert.Equal(1, runtime.ActiveFlashes);
        runtime.Update(.1, worldPaused: false);
        runtime.Update(.1, worldPaused: false);
        Assert.Equal(0, runtime.ActiveFlashes);
    }

    [Fact]
    public void WorldReplacementDisposesTransientLightState()
    {
        var scene = LdtkLightingImporter.FromJson(CavernJson(), "CAVERN_A");
        var runtime = new GameplayLightingPresenter();
        runtime.EnterWorld(91, scene);
        runtime.Present(Event(91, 1, 1));
        runtime.EnterWorld(92, scene);
        Assert.Equal(0, runtime.ActiveFlashes);
        runtime.Present(Event(91, 1, 2));
        Assert.Equal(0, runtime.ActiveFlashes);
        runtime.Present(Event(92, 1, 1));
        Assert.Equal(1, runtime.ActiveFlashes);
        runtime.Clear();
        Assert.Equal(0, runtime.SceneLightCount);
    }

    [Fact]
    public void RuntimeParticleLightsRemainBoundedByQualityProfile()
    {
        var runtime = new GameplayLightingPresenter { Quality = LightingQuality.Low };
        runtime.EnterWorld(91, LdtkLightingImporter.FromJson(CavernJson(), "CAVERN_A"));
        var particles = Enumerable.Range(0, 20).Select(i =>
            Lamp("particle_" + i, 0, 0, owner: "particle"));
        var frame = runtime.BuildVisible(View(), particles);
        Assert.InRange(frame.Count, 1, 4);
        Assert.True(frame.Count(x => x.Owner == "particle") <= 1);
        Assert.DoesNotContain(frame, x => x.CastShadows);
    }

    [Fact]
    public void DenseStressCandidatesStayWithinFrameBudgets()
    {
        var runtime = new GameplayLightingPresenter { Quality = LightingQuality.High };
        runtime.EnterWorld(91, LdtkLightingImporter.FromJson(CavernJson(), "CAVERN_A"));
        var stress = Enumerable.Range(0, 120).Select(i =>
            Lamp("stress_" + i, i % 8 * .25, i / 8 * .2,
                priority: -25, shadow: true, owner: "stress"));
        IReadOnlyList<RadialLight> frame = runtime.BuildVisible(View(), debugLights: stress);
        LightingFrameBudget budget = LightingFrameBudget.For(LightingQuality.High);
        Assert.True(frame.Count <= budget.MaxLights);
        Assert.True(frame.Count(x => x.CastShadows) <= budget.MaxShadowLights);
        Assert.Equal(frame.Count, frame.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public void ReferenceCameraProjectionMatchesGameWorldYUpContract()
    {
        var view = View();
        Assert.Equal(640, view.Project(new LightPoint(0, 0)).X, 8);
        Assert.Equal(360, view.Project(new LightPoint(0, 0)).Y, 8);
        Assert.Equal(320, view.Project(new LightPoint(-10, 0)).X, 8);
        Assert.Equal(40, view.Project(new LightPoint(0, 10)).Y, 8);
    }
}
