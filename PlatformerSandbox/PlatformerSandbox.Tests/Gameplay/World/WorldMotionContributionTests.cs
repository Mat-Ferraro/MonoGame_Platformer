using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Abilities;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.States;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Gameplay.World;

public sealed class WorldMotionContributionTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    private static WorldSession LongLevel()
    {
        var ground = HorizontalMotionSolver.Solve(MovementGymTuning.Baseline.Ground);
        var air = HorizontalMotionSolver.Solve(MovementGymTuning.Baseline.Air);
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        var world = new WorldSession(
            [new StaticAabbCollider(9, Box(-500, -1.0, 500, 0))],
            Array.Empty<MovingPlatformTrajectory>(),
            ground, air, jump, gravityAcceleration: -24.0,
            releaseConstraints: new JumpReleaseConstraints(1.10));
        world.AddPlayer(1, Box(-1, 0, -0.2, 1.6));
        world.AddPlayer(2, Box(2, 0, 2.8, 1.6));
        return world;
    }

    private static WorldPlayerCommand Walk(HorizontalDirection dir, int player = 1) =>
        new(player, dir);
    private static WorldMotionRequest Wind(int player, double v) =>
        new(player, WorldMotionRequestKind.SetEnvironmentVelocity, v);
    private static WorldMotionRequest Kick(int player, double v) =>
        new(player, WorldMotionRequestKind.AddHorizontalImpulse, v);
    private static CharacterAbilityRequest Pulse(int player = 1) =>
        new(player, CharacterAbilityId.Pulse, CharacterAbilityRequestKind.Activate);
    private static CharacterAbilityRequest CancelPulse(int player = 1) =>
        new(player, CharacterAbilityId.Pulse, CharacterAbilityRequestKind.Cancel);

    private static void Settle(WorldSession world, HorizontalDirection direction,
        WorldMotionRequest? first = null, int ticks = 120)
    {
        for (int i = 0; i < ticks; i++)
            world.Step([Walk(direction)], [], [],
                i == 0 && first is { } request
                    ? [request] : Array.Empty<WorldMotionRequest>());
    }

    [Fact]
    public void WindWithWalkingLeftReaches175PercentOfNormalSpeed()
    {
        WorldSession world = LongLevel();
        double speed = MotionContributionGym.NormalWalkingSpeed;
        Settle(world, HorizontalDirection.Left, Wind(1, -0.75 * speed));
        Assert.Equal(-1.75 * speed, world.GetPlayer(1).Velocity.X, 7);
        Assert.Equal(-0.75 * speed, world.GetPlayerMotion(1).EnvironmentVelocityX, 7);
    }

    [Fact]
    public void WindAgainstWalkingRightLeaves25PercentRight()
    {
        WorldSession world = LongLevel();
        double speed = MotionContributionGym.NormalWalkingSpeed;
        Settle(world, HorizontalDirection.Right, Wind(1, -0.75 * speed));
        Assert.Equal(+0.25 * speed, world.GetPlayer(1).Velocity.X, 7);
    }

    [Fact]
    public void IdleCharacterIsTransportedByWindWithoutWalking()
    {
        WorldSession world = LongLevel();
        double speed = MotionContributionGym.NormalWalkingSpeed;
        world.Step([], [], [], [Wind(1, -0.75 * speed)]);
        Assert.Equal(-0.75 * speed, world.GetPlayer(1).Velocity.X, 7);
        Assert.Equal(CharacterLocomotionState.GroundedIdle,
            world.GetPlayer(1).State.Locomotion);
    }

    [Fact]
    public void RemovingWindRestoresPersonalLocomotionSpeedWithoutResettingIt()
    {
        WorldSession world = LongLevel();
        double speed = MotionContributionGym.NormalWalkingSpeed;
        Settle(world, HorizontalDirection.Right, Wind(1, -0.75 * speed));
        world.Step([Walk(HorizontalDirection.Right)], [], [], [Wind(1, 0)]);
        Assert.Equal(speed, world.GetPlayer(1).Velocity.X, 7);
        Assert.Equal(0, world.GetPlayerMotion(1).EnvironmentVelocityX);
    }

    [Fact]
    public void WindDoesNotAlterOtherPlayerOrItsOwnInput()
    {
        WorldSession world = LongLevel();
        double speed = MotionContributionGym.NormalWalkingSpeed;
        world.Step([], [], [], [Wind(1, -0.75 * speed)]);
        Assert.Equal(0, world.GetPlayer(2).Velocity.X);
        Assert.Equal(0, world.GetPlayerMotion(2).CombinedExternalVelocityX);
        Assert.Equal(-0.75 * speed, world.GetPlayer(1).Velocity.X, 7);
    }

    [Fact]
    public void WindWithJumpIsCollisionIntegratedNotPostTranslated()
    {
        WorldSession windy = LongLevel();
        WorldSession ordinary = LongLevel();
        double speed = MotionContributionGym.NormalWalkingSpeed;
        windy.Step([new WorldPlayerCommand(1, HorizontalDirection.Right,
            JumpPressed: true)], [], [], [Wind(1, -0.75 * speed)]);
        ordinary.Step([new WorldPlayerCommand(1, HorizontalDirection.Right,
            JumpPressed: true)]);
        Assert.True(windy.GetPlayer(1).IsJumping);
        Assert.True(ordinary.GetPlayer(1).IsJumping);
        Assert.True(windy.GetPlayer(1).Bounds.Center.X <
            ordinary.GetPlayer(1).Bounds.Center.X);
        Assert.Equal(ordinary.GetPlayer(1).Velocity.Y,
            windy.GetPlayer(1).Velocity.Y, 7);
    }

    [Fact]
    public void ImpulsePushesOppositeFacingAndDecaysEachSuccessfulTick()
    {
        WorldSession world = LongLevel();
        double speed = MotionContributionGym.NormalWalkingSpeed;
        WorldStepResult impact = world.Step([], [], [], [Kick(1, -2 * speed)]);
        Assert.True(world.GetPlayer(1).Velocity.X < 0);
        Assert.Single(impact.Events.Where(e =>
            e.Kind == WorldGameplayEventKind.HorizontalImpulseApplied && e.PlayerId == 1));
        double firstRemaining = Math.Abs(world.GetPlayerMotion(1).ImpulseVelocityX);
        world.Step([]);
        Assert.True(Math.Abs(world.GetPlayerMotion(1).ImpulseVelocityX) < firstRemaining);
    }

    [Fact]
    public void PulseNowProducesRealDirectionLockedDashMotion()
    {
        WorldSession dash = LongLevel();
        WorldSession ordinary = LongLevel();
        var input = Walk(HorizontalDirection.Right);
        dash.Step([input], [], [Pulse()]);
        ordinary.Step([input]);
        Assert.True(dash.GetPlayer(1).Velocity.X >
            ordinary.GetPlayer(1).Velocity.X + MotionContributionGym.NormalWalkingSpeed);
        Assert.True(dash.GetPlayer(1).Bounds.Center.X >
            ordinary.GetPlayer(1).Bounds.Center.X);
        Assert.True(dash.GetPlayerMotion(1).DashVelocityX > 0.0);
        // Dash keeps its original activation direction if player reverses.
        dash.Step([Walk(HorizontalDirection.Left)]);
        Assert.True(dash.GetPlayerMotion(1).DashVelocityX > 0.0);
    }

    [Fact]
    public void ManualCancellationRemovesDashAtFollowingPhysicsBoundary()
    {
        WorldSession world = LongLevel();
        world.Step([Walk(HorizontalDirection.Right)], [], [Pulse()]);
        double activeVelocity = world.GetPlayer(1).Velocity.X;
        world.Step([], [], [CancelPulse()]);
        Assert.False(world.GetPlayerAbilities(1).Pulse.IsActive);
        Assert.Equal(0, world.GetPlayerMotion(1).DashVelocityX);
        Assert.True(world.GetPlayer(1).Velocity.X < activeVelocity);
    }

    [Fact]
    public void StunStopsDashButPreservesExternalWind()
    {
        WorldSession world = LongLevel();
        double speed = MotionContributionGym.NormalWalkingSpeed;
        world.Step([Walk(HorizontalDirection.Right)], [], [Pulse()],
            [Wind(1, -0.75 * speed)]);
        world.Step([], [new CharacterStateRequest(1,
            CharacterStateRequestKind.ApplyStun, 90)], []);
        Assert.False(world.GetPlayerAbilities(1).Pulse.IsActive);
        Assert.Equal(0, world.GetPlayerMotion(1).DashVelocityX);
        Assert.Equal(-0.75 * speed, world.GetPlayerMotion(1).EnvironmentVelocityX, 7);
    }

    [Fact]
    public void DashAndWindComposeWithoutClampingToNormalWalkMaximum()
    {
        WorldSession world = LongLevel();
        double speed = MotionContributionGym.NormalWalkingSpeed;
        world.Step([Walk(HorizontalDirection.Left)], [], [Pulse()],
            [Wind(1, -0.75 * speed)]);
        Assert.True(world.GetPlayer(1).Velocity.X < -2.0 * speed);
    }

    [Fact]
    public void DashDoesNotTunnelThroughStaticWall()
    {
        WorldSession world = MotionContributionGym.Create();
        world.RespawnPlayer(1, Box(17.1, -0.5, 17.9, 1.1));
        world.Step([Walk(HorizontalDirection.Right)], [], [Pulse()]);
        for (int i = 0; i < 24; i++)
        {
            world.Step([Walk(HorizontalDirection.Right)]);
            Assert.True(world.GetPlayer(1).Bounds.Right <= 18.0 + 1e-8);
        }
    }

    [Fact]
    public void WindPushingIntoWallDoesNotLeaveReverseVelocityOnShutdown()
    {
        WorldSession world = MotionContributionGym.Create();
        double speed = MotionContributionGym.NormalWalkingSpeed;
        world.RespawnPlayer(1, Box(17.1, -0.5, 17.9, 1.1));
        for (int i = 0; i < 10; i++)
            world.Step([], [], [], i == 0
                ? [Wind(1, speed)] : Array.Empty<WorldMotionRequest>());
        world.Step([], [], [], [Wind(1, 0)]);
        Assert.True(world.GetPlayer(1).Velocity.X >= -0.00001);
        Assert.True(world.GetPlayer(1).Bounds.Right <= 18.0 + 1e-8);
    }

    [Fact]
    public void EquivalentReorderedRequestsProduceIdenticalStateAndEvents()
    {
        WorldSession first = LongLevel();
        WorldSession second = LongLevel();
        double speed = MotionContributionGym.NormalWalkingSpeed;
        WorldMotionRequest[] requests = [Wind(1, -0.75 * speed), Kick(1, speed), Wind(2, speed)];
        WorldStepResult a = first.Step([], [], [], requests);
        WorldStepResult b = second.Step([], [], [], requests.Reverse().ToArray());
        Assert.Equal(a.Players.ToArray(), b.Players.ToArray());
        Assert.Equal(a.Events.ToArray(), b.Events.ToArray());
    }

    [Fact]
    public void BadMotionRequestCannotAdvanceTimeOrSpendAbilityEnergy()
    {
        WorldSession world = LongLevel();
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Step([], [],
            [Pulse()], [Wind(1, double.NaN)]));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Step([], [],
            [], [Kick(99, 5.0)]));
        Assert.Throws<ArgumentException>(() => world.Step([], [], [],
            [Wind(1, 1.0), Wind(1, -2.0)]));
        Assert.Throws<ArgumentNullException>(() => world.Step([], [], [], null!));
        Assert.Equal(0, world.WorldTick);
        Assert.Equal(100, world.GetPlayerAbilities(1).Energy);
        Assert.Equal(0, world.GetPlayerMotion(1).CombinedExternalVelocityX);
    }

    [Fact]
    public void RespawnResetsOnlyTargetMotionAndKeepsWorldTime()
    {
        WorldSession world = LongLevel();
        world.Step([], [], [], [Wind(1, -3), Wind(2, 4)]);
        long tick = world.WorldTick;
        world.RespawnPlayer(1, Box(-1, 0, -0.2, 1.6));
        Assert.Equal(tick, world.WorldTick);
        Assert.Equal(0, world.GetPlayerMotion(1).CombinedExternalVelocityX);
        Assert.Equal(4, world.GetPlayerMotion(2).EnvironmentVelocityX);
    }

    [Fact]
    public void TransportOnMovingPlatformKeepsOtherRiderAndWorldClockIndependent()
    {
        WorldSession world = SharedWorldClockGym.Create();
        double speed = MotionContributionGym.NormalWalkingSpeed;
        WorldStepResult first = world.Step([], [], [], [Wind(1, speed * 0.25)]);
        Assert.Equal(1, first.WorldTick);
        Assert.Equal(SharedWorldClockGym.PlatformId,
            world.GetPlayer(2).MovingSupportId);
        Assert.Equal(SharedWorldClockGym.PlatformId,
            world.GetPlayer(1).MovingSupportId);
        Assert.Equal(0, world.GetPlayerMotion(2).EnvironmentVelocityX);
        Assert.Single(first.Events.Where(e =>
            e.Kind == WorldGameplayEventKind.EnvironmentMotionChanged));
        Assert.DoesNotContain(world.Step([]).Events, e =>
            e.Kind == WorldGameplayEventKind.EnvironmentMotionChanged);
    }

}
