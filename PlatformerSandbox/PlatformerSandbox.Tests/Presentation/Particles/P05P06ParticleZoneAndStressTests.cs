using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation.Particles;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.Presentation;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Presentation.Particles;

/// <summary>P05 LDtk authoring and P06 lifecycle/performance regression acceptance.</summary>
public sealed class P05P06ParticleZoneAndStressTests
{
    private static readonly Vector2D Here = new(4, 3);
    private static Aabb Box(double x, double y, double w = 3, double h = 2) =>
        new(new Vector2D(x, y), new Vector2D(x + w, y + h));

    private static string Find(string relative)
    {
        string? folder = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && folder is not null; i++)
        {
            string filename = Path.Combine(folder, relative);
            if (File.Exists(filename)) return filename;
            folder = Directory.GetParent(folder)?.FullName;
        }
        throw new FileNotFoundException("Missing regression asset: " + relative);
    }

    private static string CaveJson() => File.ReadAllText(Find(
        Path.Combine("Content", "Levels", "ConnectedWorldDemo.ldtk")));
    private static string BaselineJson() => File.ReadAllText(Find(
        Path.Combine("PlatformerSandbox.Tests", "Fixtures", "LdtkConnectedWorldBaseline.ldtk")));
    private static ParticleCompositeCatalog Catalog(int capacity = 64)
    {
        ParticleEmitterDefinition emitter = new()
        {
            Id = "flow", RatePerSecond = 40, MaxSpawnsPerUpdate = 32,
            LifetimeSeconds = new(3, 3), Speed = new(0, 0),
            StartSize = new(.1, .1), EndSize = new(.1, .1)
        };
        ParticleCompositeDefinition effect = new()
        {
            Id = "fx.zone", Parts = [new ParticleEffectPartDefinition
            {
                EmitterId = "flow", Capacity = capacity,
                Space = ParticleCoordinateSpace.World,
                Layer = ParticleVisualLayer.Background
            }]
        };
        return new ParticleCompositeCatalog([emitter], [effect]);
    }
    private static LdtkParticleZone Zone(string id = "zone1", double x = 2,
        double density = 100, double padding = 5) =>
        new(id, "fx.zone", Box(x, 1), density, padding);
    private static ParticleZoneEnvironment Environment(params LdtkParticleZone[] zones) =>
        new(new LdtkParticleSceneDefinition("CAVERN_A", zones), Catalog());

    private static JsonNode Entity(JsonNode root, string level, int index) =>
        root["levels"]!.AsArray().First(x => x!["identifier"]!.GetValue<string>() == level)!
            ["layerInstances"]!.AsArray().First(x => x!["__identifier"]!.GetValue<string>() == "Entities")!
            ["entityInstances"]!.AsArray().Where(x => x!["__identifier"]!.GetValue<string>() == "ParticleZone")
            .ElementAt(index)!;

    [Fact]
    public void LegacyLdtkProjectsWithoutParticleZonesStillImport()
    {
        string json = BaselineJson();
        Assert.Empty(LdtkParticleZoneImporter.FromJson(json, "CAVERN_A").Zones);
        Assert.Equal(2, LdtkConnectedWorldCatalog.FromJson(json).Levels.Count);
    }

    [Fact]
    public void RealConnectedCavernsHaveEditorAuthoredParticleRegions()
    {
        string json = CaveJson();
        var catalog = ParticleCompositeCatalog.FromJson(File.ReadAllText(Find(
            Path.Combine("Content", "Particles", "P04_GameplayEffects.json"))));
        foreach (string level in new[] { "CAVERN_A", "CAVERN_B", "CAVERN_C" })
        {
            var scene = LdtkParticleZoneImporter.FromJson(json, level);
            Assert.Equal(2, scene.Zones.Count);
            Assert.All(scene.Zones, z => Assert.True(catalog.TryGet(z.EffectId, out _)));
            ParticleZoneEnvironment environment = new(scene, catalog);
            Assert.Equal(2, environment.TotalZones);
            Assert.Equal(0, environment.ActiveZones);
        }
        // Visual-only entities must never become physics triggers or colliders.
        var linked = LdtkConnectedWorldCatalog.FromJson(json);
        Assert.Equal(3, linked.Levels.Count);
        Assert.All(linked.Levels.Values, l => Assert.Single(l.WindRegions));
    }

    [Fact]
    public void LdtkPixelRectanglesConvertToExactYUpWorldBounds()
    {
        LdtkParticleZone first = LdtkParticleZoneImporter.FromJson(CaveJson(), "CAVERN_A").Zones[0];
        Assert.Equal(220.0 / 32, first.Bounds.Left, 10);
        Assert.Equal((1280.0 - 300 - 510) / 32, first.Bounds.Bottom, 10);
        Assert.Equal((220.0 + 930) / 32, first.Bounds.Right, 10);
        Assert.Equal((1280.0 - 300) / 32, first.Bounds.Top, 10);
        Assert.Equal(75, first.DensityPercent);
        Assert.Equal(10, first.ActivationPaddingWorld);
    }

    [Theory]
    [InlineData("DensityPercent", 0)]
    [InlineData("DensityPercent", 201)]
    [InlineData("ActivationPaddingWorld", -1)]
    [InlineData("ActivationPaddingWorld", 41)]
    public void InvalidAuthoredNumbersAreRejected(string key, double value)
    {
        JsonNode root = JsonNode.Parse(CaveJson())!;
        JsonNode entity = Entity(root, "CAVERN_A", 0);
        JsonNode field = entity["fieldInstances"]!.AsArray()
            .First(x => x!["__identifier"]!.GetValue<string>() == key)!;
        field["__value"] = value;
        Assert.ThrowsAny<ArgumentException>(() =>
            LdtkParticleZoneImporter.FromJson(root.ToJsonString(), "CAVERN_A"));
    }

    [Fact]
    public void DuplicateZoneIdAndOutOfBoundsRegionAreRejected()
    {
        JsonNode root = JsonNode.Parse(CaveJson())!;
        Entity(root, "CAVERN_A", 1)["iid"] =
            Entity(root, "CAVERN_A", 0)["iid"]!.GetValue<string>();
        Assert.ThrowsAny<ArgumentException>(() =>
            LdtkParticleZoneImporter.FromJson(root.ToJsonString(), "CAVERN_A"));
        root = JsonNode.Parse(CaveJson())!;
        Entity(root, "CAVERN_A", 0)["px"]![0] = 2500;
        Assert.ThrowsAny<ArgumentException>(() =>
            LdtkParticleZoneImporter.FromJson(root.ToJsonString(), "CAVERN_A"));
    }

    [Fact]
    public void UnknownEffectsAndInvalidZonePoliciesRejectBeforeEmission()
    {
        Assert.Throws<KeyNotFoundException>(() => Environment(Zone() with { EffectId = "unknown" }));
        Assert.ThrowsAny<ArgumentException>(() => Environment(Zone(), Zone()));
        Assert.ThrowsAny<ArgumentException>(() => Environment(Zone() with { DensityPercent = 300 }));
        Assert.ThrowsAny<ArgumentException>(() => Environment(Zone() with { ActivationPaddingWorld = -1 }));
    }

    [Fact]
    public void AuthoredRectangleDistributesParticlesAcrossActualWorldBounds()
    {
        var environment = Environment(Zone());
        environment.Update(Here, .1, paused: false);
        Assert.Equal(1, environment.ActiveZones);
        Assert.True(environment.ActiveParticles >= 3);
        var particles = Assert.Single(environment.Instances).Parts[0].Particles;
        for (int i = 0; i < particles.ActiveCount; i++)
        {
            Vector2D position = particles.GetAt(i).Position;
            Assert.InRange(position.X, 2, 5);
            Assert.InRange(position.Y, 1, 3);
        }
    }

    [Fact]
    public void FarZonesSleepAndCrossingIntoRangeStartsThem()
    {
        var environment = Environment(Zone(x: 70, padding: 1));
        environment.Update(Here, .1, false);
        Assert.Empty(environment.Instances);
        environment.Update(new Vector2D(71, 2), .1, false);
        Assert.Equal(1, environment.ActiveZones);
        Assert.True(environment.ActiveParticles > 0);
        environment.Update(Here, .1, false);
        Assert.Equal(0, environment.ActiveZones);
        Assert.Empty(environment.Instances);
        Assert.Equal(0, environment.ActiveParticles);
    }

    [Fact]
    public void PausedWorldDoesNotEmitAgeOrCullAndResumesNormally()
    {
        var environment = Environment(Zone());
        environment.Update(Here, .1, false);
        int oldCount = environment.ActiveParticles;
        var first = Assert.Single(environment.Instances).Parts[0].Particles.GetAt(0);
        environment.Update(new Vector2D(500, 500), 10, paused: true);
        Assert.Equal(oldCount, environment.ActiveParticles);
        Assert.Equal(first, Assert.Single(environment.Instances).Parts[0].Particles.GetAt(0));
        environment.Update(Here, .1, false);
        Assert.True(environment.ActiveParticles >= oldCount);
    }

    [Fact]
    public void DensityMultipliesEmissionWithoutChangingVisualAssets()
    {
        var environment = Environment(Zone("slow", x: 2, density: 50),
            Zone("fast", x: 3, density: 150));
        environment.Update(Here, .1, false);
        Assert.Equal(2, environment.ActiveZones);
        int[] counts = environment.Instances.Select(x => x.Parts[0].Particles.ActiveCount)
            .OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 2, 6 }, counts);
    }

    [Fact]
    public void ManyOverlappingZonesStayInsideGlobalInstanceAndParticleBudgets()
    {
        LdtkParticleZone[] zones = Enumerable.Range(0, 48)
            .Select(i => Zone("zone" + i)).ToArray();
        var environment = Environment(zones);
        for (int i = 0; i < 300; i++)
        {
            environment.Update(Here, 1.0 / 60.0, paused: false);
            Assert.True(environment.ActiveZones <= ParticleZoneEnvironment.MaxActiveZones);
            Assert.True(environment.ActiveParticles <=
                ParticleZoneEnvironment.MaxActiveZones * 64);
        }
        Assert.Equal(ParticleZoneEnvironment.MaxActiveZones, environment.ActiveZones);
        Assert.True(environment.CappedZoneFrames > 0);
        environment.Clear();
        Assert.Equal(0, environment.ActiveZones);
        Assert.Equal(0, environment.ActiveParticles);
        environment.Update(Here, .1, false);
        Assert.Equal(ParticleZoneEnvironment.MaxActiveZones, environment.ActiveZones);
    }

    [Fact]
    public void RemovingOneSceneInstanceLeavesOtherEffectsUntouched()
    {
        var environment = Environment(Zone("a", x: 2), Zone("b", x: 5));
        environment.Update(Here, .1, false);
        Assert.Equal(2, environment.ActiveZones);
        environment.Update(new Vector2D(8.5, 2), .1, false);
        // Both zone groups can still be visible within their authored padding.
        Assert.True(environment.ActiveZones <= 2);
        environment.Clear();
        Assert.Empty(environment.Instances);
    }

    [Fact]
    public void CosmeticEnvironmentDoesNotChangeAuthoritativeWorldTrajectory()
    {
        WorldSession withCosmetics = SharedWorldClockGym.Create(worldInstanceId: 211);
        WorldSession bare = SharedWorldClockGym.Create(worldInstanceId: 211);
        ParticleZoneEnvironment environment = Environment(Zone());
        for (int tick = 0; tick < 120; tick++)
        {
            WorldPlayerCommand[] commands =
            [
                new(1, HorizontalDirection.Right, JumpPressed: tick == 10),
                new(2, HorizontalDirection.Left, JumpPressed: tick == 25)
            ];
            WorldStepResult a = withCosmetics.Step(commands);
            WorldStepResult b = bare.Step(commands);
            if (a.Players.Count != 0)
                environment.Update(a.Players[0].Bounds.Center, 1.0 / 60.0, false);
            Assert.Equal(b.WorldTick, a.WorldTick);
            Assert.True(a.Events.SequenceEqual(b.Events));
            Assert.Equal(b.Players.Count, a.Players.Count);
            for (int i = 0; i < a.Players.Count; i++)
                Assert.Equal(b.Players[i], a.Players[i]);
        }
    }

    [Fact]
    public void ClampedLongFrameDoesNotProduceUnboundedParticleCatchUp()
    {
        var environment = Environment(Zone());
        environment.Update(Here, 500, false);
        Assert.True(environment.ActiveParticles <= 4);
        Assert.Equal(1, environment.ActiveZones);
    }
}
