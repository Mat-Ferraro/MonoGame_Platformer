using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.MovingPlatforms;

/// <summary>
/// Regression for a rider held stationary by a static wall while its platform
/// moves underneath it. Cancelling velocities must not accumulate X drift.
/// </summary>
public sealed class MovingPlatformWallPinPrecisionTests
{
    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));

    private static MovingPlatformTrajectory MovingFloor() => new(
        7, Box(8.0, 2.05, 9.75, 2.30),
        new Vector2D(1.0, 0.0), oneWayDurationSeconds: 10.0);

    private static StaticAabbCollider Wall() =>
        new(12, Box(10.35, 0.0, 10.75, 3.60));

    private static Aabb FlushRider() =>
        Box(9.549999999999999, 2.30, 10.35, 3.90);

    [Fact]
    public void OppositeRelativeVelocityLeavesStationaryWorldFacesBitExact()
    {
        var platform = MovingFloor();
        Aabb body = FlushRider();
        double now = 0.0;
        const double dt = 1.0 / 60.0;
        for (int frame = 0; frame < 180; frame++)
        {
            double platformVx = platform.Evaluate(now).Velocity.X;
            var next = MovingPlatformRideSolver.Step(
                platform, now, dt, body, -platformVx, 0.0);
            Assert.True(next.IsSupported);
            Assert.Equal(body.Left, next.Bounds.Left);
            Assert.Equal(body.Right, next.Bounds.Right);
            Assert.Equal(2.30, next.Bounds.Bottom);
            Assert.Equal(0.0, next.WorldVelocity.X);
            body = next.Bounds;
            now += dt;
        }
    }

    [Fact]
    public void LiveControllerRemainsOutsideWallWhilePlatformSlidesUnderRider()
    {
        var platform = MovingFloor();
        var wall = Wall();
        var ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.18, 0.13, 0.38));
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.22, 0.16, 0.43));
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        var motor = new CharacterMovementController(
            FlushRider(), [wall], ground, air, jump,
            gravityAcceleration: -24.0, movingPlatform: platform,
            faultPolicy: SimulationFaultPolicy.Development);

        Assert.Equal(7, motor.MovingSupportId);
        bool observedWallContact = false;
        for (int frame = 0; frame < 180; frame++)
        {
            CharacterControlStepResult result = motor.Step(
                1.0 / 60.0, HorizontalDirection.Neutral, false);
            observedWallContact |= result.Contacts.Any(c =>
                c.ColliderId == wall.Id && c.Normal.X == -1.0);
            Assert.False(AabbCollisionDetector.Overlaps(motor.Bounds, wall.Bounds));
            Assert.Equal(wall.Bounds.Left, motor.Bounds.Right);
            Assert.Equal(7, motor.MovingSupportId);
            Assert.True(motor.IsGrounded);
        }
        Assert.True(observedWallContact);
    }

    [Fact]
    public void RealInitialWallPenetrationStillThrows()
    {
        var platform = MovingFloor();
        var wall = Wall();
        var ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9.0, 0.18, 0.13, 0.38));
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4.0, 0.4, 0.3)));
        Assert.Throws<InvalidOperationException>(() =>
            new CharacterMovementController(
                Box(9.55, 2.30, 10.3501, 3.90), [wall],
                ground, ground, jump, -24.0, movingPlatform: platform));
    }
}
