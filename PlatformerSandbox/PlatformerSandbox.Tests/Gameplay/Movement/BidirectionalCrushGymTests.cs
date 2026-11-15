using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class BidirectionalCrushGymTests
{
    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));

    private static CharacterMovementController Motor(
        Aabb player, IReadOnlyList<StaticAabbCollider> statics,
        MovingPlatformTrajectory platform)
    {
        var ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.18, 0.13, 0.38));
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.22, 0.16, 0.43));
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        return new CharacterMovementController(player, statics, ground, air,
            jump, gravityAcceleration: -24.0, movingPlatform: platform);
    }

    [Fact]
    public void BothHorizontalSidesHaveUsableCrushStationsWithoutPlatformIntersection()
    {
        var gym = new MovementGymSession();
        var leftPad = Assert.Single(gym.Colliders, c => c.Id == 13);
        var leftWall = Assert.Single(gym.Colliders, c => c.Id == 14);
        var rightPad = Assert.Single(gym.Colliders, c => c.Id == 11);
        var rightWall = Assert.Single(gym.Colliders, c => c.Id == 12);
        var platform = Assert.Single(gym.MovingPlatforms, p => p.PlatformId == 7);

        Assert.Equal(4.70, leftWall.Bounds.Right);
        Assert.Equal(leftWall.Bounds.Right, leftPad.Bounds.Left);
        Assert.Equal(6.40, leftPad.Bounds.Right);
        Assert.Equal(1.00, leftPad.Bounds.Top);
        Assert.True(leftWall.Bounds.Top > leftPad.Bounds.Top + gym.PlayerBounds.Height);
        Assert.Equal(rightWall.Bounds.Left, rightPad.Bounds.Right);

        // Both walls stay 0.60 units away at the respective turnarounds.
        Assert.Equal(0.60, Math.Round(platform.Bounds.Left - leftWall.Bounds.Right, 2));
        Assert.True(platform.Bounds.Bottom > leftPad.Bounds.Top);
        Assert.True(platform.Bounds.Bottom > rightPad.Bounds.Top);
        Assert.False(AabbCollisionDetector.Overlaps(platform.Bounds, leftWall.Bounds));
        Assert.False(AabbCollisionDetector.Overlaps(platform.Bounds, leftPad.Bounds));

        var startPlatform = new MovingPlatformTrajectory(
            7, platform.Bounds, new Vector2D(2.4, 0.0), 2.4);
        Aabb rightmost = startPlatform.Evaluate(2.4).Bounds;
        Assert.Equal(0.60, Math.Round(rightWall.Bounds.Left - rightmost.Right, 2));
        Assert.False(AabbCollisionDetector.Overlaps(rightmost, rightWall.Bounds));
        Assert.False(AabbCollisionDetector.Overlaps(rightmost, rightPad.Bounds));

        // Both standing fixtures overlap the moving side vertically.
        Assert.True(leftPad.Bounds.Top < platform.Bounds.Bottom);
        Assert.True(leftPad.Bounds.Top + gym.PlayerBounds.Height > platform.Bounds.Top);
        Assert.True(rightPad.Bounds.Top + gym.PlayerBounds.Height > platform.Bounds.Top);
    }

    [Fact]
    public void ReturningPlatformCrushesPlayerAgainstLeftWallWithoutPartialCommit()
    {
        var gym = new MovementGymSession();
        var pad = Assert.Single(gym.Colliders, c => c.Id == 13);
        var wall = Assert.Single(gym.Colliders, c => c.Id == 14);
        Aabb player = Box(wall.Bounds.Right, pad.Bounds.Top,
            wall.Bounds.Right + gym.PlayerBounds.Width,
            pad.Bounds.Top + gym.PlayerBounds.Height);

        // This is the existing platform's leftward leg, with its local clock
        // starting at the rightmost turnaround. It keeps ID 7 and the same
        // bounds, speed and travel span as the live gym.
        var live = Assert.Single(gym.MovingPlatforms, p => p.PlatformId == 7);
        var returning = new MovingPlatformTrajectory(7,
            live.Bounds.Translated(new Vector2D(2.4, 0.0)),
            new Vector2D(-2.4, 0.0), 2.4);
        var motor = Motor(player, gym.Colliders, returning);

        Assert.True(motor.IsGrounded);
        motor.Step(2.15, HorizontalDirection.Neutral, false);
        Assert.Equal(player, motor.Bounds);
        double before = motor.ElapsedSeconds;

        CharacterCrushException failure = Assert.Throws<CharacterCrushException>(
            () => motor.Step(0.10, HorizontalDirection.Neutral, false));
        Assert.Equal(CharacterCrushAxis.Horizontal, failure.Crush.Axis);
        Assert.Equal(7, failure.Crush.PushingColliderId);
        Assert.Equal(14, failure.Crush.OpposingColliderId);
        Assert.Equal(before, motor.ElapsedSeconds);
        Assert.Equal(player, motor.Bounds);
    }

    [Fact]
    public void VerticalElevatorCanCrushPlayerIntoCeilingWhileRetainingFloorFixture()
    {
        var gym = new MovementGymSession();
        var floor = Assert.Single(gym.Colliders, c => c.Id == 10);
        var ceiling = Assert.Single(gym.Colliders, c => c.Id == 15);
        var liftSample = Assert.Single(gym.MovingPlatforms, p => p.PlatformId == 8);
        var lift = new MovingPlatformTrajectory(8, liftSample.Bounds,
            new Vector2D(0.0, 3.0), 2.0);

        Assert.Equal(-1.0, floor.Bounds.Top);
        Assert.Equal(4.30, ceiling.Bounds.Bottom);
        Assert.Equal(-8.40, ceiling.Bounds.Left);
        Assert.Equal(-5.40, ceiling.Bounds.Right);
        Assert.True(floor.Bounds.Top < liftSample.Bounds.Bottom);
        Assert.True(lift.Evaluate(2.0).Bounds.Top < ceiling.Bounds.Bottom);
        Assert.Equal(1.30, Math.Round(
            ceiling.Bounds.Bottom - lift.Evaluate(2.0).Bounds.Top, 2));

        Aabb player = Box(-7.35, liftSample.Bounds.Top,
            -6.55, liftSample.Bounds.Top + gym.PlayerBounds.Height);
        var motor = Motor(player, gym.Colliders, lift);
        Assert.Equal(8, motor.MovingSupportId);

        // The first leg stops before reaching the overhead collider.
        motor.Step(1.70, HorizontalDirection.Neutral, false);
        Assert.Equal(8, motor.MovingSupportId);
        Aabb before = motor.Bounds;
        double time = motor.ElapsedSeconds;

        CharacterCrushException failure = Assert.Throws<CharacterCrushException>(
            () => motor.Step(0.20, HorizontalDirection.Neutral, false));
        Assert.Equal(CharacterCrushAxis.Vertical, failure.Crush.Axis);
        Assert.Equal(8, failure.Crush.PushingColliderId);
        Assert.Equal(15, failure.Crush.OpposingColliderId);
        Assert.Equal(time, motor.ElapsedSeconds);
        Assert.Equal(before, motor.Bounds);
    }

    [Fact]
    public void NewlyAuthoredStaticCollidersDoNotOverlapOneAnotherOrMovingPaths()
    {
        var gym = new MovementGymSession();
        var statics = gym.Colliders;
        Assert.Equal(statics.Count, statics.Select(c => c.Id).Distinct().Count());
        foreach (var newCollider in statics.Where(c => c.Id is 13 or 14 or 15))
        {
            foreach (var other in statics.Where(c => c.Id != newCollider.Id))
                Assert.False(AabbCollisionDetector.Overlaps(newCollider.Bounds, other.Bounds),
                    $"Static {newCollider.Id} overlaps static {other.Id}.");
        }

        // Endpoint samples cover every axis-aligned extent of these
        // linear ping-pong trajectories, without relying on screen rendering.
        var horizontal = new MovingPlatformTrajectory(7,
            Assert.Single(gym.MovingPlatforms, p => p.PlatformId == 7).Bounds,
            new Vector2D(2.4, 0.0), 2.4);
        var lift = new MovingPlatformTrajectory(8,
            Assert.Single(gym.MovingPlatforms, p => p.PlatformId == 8).Bounds,
            new Vector2D(0.0, 3.0), 2.0);
        foreach (var collider in statics.Where(c => c.Id is 13 or 14 or 15))
        {
            Assert.False(AabbCollisionDetector.Overlaps(collider.Bounds, horizontal.Evaluate(0).Bounds));
            Assert.False(AabbCollisionDetector.Overlaps(collider.Bounds, horizontal.Evaluate(2.4).Bounds));
            Assert.False(AabbCollisionDetector.Overlaps(collider.Bounds, lift.Evaluate(0).Bounds));
            Assert.False(AabbCollisionDetector.Overlaps(collider.Bounds, lift.Evaluate(2.0).Bounds));
        }
    }
}
