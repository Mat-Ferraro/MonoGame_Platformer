using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class VerticalElevatorRuntimeTests
{
    private static Aabb Box(double x, double y, double width = 0.8, double height = 1.6) =>
        new(new Vector2D(x, y), new Vector2D(x + width, y + height));

    private static MovingPlatformTrajectory Elevator(double rise = 2.0,
        double oneWay = 1.0) => new(7, Box(-1, 0, 4, 0.25),
            new Vector2D(0, rise), oneWay);

    private static CharacterMovementController Motor(Aabb initial,
        MovingPlatformTrajectory? elevator = null) => new(
        initial, Array.Empty<StaticAabbCollider>(),
        HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10, .2, .1, .35)),
        HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10, .2, .1, .35)),
        JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4, .4, .3))),
        gravityAcceleration: -20,
        movingPlatform: elevator);

    private static void Near(double expected, double actual, double tolerance = 1e-7) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, tolerance);

    [Fact]
    public void ElevatorCarriesRiderBothWaysWithoutTransformParenting()
    {
        var lift = Elevator();
        var motor = Motor(Box(0, .25), lift);
        Assert.True(motor.IsGrounded);
        Assert.Equal(7, motor.MovingSupportId);
        motor.Step(.50, HorizontalDirection.Neutral, false);
        Near(1.25, motor.Bounds.Bottom);
        Near(2, motor.Velocity.Y);
        motor.Step(.75, HorizontalDirection.Neutral, false);
        Near(1.75, motor.Bounds.Bottom);
        Near(-2, motor.Velocity.Y);
        Assert.True(motor.IsGrounded);
        motor.Step(.75, HorizontalDirection.Neutral, false);
        Near(.25, motor.Bounds.Bottom);
        Assert.Equal(7, motor.MovingSupportId);
    }

    [Fact]
    public void UpwardLaunchInheritsVerticalSpeedExactlyOnce()
    {
        var motor = Motor(Box(0, .25), Elevator());
        var reference = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4, .4, .3)));
        var started = motor.Step(.1, HorizontalDirection.Neutral, true);
        Assert.True(started.JumpStarted);
        Assert.Null(motor.MovingSupportId);
        Assert.False(motor.IsGrounded);
        Near(.25 + reference.Evaluate(.1).VerticalDisplacement + .2,
            motor.Bounds.Bottom);
        Near(reference.Evaluate(.1).VerticalVelocity + 2.0, motor.Velocity.Y);
    }

    [Fact]
    public void DownwardLaunchInheritsNegativeVerticalSpeedWithoutStoppingTheJump()
    {
        var lift = Elevator();
        var motor = Motor(Box(0, .25), lift);
        motor.Step(1.0, HorizontalDirection.Neutral, false);
        Near(-2.0, motor.Velocity.Y);
        double baseY = motor.Bounds.Bottom;
        var reference = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4, .4, .3)));
        var result = motor.Step(.05, HorizontalDirection.Neutral, true);
        Assert.True(result.JumpStarted);
        Near(baseY + reference.Evaluate(.05).VerticalDisplacement - .1,
            motor.Bounds.Bottom);
        Near(reference.Evaluate(.05).VerticalVelocity - 2.0, motor.Velocity.Y);
        Assert.True(motor.IsJumping);
    }

    [Fact]
    public void RisingElevatorCanCatchFallingPlayer()
    {
        var lift = Elevator();
        var motor = Motor(Box(0, 2.0), lift);
        motor.Step(.5, HorizontalDirection.Neutral, false);
        Assert.True(motor.IsGrounded);
        Assert.Equal(7, motor.MovingSupportId);
        Near(lift.Evaluate(.5).Bounds.Top, motor.Bounds.Bottom);
        Near(2, motor.Velocity.Y);
    }

    [Fact]
    public void JumpAfterElevatorTurnUsesCurrentTakeoffVelocity()
    {
        var lift = Elevator();
        var motor = Motor(Box(0, .25), lift);
        motor.Step(.9, HorizontalDirection.Neutral, false);
        motor.Step(.1, HorizontalDirection.Neutral, false);
        Near(-2, motor.Velocity.Y);
        double y = motor.Bounds.Bottom;
        motor.Step(.05, HorizontalDirection.Neutral, true);
        Assert.True(motor.Bounds.Bottom > y);
        Assert.True(motor.IsJumping);
    }

    [Fact]
    public void LongRunningElevatorRideRemainsAtExactSupportPlane()
    {
        var lift = Elevator(3, 1.3);
        var motor = Motor(Box(0, .25), lift);
        for (int i = 0; i < 1200; ++i)
        {
            motor.Step(1.0 / 60.0, HorizontalDirection.Neutral, false);
            Assert.Equal(7, motor.MovingSupportId);
            Assert.True(motor.IsGrounded);
            Assert.Equal(lift.Evaluate(motor.ElapsedSeconds).Bounds.Top,
                motor.Bounds.Bottom);
        }
    }

    [Fact]
    public void SteeringDuringRepeatedElevatorReversalsPreservesExactSupportAtCommittedClock()
    {
        var lift = new MovingPlatformTrajectory(7,
            Box(-50, 0, 100, .25), new Vector2D(0, 3), 1.3);
        var motor = Motor(Box(0, .25), lift);
        for (int i = 0; i < 1800; ++i)
        {
            HorizontalDirection direction = i % 240 < 60
                ? HorizontalDirection.Right
                : i % 240 < 120
                    ? HorizontalDirection.Neutral
                    : i % 240 < 180
                        ? HorizontalDirection.Left
                        : HorizontalDirection.Neutral;
            motor.Step(1.0 / 60.0, direction, false);
            Assert.True(motor.IsGrounded);
            Assert.Equal(7, motor.MovingSupportId);
            Assert.Equal(lift.Evaluate(motor.ElapsedSeconds).Bounds.Top,
                motor.Bounds.Bottom);
        }
    }

    [Fact]
    public void InvalidInheritedVerticalLaunchVelocityIsRejected()
    {
        var flight = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4, .4, .3)));
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, .2, .1, .35));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SteerableJumpMotor(
            Box(0, 1), 0, air, flight, Array.Empty<StaticAabbCollider>(),
            -20, inheritedVerticalVelocity: double.NaN));
    }

    [Fact]
    public void HorizontalMovingPlatformBehaviorStillWorks()
    {
        var platform = new MovingPlatformTrajectory(7,
            Box(-1, 0, 4, .25), new Vector2D(2, 0), 1);
        var motor = Motor(Box(0, .25), platform);
        motor.Step(.3, HorizontalDirection.Neutral, false);
        Near(.6, motor.Bounds.Left);
        Near(.25, motor.Bounds.Bottom);
        Assert.Equal(7, motor.MovingSupportId);
    }
}
