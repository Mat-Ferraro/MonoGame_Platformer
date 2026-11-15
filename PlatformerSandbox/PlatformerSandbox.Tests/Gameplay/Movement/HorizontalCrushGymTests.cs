using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class HorizontalCrushGymTests
{
    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));

    [Fact]
    public void CombinedGymProvidesReachableHorizontalCrushStation()
    {
        var gym = new MovementGymSession();
        StaticAabbCollider pad = Assert.Single(gym.Colliders, c => c.Id == 11);
        StaticAabbCollider wall = Assert.Single(gym.Colliders, c => c.Id == 12);
        var horizontal = Assert.Single(gym.MovingPlatforms, p => p.PlatformId == 7);
        var liftFloor = Assert.Single(gym.Colliders, c => c.Id == 10);

        Assert.Equal(8.2, pad.Bounds.Left);
        Assert.Equal(10.35, pad.Bounds.Right);
        Assert.Equal(1.00, pad.Bounds.Top);
        Assert.Equal(pad.Bounds.Right, wall.Bounds.Left);
        Assert.Equal(0.0, wall.Bounds.Bottom);
        Assert.True(wall.Bounds.Top > pad.Bounds.Top + gym.PlayerBounds.Height);
        Assert.Equal(-1.0, liftFloor.Bounds.Top); // Existing vertical test is preserved.

        // The platform must never collide directly with either fixture.
        Assert.True(horizontal.Bounds.Bottom > pad.Bounds.Top);
        Assert.True(horizontal.Bounds.Right < wall.Bounds.Left);
        gym.Step(2.4, HorizontalDirection.Neutral, false); // Rightmost turnaround.
        Assert.True(gym.MovingPlatform.Bounds.Right < wall.Bounds.Left);
        Assert.Equal(0.60, Math.Round(
            wall.Bounds.Left - gym.MovingPlatform.Bounds.Right, 2));

        // A player standing on the pad overlaps the platform's Y range.
        Aabb waitingPlayer = Box(9.55, pad.Bounds.Top,
            wall.Bounds.Left, pad.Bounds.Top + gym.PlayerBounds.Height);
        Assert.True(waitingPlayer.Top > horizontal.Bounds.Bottom);
        Assert.True(waitingPlayer.Top > horizontal.Bounds.Top);
        Assert.True(waitingPlayer.Bottom < horizontal.Bounds.Bottom);
        Assert.NotEqual(waitingPlayer.Top, horizontal.Bounds.Top);
    }

    [Fact]
    public void ExistingHorizontalPlatformCrushesPlayerPinnedAgainstNewWall()
    {
        var gym = new MovementGymSession();
        StaticAabbCollider wall = Assert.Single(gym.Colliders, c => c.Id == 12);
        StaticAabbCollider pad = Assert.Single(gym.Colliders, c => c.Id == 11);

        Aabb player = Box(wall.Bounds.Left - gym.PlayerBounds.Width,
            pad.Bounds.Top, wall.Bounds.Left,
            pad.Bounds.Top + gym.PlayerBounds.Height);
        var platform = new MovingPlatformTrajectory(
            7, gym.MovingPlatform.Bounds, new Vector2D(2.4, 0.0), 2.4);
        var ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.18, 0.13, 0.38));
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.22, 0.16, 0.43));
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        var motor = new CharacterMovementController(player, gym.Colliders,
            ground, air, jump, gravityAcceleration: -24.0,
            movingPlatform: platform);

        Assert.True(motor.IsGrounded);
        motor.Step(2.15, HorizontalDirection.Neutral, false);
        Assert.Equal(player, motor.Bounds);
        double beforeCrush = motor.ElapsedSeconds;

        CharacterCrushException failure = Assert.Throws<CharacterCrushException>(
            () => motor.Step(0.10, HorizontalDirection.Neutral, false));
        Assert.Equal(CharacterCrushAxis.Horizontal, failure.Crush.Axis);
        Assert.Equal(7, failure.Crush.PushingColliderId);
        Assert.Equal(12, failure.Crush.OpposingColliderId);
        Assert.Equal(player, motor.Bounds); // Failed update was transactional.
        Assert.Equal(beforeCrush, motor.ElapsedSeconds);
    }

    [Fact]
    public void IsolatedPlatformGymsKeepTheirPreviousGeometry()
    {
        foreach (MovingPlatformGymMode mode in new[]
                 { MovingPlatformGymMode.Horizontal, MovingPlatformGymMode.Elevator })
        {
            var gym = new MovementGymSession(platformMode: mode);
            Assert.DoesNotContain(gym.Colliders, c => c.Id is 11 or 12 or 13 or 14 or 15);
        }
    }
}
