using System;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation.Particles;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Particles;

public sealed class P01ParticleFoundationTests
{
    private static readonly Vector2D Origin = Vector2D.Zero;

    private static ParticleEmitterDefinition Emitter(int burst = 0, double rate = 10) => new()
    {
        Id = "test.unit",
        BurstCount = burst,
        RatePerSecond = rate,
        MaxSpawnsPerUpdate = 128,
        MaxEmissionDeltaSeconds = 1,
        Shape = ParticleSpawnShape.Box,
        SpawnOffsetX = new(-1, 2),
        SpawnOffsetY = new(-2, 1),
        LifetimeSeconds = new(2, 5),
        Speed = new(.25, 3),
        DirectionRadians = new(-Math.PI, Math.PI),
        RotationRadians = new(-2, 2),
        AngularVelocityRadiansPerSecond = new(-1, 1),
        StartSize = new(.5, 1),
        EndSize = new(.1, 2),
        StartColor = new(1, .5, .25, .75),
        EndColor = new(.25, .5, 1, 0)
    };

    private static ParticleSpawn Spawn(double x = 0, double lifetime = 2,
        double vx = 0, double ax = 0) => new(
        new Vector2D(x, 0), new Vector2D(vx, 0),
        new Vector2D(ax, 0), lifetime, 2, 0,
        new ParticleColor(1, .5, 0, 1),
        new ParticleColor(0, .5, 1, 0),
        RotationRadians: .4,
        AngularVelocityRadiansPerSecond: 2);

    [Fact]
    public void Motion_UsesConstantAccelerationAndInterpolatesSizeColorAndRotation()
    {
        var system = new ParticleSystem(4, maxUpdateDeltaSeconds: 1);
        var spawn = new ParticleSpawn(new Vector2D(10, 4),
            new Vector2D(2, 3), new Vector2D(0, -4),
            2, 2, 0, new ParticleColor(1, 0, 0, 1),
            new ParticleColor(0, 0, 1, 0), .25, 2);
        Assert.True(system.TrySpawn(spawn));
        system.Update(.5);
        ParticleState p = system.GetAt(0);
        Assert.Equal(11, p.Position.X, 10);
        Assert.Equal(5, p.Position.Y, 10);
        Assert.Equal(2, p.Velocity.X, 10);
        Assert.Equal(1, p.Velocity.Y, 10);
        Assert.Equal(.5, p.AgeSeconds, 10);
        Assert.Equal(.25, p.NormalizedAge, 10);
        Assert.Equal(1.5, p.Size, 10);
        Assert.Equal(1.25, p.RotationRadians, 10);
        Assert.Equal(.75, p.Color.R, 10);
        Assert.Equal(.25, p.Color.B, 10);
        Assert.Equal(.75, p.Color.A, 10);
    }

    [Fact]
    public void ExpiryOccursAtExactLifetimeBoundaryAndAgeIsAlwaysClamped()
    {
        var system = new ParticleSystem(2, maxUpdateDeltaSeconds: 1);
        Assert.True(system.TrySpawn(Spawn(lifetime: .5)));
        system.Update(.499);
        Assert.Equal(1, system.ActiveCount);
        Assert.InRange(system.GetAt(0).NormalizedAge, .997, .999);
        system.Update(.001);
        Assert.Equal(0, system.ActiveCount);
        Assert.Equal(1, system.Diagnostics.Expired);
        // A default/uninitialized snapshot safely clamps to its expired endpoint.
        Assert.Equal(1.0, default(ParticleState).NormalizedAge);
    }

    [Fact]
    public void PoolNeverOverwritesLivingParticlesAndKeepsStableSpawnOrder()
    {
        var system = new ParticleSystem(2, maxUpdateDeltaSeconds: 1);
        Assert.True(system.TrySpawn(Spawn(x: 1, lifetime: .1)));
        Assert.True(system.TrySpawn(Spawn(x: 2, lifetime: 2)));
        Assert.False(system.TrySpawn(Spawn(x: 3)));
        Assert.Equal(1, system.Diagnostics.DroppedAtCapacity);
        system.Update(.1);
        Assert.Equal(1, system.ActiveCount);
        Assert.Equal(2, system.GetAt(0).Position.X);
        Assert.True(system.TrySpawn(Spawn(x: 3)));
        Assert.Equal(2, system.GetAt(0).Position.X);
        Assert.Equal(3, system.GetAt(1).Position.X);
        Assert.Throws<ArgumentOutOfRangeException>(() => system.GetAt(2));
    }

    [Fact]
    public void WorldUpdateIsBoundedAfterHitchAndZeroTimeDoesNotAdvance()
    {
        var slow = new ParticleSystem(2, maxUpdateDeltaSeconds: .04);
        var bounded = new ParticleSystem(2, maxUpdateDeltaSeconds: .04);
        slow.TrySpawn(Spawn(x: 10, vx: 3));
        bounded.TrySpawn(Spawn(x: 10, vx: 3));
        slow.Update(60);
        bounded.Update(.04);
        Assert.Equal(bounded.GetAt(0), slow.GetAt(0));
        ParticleState before = slow.GetAt(0);
        slow.Update(0);
        Assert.Equal(before, slow.GetAt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => slow.Update(-.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => slow.Update(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => slow.Update(double.PositiveInfinity));
    }

    [Fact]
    public void BurstAndContinuousEmittersHaveSeparateExplicitPaths()
    {
        var system = new ParticleSystem(20);
        var emitter = new ParticleEmitter(system, Emitter(burst: 5, rate: 0), seed: 3);
        Assert.Equal(5, emitter.Burst(Origin));
        Assert.Equal(0, emitter.Update(Origin, .5));
        Assert.Equal(5, system.ActiveCount);
        Assert.Equal(5, system.Diagnostics.Spawned);
    }

    [Fact]
    public void FractionalEmissionIsConsistentAcrossSmallTimesteps()
    {
        var system = new ParticleSystem(50);
        var def = Emitter(rate: 7.5);
        var emitter = new ParticleEmitter(system, def, seed: 3);
        for (int i = 0; i < 10; i++) emitter.Update(Origin, .1);
        Assert.Equal(7, system.ActiveCount);
        Assert.Equal(.5, emitter.FractionalEmissionCarry, 8);
        emitter.Update(Origin, .1);
        Assert.Equal(8, system.ActiveCount);
    }

    [Fact]
    public void HitchCapsContinuousSpawnsAndRecordsAllSkippedOpportunities()
    {
        var system = new ParticleSystem(100);
        var def = Emitter(rate: 100) with
        {
            MaxEmissionDeltaSeconds = .05,
            MaxSpawnsPerUpdate = 3
        };
        var emitter = new ParticleEmitter(system, def, seed: 1);
        Assert.Equal(3, emitter.Update(Origin, 10));
        Assert.Equal(997, system.Diagnostics.SkippedEmissionOpportunities);
        Assert.Equal(3, system.Diagnostics.Spawned);
        Assert.Equal(0, emitter.FractionalEmissionCarry, 8);
        Assert.Equal(1, emitter.Update(Origin, .01));
        Assert.Equal(4, system.ActiveCount);
    }

    [Fact]
    public void BurstCapDropsExcessRatherThanDelayingTheBurst()
    {
        var system = new ParticleSystem(100);
        var emitter = new ParticleEmitter(system, Emitter(burst: 9, rate: 0) with
        {
            MaxSpawnsPerUpdate = 4
        });
        Assert.Equal(4, emitter.Burst(Origin));
        Assert.Equal(5, system.Diagnostics.SkippedEmissionOpportunities);
        Assert.Equal(4, system.ActiveCount);
        emitter.Stop();
        Assert.Equal(0, emitter.Burst(Origin));
        Assert.Equal(0, emitter.Update(Origin, 100));
    }

    [Fact]
    public void PerEmitterSeedsReplayIdenticallyWithoutSharedRngState()
    {
        var a = new ParticleSystem(100);
        var b = new ParticleSystem(100);
        var c = new ParticleSystem(100);
        var def = Emitter(burst: 30, rate: 0);
        new ParticleEmitter(a, def, 77).Burst(new Vector2D(10, 20));
        new ParticleEmitter(b, def, 77).Burst(new Vector2D(10, 20));
        new ParticleEmitter(c, def, 78).Burst(new Vector2D(10, 20));
        Assert.Equal(30, a.ActiveCount);
        for (int i = 0; i < a.ActiveCount; i++)
            Assert.Equal(a.GetAt(i), b.GetAt(i));
        Assert.NotEqual(a.GetAt(0), c.GetAt(0));
    }

    [Fact]
    public void RestartCanResetEmitterRandomSequenceAndEmissionCarry()
    {
        var system = new ParticleSystem(30);
        var emitter = new ParticleEmitter(system, Emitter(burst: 2, rate: 1), seed: 99);
        emitter.Burst(Origin);
        ParticleState first = system.GetAt(0);
        emitter.Update(Origin, .5);
        Assert.Equal(.5, emitter.FractionalEmissionCarry, 8);
        emitter.Stop();
        system.Clear();
        emitter.Restart(resetRandom: true);
        Assert.Equal(0, emitter.FractionalEmissionCarry);
        emitter.Burst(Origin);
        Assert.Equal(first, system.GetAt(0));
    }

    [Fact]
    public void StrictAndRecoveringFaultPoliciesHandleInvalidSpawnsDifferently()
    {
        var invalid = Spawn(lifetime: 0);
        var strict = new ParticleSystem(1, faultPolicy: ParticleFaultPolicy.Strict);
        var resilient = new ParticleSystem(1, faultPolicy: ParticleFaultPolicy.RecoverAndCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => strict.TrySpawn(invalid));
        Assert.False(resilient.TrySpawn(invalid));
        Assert.Equal(1, resilient.Diagnostics.DroppedInvalid);
        Assert.Equal(0, resilient.ActiveCount);
    }

    [Fact]
    public void StrictNonfiniteMotionFailsAndReleasePolicyDropsOnlyCosmetic()
    {
        ParticleSpawn extreme = Spawn(x: double.MaxValue * .9,
            lifetime: 10, vx: double.MaxValue);
        var strict = new ParticleSystem(2, maxUpdateDeltaSeconds: 1);
        strict.TrySpawn(extreme);
        Assert.Throws<InvalidOperationException>(() => strict.Update(.5));
        var release = new ParticleSystem(2, maxUpdateDeltaSeconds: 1,
            faultPolicy: ParticleFaultPolicy.RecoverAndCount);
        release.TrySpawn(extreme);
        release.TrySpawn(Spawn(x: 10, lifetime: 10, vx: 1));
        release.Update(.5);
        Assert.Equal(1, release.ActiveCount);
        Assert.Equal(10.5, release.GetAt(0).Position.X, 8);
        Assert.Equal(1, release.Diagnostics.DroppedInvalid);
    }

    [Fact]
    public void RecoveringEmitterDropsNonfiniteDerivedCoordinates()
    {
        var def = Emitter(burst: 1, rate: 0) with
        {
            Shape = ParticleSpawnShape.Point,
            SpawnOffsetX = new(double.MaxValue, double.MaxValue),
            SpawnOffsetY = new(0, 0)
        };
        var release = new ParticleSystem(4, faultPolicy: ParticleFaultPolicy.RecoverAndCount);
        Assert.Equal(0, new ParticleEmitter(release, def).Burst(new Vector2D(double.MaxValue, 0)));
        Assert.Equal(1, release.Diagnostics.DroppedInvalid);
        var strict = new ParticleSystem(4);
        Assert.Throws<InvalidOperationException>(() =>
            new ParticleEmitter(strict, def).Burst(new Vector2D(double.MaxValue, 0)));
    }

    [Fact]
    public void ConfigurationRejectsInvalidRangesAnglesColorsAndBudgets()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Emitter() with { LifetimeSeconds = new(0, 1) }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Emitter() with { LifetimeSeconds = new(2, 1) }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Emitter() with { RatePerSecond = double.NaN }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Emitter() with { MaxSpawnsPerUpdate = 0 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Emitter() with { Shape = (ParticleSpawnShape)99 }).Validate());
        Assert.Throws<ArgumentException>(() =>
            (Emitter() with { Shape = ParticleSpawnShape.Point }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Emitter() with { StartColor = new ParticleColor(2, 0, 0, 1) }).Validate());
        // The Emitter() fixture uses variable box offsets. Disk emitters require
        // fixed center offsets; make those valid so this specifically tests the radius.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Emitter() with
            {
                Shape = ParticleSpawnShape.Disk,
                SpawnRadius = 0,
                SpawnOffsetX = new(0, 0),
                SpawnOffsetY = new(0, 0)
            }).Validate());
        (Emitter() with
        {
            Shape = ParticleSpawnShape.Disk,
            SpawnRadius = 2,
            SpawnOffsetX = new(0, 0),
            SpawnOffsetY = new(0, 0)
        }).Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => new ParticleSystem(0));
    }

    [Fact]
    public void VersionedJsonLibraryLoadsPresetsAndRejectsUnknownOrDuplicateData()
    {
        const string good = """
            { "version":1, "emitters":[
              { "id":"unit.dust", "burstCount":6, "shape":"Disk", "spawnRadius":2,
                "lifetimeSeconds":{"min":0.1,"max":0.3} },
              { "id":"unit.motes", "ratePerSecond":5 }
            ]}
            """;
        var library = ParticleEmitterLibrary.FromJson(good);
        Assert.Equal(2, library.Count);
        Assert.Equal(6, library.Get("unit.dust").BurstCount);
        Assert.True(library.TryGet("unit.motes", out ParticleEmitterDefinition? motes));
        Assert.Equal(5, motes.RatePerSecond);
        Assert.Throws<KeyNotFoundException>(() => library.Get("missing"));
        Assert.Throws<ArgumentException>(() => ParticleEmitterLibrary.FromJson(
            "{\"version\":1,\"emitters\":[{\"id\":\"same\",\"burstCount\":1},{\"id\":\"same\",\"burstCount\":1}]}"));
        Assert.Throws<NotSupportedException>(() => ParticleEmitterLibrary.FromJson(
            "{\"version\":2,\"emitters\":[{\"id\":\"x\",\"burstCount\":1}]}"));
        Assert.Throws<System.Text.Json.JsonException>(() => ParticleEmitterLibrary.FromJson(
            "{\"version\":1,\"emitters\":[{\"id\":\"x\",\"burstCount\":1,\"typo\":1}]}"));
    }

    [Fact]
    public void StablePoolRepeatedBurstsDoNotAllocatePerParticle()
    {
        var system = new ParticleSystem(64, maxUpdateDeltaSeconds: 1);
        var emitter = new ParticleEmitter(system, Emitter(burst: 64, rate: 0), seed: 1);
        const int warmupBursts = 512;
        const int measuredBursts = 200;
        const int particlesPerBurst = 64;
        // Exercise the exact hot path repeatedly before measuring steady-state
        // allocations. A single invocation is insufficient on tiered runtimes.
        for (int i = 0; i < warmupBursts; i++)
        {
            emitter.Burst(Origin);
            system.Update(.01);
            system.Clear();
        }
        Assert.Equal((long)warmupBursts * particlesPerBurst, system.Diagnostics.Spawned);
        Assert.Equal(0, system.ActiveCount);
        // Warm the allocation counter too; no forced collection or retries.
        _ = GC.GetAllocatedBytesForCurrentThread();
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < measuredBursts; i++)
        {
            emitter.Burst(Origin);
            system.Update(.01);
            system.Clear();
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.InRange(allocated, 0, 4096);
        Assert.Equal(0, system.ActiveCount);
        Assert.Equal((long)(warmupBursts + measuredBursts) * particlesPerBurst,
            system.Diagnostics.Spawned);
        Assert.Equal(0L, system.Diagnostics.DroppedAtCapacity);
        Assert.Equal(0L, system.Diagnostics.DroppedInvalid);
    }

    [Fact]
    public void ClearingPoolCanRetainOrResetCumulativeDiagnostics()
    {
        var system = new ParticleSystem(1);
        system.TrySpawn(Spawn());
        system.TrySpawn(Spawn());
        system.Clear();
        Assert.Equal(1, system.Diagnostics.Spawned);
        Assert.Equal(1, system.Diagnostics.DroppedAtCapacity);
        Assert.Equal(0, system.ActiveCount);
        system.Clear(resetDiagnostics: true);
        Assert.Equal(0, system.Diagnostics.Spawned);
        Assert.Equal(0, system.Diagnostics.DroppedAtCapacity);
        Assert.Equal(1, system.Capacity);
    }
}
