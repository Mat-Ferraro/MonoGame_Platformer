using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Gameplay.World;

public sealed class WorldGameplayEventTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    private static WorldSession CreateStableGroundWorld()
    {
        var ground = HorizontalMotionSolver.Solve(MovementGymTuning.Baseline.Ground);
        var air = HorizontalMotionSolver.Solve(MovementGymTuning.Baseline.Air);
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        var world = new WorldSession(
            [new StaticAabbCollider(10, Box(-20, -1, 20, 0))],
            Array.Empty<MovingPlatformTrajectory>(), ground, air, jump,
            gravityAcceleration: -24.0);
        world.AddPlayer(1, Box(-0.4, 0, 0.4, 1.6));
        return world;
    }

    [Fact]
    public void CommittedStepPublishesImmutableOrderedEventsNotSnapshots()
    {
        WorldSession world = AuthoredWorldGym.Create(worldInstanceId: 41);
        Assert.Empty(world.Snapshot().Events);
        WorldStepResult step = world.Step([
            new WorldPlayerCommand(1, HorizontalDirection.Neutral, JumpPressed: true)]);
        Assert.NotEmpty(step.Events);
        Assert.All(step.Events, e =>
        {
            Assert.Equal(41L, e.WorldInstanceId);
            Assert.Equal(1L, e.WorldTick);
        });
        Assert.Equal(Enumerable.Range(0, step.Events.Count).ToArray(),
            step.Events.Select(e => e.Sequence).ToArray());
        Assert.Single(step.Events.Where(e => e.Kind == WorldGameplayEventKind.JumpStarted && e.PlayerId == 1));
        Assert.Empty(world.Snapshot().Events);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<WorldGameplayEvent>)step.Events)[0] = default);
        WorldGameplayEvent[] captured = step.Events.ToArray();
        world.Step(Array.Empty<WorldPlayerCommand>());
        Assert.Equal(captured, step.Events.ToArray());
    }

    [Fact]
    public void RepeatedJumpPressDoesNotRepeatJumpStartedWhileAirborne()
    {
        WorldSession world = AuthoredWorldGym.Create();
        int starts = 0;
        for (int i = 0; i < 25; i++)
        {
            WorldStepResult step = world.Step([
                new WorldPlayerCommand(1, HorizontalDirection.Neutral, JumpPressed: true)]);
            starts += step.Events.Count(e => e.Kind == WorldGameplayEventKind.JumpStarted && e.PlayerId == 1);
        }
        Assert.Equal(1, starts);
    }

    [Fact]
    public void LandingIsEmittedOnTransitionButNotEverySubsequentGroundedTick()
    {
        WorldSession world = CreateStableGroundWorld();
        WorldStepResult takeoff = world.Step([
            new WorldPlayerCommand(1, HorizontalDirection.Neutral, JumpPressed: true)]);
        Assert.Contains(takeoff.Events, e => e.Kind == WorldGameplayEventKind.JumpStarted && e.PlayerId == 1);
        int landings = 0;
        for (int i = 0; i < 240; i++)
        {
            WorldStepResult step = world.Step(Array.Empty<WorldPlayerCommand>());
            landings += step.Events.Count(e => e.Kind == WorldGameplayEventKind.Landed && e.PlayerId == 1);
        }
        Assert.Equal(1, landings);
        Assert.True(world.GetPlayer(1).IsGrounded);
    }

    [Fact]
    public void LifecycleActionsAreEmittedOnlyOnNextSuccessfulTick()
    {
        WorldSession world = AuthoredWorldGym.Create();
        WorldStepResult start = world.Step(Array.Empty<WorldPlayerCommand>());
        Assert.Equal(new[] { 1, 2 }, start.Events.Where(e =>
            e.Kind == WorldGameplayEventKind.PlayerJoined).Select(e => e.PlayerId).ToArray());
        Assert.Empty(world.Step(Array.Empty<WorldPlayerCommand>()).Events);
        long tick = world.WorldTick;
        world.RespawnPlayer(1);
        Assert.Equal(tick, world.WorldTick);
        Assert.Empty(world.Snapshot().Events);
        WorldStepResult next = world.Step(Array.Empty<WorldPlayerCommand>());
        Assert.Single(next.Events.Where(e =>
            e.Kind == WorldGameplayEventKind.PlayerRespawned && e.PlayerId == 1));
        Assert.DoesNotContain(world.Step(Array.Empty<WorldPlayerCommand>()).Events,
            e => e.Kind == WorldGameplayEventKind.PlayerRespawned);
    }

    [Fact]
    public void InvalidCommandDoesNotAdvanceTickOrConsumeQueuedActions()
    {
        WorldSession world = AuthoredWorldGym.Create(worldInstanceId: 17);
        world.Step(Array.Empty<WorldPlayerCommand>());
        world.RespawnPlayer(1);
        long tick = world.WorldTick;
        Assert.Throws<ArgumentException>(() => world.Step([
            new WorldPlayerCommand(1, HorizontalDirection.Right),
            new WorldPlayerCommand(1, HorizontalDirection.Left)]));
        Assert.Equal(tick, world.WorldTick);
        Assert.Empty(world.Snapshot().Events);
        WorldGameplayEvent e = Assert.Single(world.Step(Array.Empty<WorldPlayerCommand>())
            .Events.Where(e => e.Kind == WorldGameplayEventKind.PlayerRespawned));
        Assert.Equal(tick + 1, e.WorldTick);
        Assert.Equal(0, e.Sequence);
        Assert.Equal(17L, e.WorldInstanceId);
    }

    [Fact]
    public void TriggerIsEdgeDrivenAndReportsExitOnce()
    {
        WorldSession world = AuthoredWorldGym.Create();
        world.SetPlayerSpawn(1, AuthoredWorldGym.GroundSpawn);
        world.RespawnPlayer(1);
        WorldStepResult first = world.Step(Array.Empty<WorldPlayerCommand>());
        Assert.Single(first.Events.Where(e =>
            e.Kind == WorldGameplayEventKind.TriggerEntered && e.PlayerId == 1 && e.TriggerId == 301));
        for (int i = 0; i < 10; i++)
            Assert.DoesNotContain(world.Step(Array.Empty<WorldPlayerCommand>()).Events,
                e => e.Kind == WorldGameplayEventKind.TriggerEntered && e.PlayerId == 1);
        int exits = 0;
        for (int i = 0; i < 95; i++)
        {
            WorldStepResult step = world.Step([new WorldPlayerCommand(1, HorizontalDirection.Right)]);
            exits += step.Events.Count(e =>
                e.Kind == WorldGameplayEventKind.TriggerExited && e.PlayerId == 1 && e.TriggerId == 301);
        }
        Assert.Equal(1, exits);
    }

    [Fact]
    public void JoinLeaveReportsPlayerThreeWithoutSpuriousOtherJoins()
    {
        WorldSession world = AuthoredWorldGym.Create();
        world.Step(Array.Empty<WorldPlayerCommand>());
        AuthoredWorldGym.JoinGuest(world);
        var joined = world.Step(Array.Empty<WorldPlayerCommand>()).Events;
        Assert.Single(joined.Where(e => e.Kind == WorldGameplayEventKind.PlayerJoined && e.PlayerId == 3));
        Assert.DoesNotContain(joined, e => e.Kind == WorldGameplayEventKind.PlayerJoined && e.PlayerId != 3);
        world.RemovePlayer(3);
        var removed = world.Step(Array.Empty<WorldPlayerCommand>()).Events;
        Assert.Single(removed.Where(e => e.Kind == WorldGameplayEventKind.PlayerRemoved && e.PlayerId == 3));
        Assert.DoesNotContain(removed, e => e.Kind == WorldGameplayEventKind.PlayerRemoved && e.PlayerId != 3);
    }

    [Fact]
    public void ReversedPlayerInputOrderDoesNotChangeCommittedEventBatches()
    {
        WorldSession a = AuthoredWorldGym.Create(worldInstanceId: 55);
        WorldSession b = AuthoredWorldGym.Create(worldInstanceId: 55);
        for (int i = 0; i < 75; i++)
        {
            var p1 = new WorldPlayerCommand(1, HorizontalDirection.Left, JumpPressed: i == 0);
            var p2 = new WorldPlayerCommand(2, HorizontalDirection.Right, JumpPressed: i == 0);
            WorldStepResult first = a.Step([p1, p2]);
            WorldStepResult second = b.Step([p2, p1]);
            Assert.Equal(first.Players.ToArray(), second.Players.ToArray());
            Assert.Equal(first.Events.ToArray(), second.Events.ToArray());
        }
    }

    [Fact]
    public void DifferentWorldRunIdsPreventEventKeyCollisionAfterRestart()
    {
        WorldSession first = AuthoredWorldGym.Create(worldInstanceId: 81);
        WorldSession restarted = AuthoredWorldGym.Create(worldInstanceId: 82);
        WorldGameplayEvent a = Assert.Single(first.Step([
            new WorldPlayerCommand(1, HorizontalDirection.Neutral, JumpPressed: true)])
            .Events.Where(e => e.Kind == WorldGameplayEventKind.JumpStarted));
        WorldGameplayEvent b = Assert.Single(restarted.Step([
            new WorldPlayerCommand(1, HorizontalDirection.Neutral, JumpPressed: true)])
            .Events.Where(e => e.Kind == WorldGameplayEventKind.JumpStarted));
        Assert.Equal(a.WorldTick, b.WorldTick);
        Assert.Equal(a.Sequence, b.Sequence);
        Assert.NotEqual(a.WorldInstanceId, b.WorldInstanceId);
    }

    [Fact]
    public void TriggerDefinitionsAreValidatedAndDefensivelyCopied()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new WorldTriggerDefinition(0, "GOAL", Box(0, 0, 1, 1)));
        Assert.Throws<ArgumentException>(() =>
            new WorldTriggerDefinition(7, "  ", Box(0, 0, 1, 1)));
        Assert.Throws<ArgumentException>(() =>
            new WorldTriggerDefinition(7, "GOAL", default));
        Assert.Throws<ArgumentException>(() => new WorldDefinition("TRIGGER_DUP",
            Array.Empty<StaticAabbCollider>(), Array.Empty<MovingPlatformTrajectory>(),
            [new WorldSpawnPoint("S", Box(-2, -2, -1, -1))],
            [new WorldTriggerDefinition(3, "A", Box(0, 0, 1, 1)),
             new WorldTriggerDefinition(3, "B", Box(2, 0, 3, 1))]));
        var originals = new[] { new WorldTriggerDefinition(4, "GOAL", Box(0, 0, 1, 1)) };
        var definition = new WorldDefinition("TRIGGER", Array.Empty<StaticAabbCollider>(),
            Array.Empty<MovingPlatformTrajectory>(),
            [new WorldSpawnPoint("S", Box(-2, -2, -1, -1))], originals);
        originals[0] = new WorldTriggerDefinition(99, "REPLACED", Box(0, 0, 1, 1));
        Assert.Equal(4, Assert.Single(definition.Triggers).Id);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<WorldTriggerDefinition>)definition.Triggers)[0] = originals[0]);
    }
}
