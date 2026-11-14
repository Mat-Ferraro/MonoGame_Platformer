using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

/// <summary>
/// Moving-floor movement phases can be shorter than a single representable
/// increment of the absolute simulation clock. The controller must consume
/// analytical cruise/reversal events without passing such a duration into
/// the independently validated MovingPlatformRideSolver.
/// </summary>
public sealed class MovingPlatformSubResolutionRideTests
{
    private static readonly double FixedStep = 1.0 / 60.0;

    private static (CharacterMovementController Motor, MovingPlatformTrajectory Platform)
        CreateRider(double accelerationTime = 0.18)
    {
        var platform = new MovingPlatformTrajectory(
            7, new Aabb(new Vector2D(-20.0, 0.0),
                        new Vector2D(20.0, 0.25)),
            new Vector2D(2.4, 0.0), 2.4);
        var ground = HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(
            9.0, accelerationTime, 0.13, accelerationTime + 0.20));
        var air = HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(
            9.0, 0.22, 0.16, 0.43));
        var flight = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4.0, 0.40, 0.30)));
        var motor = new CharacterMovementController(
            new Aabb(new Vector2D(0.0, 0.25),
                     new Vector2D(0.8, 1.85)),
            Array.Empty<StaticAabbCollider>(), ground, air, flight,
            -24.0, movingPlatform: platform);
        return (motor, platform);
    }

    [Fact]
    public void NearCruiseRidingPhaseIsResolvedWithoutAdvancingAnUnrepresentableClock()
    {
        var (motor, platform) = CreateRider();
        motor.Step(3.5, HorizontalDirection.Neutral, false);
        motor.Step(Math.BitDecrement(0.18), HorizontalDirection.Right, false);

        double platformSpeed = platform.Evaluate(motor.ElapsedSeconds).Velocity.X;
        double relativeSpeed = motor.Velocity.X - platformSpeed;
        double tinyCruiseTime = (9.0 - relativeSpeed) / 50.0;
        Assert.True(tinyCruiseTime > 0.0);
        Assert.Equal(motor.ElapsedSeconds, motor.ElapsedSeconds + tinyCruiseTime);

        var step = motor.Step(FixedStep, HorizontalDirection.Right, false);
        Assert.True(step.IsGrounded);
        Assert.Equal(7, motor.MovingSupportId);
        Assert.InRange(Math.Abs(9.0 -
            (step.Velocity.X - platform.Evaluate(motor.ElapsedSeconds).Velocity.X)),
            0.0, 1e-10);
    }

    [Fact]
    public void NearReversalStopOnMovingFloorTransitionsToOppositeAcceleration()
    {
        var (motor, platform) = CreateRider();
        motor.Step(3.5, HorizontalDirection.Neutral, false);
        motor.Step(0.18, HorizontalDirection.Right, false);
        double beforeReversalRelative = motor.Velocity.X -
            platform.Evaluate(motor.ElapsedSeconds).Velocity.X;
        Assert.InRange(Math.Abs(9.0 - beforeReversalRelative), 0, 1e-10);

        // Two representable double decrements leave a tiny positive velocity
        // after the nominal 0.20-second braking phase.
        double justBeforeStop = Math.BitDecrement(Math.BitDecrement(0.20));
        motor.Step(justBeforeStop, HorizontalDirection.Left, false);
        double relative = motor.Velocity.X -
            platform.Evaluate(motor.ElapsedSeconds).Velocity.X;
        Assert.True(relative > 0.0);
        double remainingStopTime = relative / 45.0;
        Assert.Equal(motor.ElapsedSeconds, motor.ElapsedSeconds + remainingStopTime);

        var step = motor.Step(FixedStep, HorizontalDirection.Left, false);
        Assert.True(step.IsGrounded);
        Assert.Equal(7, motor.MovingSupportId);
        Assert.True(step.Velocity.X -
            platform.Evaluate(motor.ElapsedSeconds).Velocity.X < 0.0);
    }

    [Fact]
    public void RideSolverStillRejectsAPositiveDurationThatCannotAdvanceWorldTime()
    {
        var (_, platform) = CreateRider();
        var rider = new Aabb(new Vector2D(0.0, 0.25),
                             new Vector2D(0.8, 1.85));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovingPlatformRideSolver.Step(platform,
                3.6111111111111032, 1.603655480141151e-18,
                rider, 0.0, 0.0));
    }

    [Fact]
    public void LongRunningRiderDoesNotHitMicroPhaseExceptionAcrossPresets()
    {
        foreach (double accelerationTime in new[] { 0.18, 0.10 })
        {
            var (motor, _) = CreateRider(accelerationTime);
            for (int i = 0; i < 900; i++)
            {
                HorizontalDirection input = i % 180 < 60
                    ? HorizontalDirection.Right
                    : i % 180 < 120
                        ? HorizontalDirection.Neutral
                        : HorizontalDirection.Left;
                motor.Step(FixedStep, input, false);
                Assert.True(motor.IsGrounded);
            }
        }
    }
}
