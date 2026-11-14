using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

/// <summary>
/// Milestone 24 Phase 2A: verifies the live controller consumes the shared
/// static/moving earliest-hit query without changing established movement.
/// The collision solver itself has its own broader Phase 1 test suite.
/// </summary>
public sealed class Milestone24LiveCollisionArbitrationTests
{
    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));

    private static MovingPlatformTrajectory MovingFloor(double bottom = 0.0) =>
        new(7, Box(-2.0, bottom, 2.0, bottom + 0.25),
            new Vector2D(2.0, 0.0), 1.0);

    private static CharacterMovementController Controller(
        Aabb body, StaticAabbCollider[] staticColliders,
        MovingPlatformTrajectory movingFloor)
    {
        HorizontalMotionProfile ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10.0, 0.2, 0.1, 0.35));
        HorizontalMotionProfile air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10.0, 0.2, 0.1, 0.35));
        JumpFlightTrajectory jump = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(4.0, 0.4, 0.3)));
        return new CharacterMovementController(
            body, staticColliders, ground, air, jump,
            gravityAcceleration: -20.0, movingPlatform: movingFloor);
    }

    [Fact]
    public void MovingFloorIsSelectedBeforeLowerStaticFloor()
    {
        // Falling from y=1.5 hits the moving floor top at y=0.25 before
        // reaching the static floor top at y=-1. No static overlap at start.
        var staticFloor = new StaticAabbCollider(
            1, Box(-10.0, -2.0, 10.0, -1.0));
        var motor = Controller(Box(0.0, 1.5, 0.8, 3.1),
            [staticFloor], MovingFloor());
        CharacterControlStepResult result = motor.Step(
            0.4, HorizontalDirection.Neutral, false);

        Assert.Equal(7, motor.MovingSupportId);
        Assert.True(result.IsGrounded);
        Assert.Contains(result.Contacts,
            c => c.ColliderId == 7 && c.Normal == new Vector2D(0, 1));
    }

    [Fact]
    public void StaticFloorIsSelectedBeforeLowerMovingFloor()
    {
        // The platform is below the stationary floor. The first contact
        // is static; the player must not become a moving-floor rider.
        var staticFloor = new StaticAabbCollider(
            1, Box(-10.0, -1.0, 10.0, 0.0));
        var motor = Controller(Box(0.0, 1.5, 0.8, 3.1),
            [staticFloor], MovingFloor(bottom: -2.0));
        CharacterControlStepResult result = motor.Step(
            0.5, HorizontalDirection.Neutral, false);

        Assert.True(result.IsGrounded);
        Assert.Null(motor.MovingSupportId);
        Assert.Contains(result.Contacts,
            c => c.ColliderId == 1 && c.Normal == new Vector2D(0, 1));
    }

    [Fact]
    public void NoContactWhenBothSurfacesAreOutOfReach()
    {
        var staticFloor = new StaticAabbCollider(
            1, Box(-10.0, -3.0, 10.0, -2.0));
        var motor = Controller(Box(0.0, 3.0, 0.8, 4.6),
            [staticFloor], MovingFloor(bottom: -4.0));
        CharacterControlStepResult result = motor.Step(
            0.1, HorizontalDirection.Neutral, false);

        Assert.Empty(result.Contacts);
        Assert.False(result.IsGrounded);
        Assert.Null(motor.MovingSupportId);
        Assert.True(result.Bounds.Bottom < 3.0);
    }

    [Fact]
    public void MovingFloorLandingContinuesAsRidingNextUpdate()
    {
        var motor = Controller(Box(0.0, 1.5, 0.8, 3.1),
            [], MovingFloor());
        motor.Step(0.4, HorizontalDirection.Neutral, false);
        Assert.Equal(7, motor.MovingSupportId);

        double firstLeft = motor.Bounds.Left;
        motor.Step(0.1, HorizontalDirection.Neutral, false);
        Assert.Equal(7, motor.MovingSupportId);
        Assert.InRange(motor.Bounds.Left - firstLeft, 0.1999999, 0.2000001);
    }
}
