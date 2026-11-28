using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;
using PlatformerSandbox.Simulation.Physics;

namespace PlatformerSandbox.Tests.Gameplay.World;

public sealed class WorldCrushRespawnTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));
    private static Aabb Danger => Box(0, 0, .8, 1.6);
    private static Aabb Safe => Box(6, 0, 6.8, 1.6);
    private static Aabb Other => Box(9, 0, 9.8, 1.6);

    private static WorldSession Create(bool horizontal = false,
        WorldCrushPolicy policy = WorldCrushPolicy.RespawnAtNamedSpawn,
        SimulationFaultSensitivity sensitivity = SimulationFaultSensitivity.Strict,
        bool safeInitial = true, double fixedStep = 1.0 / 60.0,
        bool anchoredCheckpoint = false)
    {
        // Match the already-tested M26 descending-platform and side-pusher
        // geometries. Add a distant safe floor region for a second player.
        StaticAabbCollider[] statics = horizontal
            ? [new(1, Box(-3, -1, 3, 0)), new(3, Box(1, -.5, 1.5, 2)),
               new(4, Box(4, -1, 15, 0))]
            : [new(1, Box(-3, -1, 15, 0))];
        var hazard = horizontal
            ? new MovingPlatformTrajectory(7, Box(-1, -.25, -.2, 1.9), new(.5, 0), .5)
            : new MovingPlatformTrajectory(7, Box(-1, 1.8, 2, 2.1), new(0, -1.5), .5);
        var safePlatform = new MovingPlatformTrajectory(8, Box(5, 3, 8, 3.25), new(1, 0), 2);
        var definition = new WorldDefinition("CRUSH_REGRESSION", statics,
            anchoredCheckpoint ? [hazard, safePlatform] : [hazard],
            [new("danger", Danger), new("safe", Safe), new("other", Other),
             anchoredCheckpoint ? new("rider", Box(6, 3.25, 6.8, 4.85), 8) : new("unused", Safe)],
            [new(21, "hazard", Box(-.1, -.1, .9, 1.7)), new(22, "checkpoint", Box(5.9, -.1, 7, 1.7))]);
        var motion = HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10, .2, .1, .35));
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(new JumpConstraints(4, .4, .3)));
        var world = new WorldSession(definition, motion, motion, jump, -20,
            fixedStepSeconds: fixedStep, faultPolicy: new SimulationFaultPolicy(sensitivity),
            crushPolicy: policy);
        world.AddPlayer(new WorldPlayerSlotRegistration(1, 1001, "other", 11));
        world.AddPlayer(new WorldPlayerSlotRegistration(2, 1002, safeInitial ? "safe" : "danger", 22));
        if (safeInitial) world.RespawnPlayer(2, Danger);
        return world;
    }

    private static WorldPlayerCommand[] Commands => [new(1, HorizontalDirection.Right)];

    [Theory]
    [InlineData(SpatialQueryMode.Reference, false, SimulationFaultSensitivity.Strict)]
    [InlineData(SpatialQueryMode.Indexed, false, SimulationFaultSensitivity.Diagnostic)]
    [InlineData(SpatialQueryMode.Verify, false, SimulationFaultSensitivity.Resilient)]
    [InlineData(SpatialQueryMode.Reference, true, SimulationFaultSensitivity.Strict)]
    [InlineData(SpatialQueryMode.Indexed, true, SimulationFaultSensitivity.Strict)]
    [InlineData(SpatialQueryMode.Verify, true, SimulationFaultSensitivity.Strict)]
    public void ClassifiedCrushCommitsOnlyVictimsRespawnAndKeepsWorldRunning(
        SpatialQueryMode mode, bool horizontal, SimulationFaultSensitivity sensitivity)
    {
        using var scope = SpatialQueryPolicy.Use(mode);
        WorldSession world = Create(horizontal, sensitivity: sensitivity);
        WorldSession control = Create(horizontal, sensitivity: sensitivity);
        control.RespawnPlayer(2, Safe);
        WorldStepResult? outcome = null;
        for (int i = 0; i < 30; i++)
        {
            var step = world.Step(Commands);
            var otherStep = control.Step(Commands);
            Assert.Equal(otherStep.WorldTick, step.WorldTick);
            Assert.Equal(control.GetPlayer(1), world.GetPlayer(1));
            if (step.Events.Any(e => e.Kind == WorldGameplayEventKind.PlayerCrushed))
            {
                outcome = step;
                break;
            }
        }
        Assert.NotNull(outcome);
        var result = outcome!;
        var crushed = Assert.Single(result.Events.Where(e => e.Kind == WorldGameplayEventKind.PlayerCrushed));
        var respawn = Assert.Single(result.Events.Where(e => e.Kind == WorldGameplayEventKind.PlayerRespawned));
        Assert.Equal(2, crushed.PlayerId);
        Assert.Equal(2, respawn.PlayerId);
        Assert.NotNull(crushed.Crush);
        Assert.Equal(horizontal ? CharacterCrushAxis.Horizontal : CharacterCrushAxis.Vertical, crushed.Crush!.Axis);
        Assert.Equal(7, crushed.Crush.PushingColliderId);
        Assert.Equal(horizontal ? 3 : 1, crushed.Crush.OpposingColliderId);
        Assert.InRange(crushed.Crush.WorldTimeSeconds, result.WorldTimeSeconds - world.FixedStepSeconds - 1e-9,
            result.WorldTimeSeconds + 1e-9);
        Assert.True(crushed.Sequence < respawn.Sequence);
        Assert.Equal(Enumerable.Range(0, result.Events.Count), result.Events.Select(e => e.Sequence));
        Assert.Equal(Safe, world.GetPlayer(2).Bounds);
        Assert.Equal(1002, world.GetPlayerSlot(2).CharacterId);
        Assert.Equal(22, world.GetPlayerSlot(2).InputSourceId);
        Assert.Equal(Vector2D.Zero, world.GetPlayer(2).Velocity);
        Assert.False(world.GetPlayer(2).JumpStartedThisTick);
        Assert.All(world.MovingPlatforms, p => Assert.Equal(result.WorldTimeSeconds, p.TimeSeconds));
        Assert.Contains(result.Events, e => e.Kind == WorldGameplayEventKind.TriggerExited && e.PlayerId == 2 && e.TriggerId == 21);
        Assert.Empty(world.Snapshot().Events);
        Assert.Contains(world.CaptureSpatialSnapshot().Point(Safe.Center),
            e => e.Kind == WorldSpatialKind.Player && e.Id == 2);
        Assert.DoesNotContain(world.CaptureSpatialSnapshot().Point(Danger.Center),
            e => e.Kind == WorldSpatialKind.Player && e.Id == 2);
        var next = world.Step(Commands);
        Assert.Equal(result.WorldTick + 1, next.WorldTick);
        Assert.DoesNotContain(next.Events, e => e.Kind == WorldGameplayEventKind.PlayerCrushed || e.Kind == WorldGameplayEventKind.PlayerRespawned);
        Assert.Contains(next.Events, e => e.Kind == WorldGameplayEventKind.TriggerEntered && e.PlayerId == 2 && e.TriggerId == 22);
    }

    [Fact]
    public void BlockedCheckpointFallsBackToOriginalSpawnAtEndOfTick()
    {
        var world = Create(fixedStep: .2);
        world.SetPlayerSpawn(2, "danger");
        var result = world.Step(Commands);
        Assert.Contains(result.Events, e => e.Kind == WorldGameplayEventKind.PlayerCrushed && e.PlayerId == 2);
        Assert.Equal(Safe, world.GetPlayer(2).Bounds);
        Assert.Equal("danger", world.GetPlayerSlot(2).SpawnId);
        Assert.Equal(1L, world.WorldTick);
        world.Step(Commands);
        Assert.Equal(2L, world.WorldTick);
    }

    [Fact]
    public void AnchoredCheckpointUsesEndTimeRatherThanOldPlatformPosition()
    {
        var world = Create(fixedStep: .2, anchoredCheckpoint: true);
        world.SetPlayerSpawn(2, "rider");
        var result = world.Step(Commands);
        Assert.Contains(result.Events, e => e.Kind == WorldGameplayEventKind.PlayerCrushed && e.PlayerId == 2);
        Assert.Equal(world.Definition!.ResolveSpawnBounds("rider", result.WorldTimeSeconds), world.GetPlayer(2).Bounds);
        Assert.Equal(8, world.GetPlayer(2).MovingSupportId);
        world.Step(Commands);
        Assert.Equal(2L, world.WorldTick);
        Assert.Equal(8, world.GetPlayer(2).MovingSupportId);
    }

    [Theory]
    [InlineData(WorldRespawnMode.None)]
    [InlineData(WorldRespawnMode.Manual)]
    [InlineData(WorldRespawnMode.Delayed)]
    [InlineData(WorldRespawnMode.Immediate)]
    public void DeathAndRespawnPoliciesAreIndependent(WorldRespawnMode mode)
    {
        var world=Create(fixedStep:.2);
        world.ConfigureCrushLifecycle(WorldCrushPolicy.Death,new WorldRespawnSettings{Mode=mode,DelayTicks=mode==WorldRespawnMode.Delayed?2:0});
        var first=world.Step(Commands);
        Assert.Contains(first.Events,e=>e.Kind==WorldGameplayEventKind.PlayerCrushed&&e.PlayerId==2);
        Assert.Contains(first.Events,e=>e.Kind==WorldGameplayEventKind.PlayerDied&&e.PlayerId==2);
        Assert.Equal(mode!=WorldRespawnMode.Immediate,world.GetPlayer(2).IsDead);
        if(mode==WorldRespawnMode.Immediate){Assert.Equal(Safe,world.GetPlayer(2).Bounds);return;}
        Assert.DoesNotContain(WorldPhysicalBodyView.Capture(world).Bodies,b=>b.Id==new PhysicalBodyId(PhysicalBodyKind.KinematicCharacter,1002));
        Assert.DoesNotContain(world.CaptureSpatialSnapshot().Point(Danger.Center),b=>b.Kind==WorldSpatialKind.Player&&b.Id==2);
        var next=world.Step([new(2,HorizontalDirection.Right,true)]);
        Assert.True(world.GetPlayer(2).IsDead);Assert.DoesNotContain(next.Events,e=>e.PlayerId==2&&e.Kind==WorldGameplayEventKind.JumpStarted);
        if(mode==WorldRespawnMode.Manual)world.RequestPlayerRespawn(2);
        var third=world.Step(Commands);Assert.Equal(3L,world.WorldTick);
        Assert.Equal(mode==WorldRespawnMode.None,world.GetPlayer(2).IsDead);
        if(mode!=WorldRespawnMode.None)
        {Assert.Equal(Safe,world.GetPlayer(2).Bounds);Assert.Equal(1,world.GetPlayer(2).Life.RespawnsUsed);Assert.Contains(third.Events,e=>e.PlayerId==2&&e.Kind==WorldGameplayEventKind.PlayerRespawned);}
    }

    [Fact]
    public void BlockedSpawnWaitsWithoutCrashingAndRetriesAtLaterTick()
    {
        var world=Create(safeInitial:false,fixedStep:.2);
        world.ConfigureCrushLifecycle(WorldCrushPolicy.Death,new());
        world.Step(Commands);Assert.True(world.GetPlayer(2).IsDead);
        for(int i=0;i<12&&world.GetPlayer(2).IsDead;i++)world.Step(Commands);
        Assert.False(world.GetPlayer(2).IsDead);Assert.Equal(Danger,world.GetPlayer(2).Bounds);
    }

    [Fact]
    public void LifeBudgetAndQueuedExternalDeathUseSameTransactionalPath()
    {
        var world=Create(fixedStep:.2);
        world.ConfigureCrushLifecycle(WorldCrushPolicy.Death,new WorldRespawnSettings{MaximumRespawns=0});
        world.RequestPlayerDeath(1);
        Assert.Throws<ArgumentException>(()=>world.Step([new(1,HorizontalDirection.Right),new(1,HorizontalDirection.Left)]));
        Assert.False(world.GetPlayer(1).IsDead);Assert.Equal(0L,world.WorldTick);
        var first=world.Step(Commands);Assert.True(world.GetPlayer(1).IsDead);Assert.True(world.GetPlayer(2).IsDead);
        Assert.Equal(2,first.Events.Count(e=>e.Kind==WorldGameplayEventKind.PlayerDied));
        Assert.Empty(WorldPhysicalBodyView.Capture(world).Bodies.Where(b=>b.Id.Kind==PhysicalBodyKind.KinematicCharacter));
        world.Step(Commands);Assert.Equal(2L,world.WorldTick);Assert.True(world.GetPlayer(1).IsDead);
    }

    [Fact]
    public void AnchoredSpawnAndRiderUsePausedRuntimePlatformRatherThanAuthoredClock()
    {
        var world=Create(fixedStep:.2,anchoredCheckpoint:true);
        world.Step(Commands);world.ControlPlatform(8,PlatformPlaybackCommand.Pause);
        var platform=world.MovingPlatforms.Single(p=>p.PlatformId==8);world.SetPlayerSpawn(2,"rider");world.RespawnPlayer(2);
        var bounds=world.GetPlayer(2).Bounds;world.Step(Commands);
        Assert.Equal(platform.Bounds,world.MovingPlatforms.Single(p=>p.PlatformId==8).Bounds);
        Assert.Equal(bounds,world.GetPlayer(2).Bounds);Assert.Equal(8,world.GetPlayer(2).MovingSupportId);
        world.RespawnPlayer(2);Assert.Equal(bounds,world.GetPlayer(2).Bounds);
    }

    [Fact]
    public void ExplicitThrowPolicyPreservesTransactionalRollback()
    {
        var world = Create(policy: WorldCrushPolicy.Throw, fixedStep: .2);
        var before = world.Snapshot();
        Assert.Throws<CharacterCrushException>(() => world.Step(Commands));
        Assert.Equal(before.WorldTick, world.WorldTick);
        Assert.Equal(before.Players.ToArray(), world.Snapshot().Players.ToArray());
        Assert.Empty(world.Snapshot().Events);
    }

    [Fact]
    public void MissingSafeSpawnFailsWithoutPartialWorldCommit()
    {
        var world = Create(safeInitial: false, fixedStep: .2);
        var before = world.Snapshot();
        var failure = Assert.Throws<InvalidOperationException>(() => world.Step(Commands));
        Assert.Contains("No safe authored respawn", failure.Message);
        Assert.IsType<CharacterCrushException>(failure.InnerException);
        Assert.Equal(before.WorldTick, world.WorldTick);
        Assert.Equal(before.Players.ToArray(), world.Snapshot().Players.ToArray());
        Assert.Empty(world.Snapshot().Events);
    }

    [Fact]
    public void UnrelatedValidationErrorsAreNeverSwallowedByCrushPolicy()
    {
        var world = Create();
        var before = world.Snapshot();
        Assert.Throws<ArgumentException>(() => world.Step([new(1, HorizontalDirection.Right), new(1, HorizontalDirection.Left)]));
        Assert.Equal(before.WorldTick, world.WorldTick);
        Assert.Equal(before.Players.ToArray(), world.Snapshot().Players.ToArray());
        Assert.Empty(world.Snapshot().Events);
    }
}
