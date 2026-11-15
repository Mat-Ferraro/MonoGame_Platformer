using System.Reflection;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class Milestone26RideHandoffRegressionTests
{
    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));

    private static MovingPlatformTrajectory Elevator() => new(
        8, Box(-7.7, -0.35, -6.2, 0.0), new Vector2D(0.0, 3.0), 2.0);

    private static CharacterMovementController Motor(
        MovingPlatformTrajectory lift,
        IReadOnlyList<StaticAabbCollider>? statics = null) =>
        new(Box(-7.1, 0.0, -6.3, 1.6), statics ?? [],
            HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10, .2, .1, .35)),
            HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10, .2, .1, .35)),
            JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
                new JumpConstraints(4.0, .4, .3))),
            gravityAcceleration: -20.0, movingPlatform: lift);

    private static void SetBoundsForBoundaryRegression(
        CharacterMovementController motor, Aabb bounds)
    {
        // Test-only injection of the rounding drift observed between a
        // collision-query timestamp and a ride-solver support sample.
        FieldInfo field = typeof(CharacterMovementController).GetField(
            "_bounds", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Controller bounds field missing.");
        field.SetValue(motor, bounds);
    }

    [Fact]
    public void SupportedRiderNormalizesAnUlpAtPhaseEntryBeforeCallingStrictRideSolver()
    {
        MovingPlatformTrajectory lift = Elevator();
        CharacterMovementController motor = Motor(lift);
        motor.Step(.73, HorizontalDirection.Neutral, false);
        Assert.Equal(8, motor.MovingSupportId);

        Aabb original = motor.Bounds;
        double slightlyHigh = Math.BitIncrement(original.Bottom);
        Aabb drifted = Box(original.Left, slightlyHigh,
            original.Right, slightlyHigh + original.Height);
        SetBoundsForBoundaryRegression(motor, drifted);
        Assert.NotEqual(lift.Evaluate(motor.ElapsedSeconds).Bounds.Top,
            motor.Bounds.Bottom);

        motor.Step(1.0 / 60.0, HorizontalDirection.Neutral, false);
        Assert.Equal(8, motor.MovingSupportId);
        Assert.Equal(lift.Evaluate(motor.ElapsedSeconds).Bounds.Top,
            motor.Bounds.Bottom);
        Assert.True(motor.IsGrounded);
    }

    [Fact]
    public void ARealGapCannotBeTreatedAsAnExistingMovingSupport()
    {
        MovingPlatformTrajectory lift = Elevator();
        CharacterMovementController motor = Motor(lift);
        motor.Step(.73, HorizontalDirection.Neutral, false);
        Aabb original = motor.Bounds;
        Aabb invalid = original.Translated(new Vector2D(0, .01));
        SetBoundsForBoundaryRegression(motor, invalid);
        double startTime = motor.ElapsedSeconds;

        Assert.Throws<SimulationNoProgressException>(() =>
            motor.Step(1.0 / 60.0, HorizontalDirection.Neutral, false));
        Assert.Equal(invalid, motor.Bounds);
        Assert.Equal(startTime, motor.ElapsedSeconds);
        Assert.Equal(8, motor.MovingSupportId);
    }

    [Fact]
    public void JumpingFromRisingElevatorCanLandAndRideAgain()
    {
        MovingPlatformTrajectory lift = Elevator();
        CharacterMovementController motor = Motor(lift);
        Assert.Equal(8, motor.MovingSupportId);

        var launch = motor.Step(1.0 / 60.0, HorizontalDirection.Neutral, true);
        Assert.True(launch.JumpStarted);
        Assert.Null(motor.MovingSupportId);

        bool landed = false;
        for (int i = 0; i < 180; ++i)
        {
            motor.Step(1.0 / 60.0, HorizontalDirection.Neutral, false);
            if (motor.MovingSupportId != 8) continue;
            landed = true;
            break;
        }
        Assert.True(landed);
        Assert.True(motor.IsGrounded);
        Assert.Equal(lift.Evaluate(motor.ElapsedSeconds).Bounds.Top,
            motor.Bounds.Bottom);

        for (int i = 0; i < 60; ++i)
        {
            motor.Step(1.0 / 60.0, HorizontalDirection.Neutral, false);
            Assert.Equal(8, motor.MovingSupportId);
            Assert.Equal(lift.Evaluate(motor.ElapsedSeconds).Bounds.Top,
                motor.Bounds.Bottom);
        }
    }

    [Fact]
    public void AClassifiedCrushDoesNotPreventAFreshMotorFromLandingOnElevator()
    {
        MovingPlatformTrajectory lift = Elevator();
        var ceiling = new StaticAabbCollider(3,
            Box(-7.8, 1.9, -6.1, 2.2));
        CharacterMovementController beforeRespawn = Motor(lift, [ceiling]);
        Assert.Throws<CharacterCrushException>(() =>
            beforeRespawn.Step(.4, HorizontalDirection.Neutral, false));
        Assert.Equal(0.0, beforeRespawn.ElapsedSeconds);

        // The gym's classified crush response creates a new controller and
        // clears input buffering. Exercise its next jump + landing + ride,
        // without substituting the numerical Resilient fallback.
        CharacterMovementController afterRespawn = Motor(lift);
        afterRespawn.Step(1.0 / 60.0, HorizontalDirection.Neutral, true);
        bool landed = false;
        for (int i = 0; i < 180; ++i)
        {
            afterRespawn.Step(1.0 / 60.0, HorizontalDirection.Neutral, false);
            if (afterRespawn.MovingSupportId != 8) continue;
            landed = true;
            break;
        }
        Assert.True(landed);
        afterRespawn.Step(1.0 / 60.0, HorizontalDirection.Neutral, false);
        Assert.Equal(8, afterRespawn.MovingSupportId);
        Assert.Equal(lift.Evaluate(afterRespawn.ElapsedSeconds).Bounds.Top,
            afterRespawn.Bounds.Bottom);
    }
}
