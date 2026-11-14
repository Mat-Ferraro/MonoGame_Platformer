using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

/// <summary>
/// Reproduces a motion-phase boundary shorter than one ULP of the absolute
/// simulation clock. This used to throw in MovingPlatformCollisionDetector
/// even when the moving platform was nowhere near the character.
/// </summary>
public sealed class MovingPlatformSubResolutionTimeTests
{
    private static MovingPlatformTrajectory DistantPlatform() => new(
        7,
        new Aabb(new Vector2D(1000, 0), new Vector2D(1004, 0.25)),
        new Vector2D(2, 0), 1.0);

    private static CharacterMovementController MakeAirborneController()
    {
        var motion = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9, 0.2, 0.1, 0.35));
        var held = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4, 0.4, 0.3)));
        return new CharacterMovementController(
            new Aabb(new Vector2D(0, 10), new Vector2D(0.8, 11.6)),
            Array.Empty<StaticAabbCollider>(), motion, motion, held, -20,
            movingPlatform: DistantPlatform());
    }

    [Fact]
    public void AlmostAtCruiseSpeedDoesNotCreateAnUnrepresentableMovingSweep()
    {
        var motor = MakeAirborneController();
        motor.Step(20.0, HorizontalDirection.Neutral, false);
        motor.Step(0.19999999999999996, HorizontalDirection.Right, false);

        double residualTime = (9.0 - motor.Velocity.X) / 45.0;
        Assert.True(residualTime > 0.0);
        Assert.Equal(motor.ElapsedSeconds, motor.ElapsedSeconds + residualTime);

        CharacterControlStepResult result = motor.Step(
            1.0 / 60.0, HorizontalDirection.Right, false);

        Assert.Equal(9.0, result.Velocity.X);
        Assert.False(result.IsGrounded);
        Assert.True(result.ElapsedSeconds > 20.2);
    }

    [Fact]
    public void AlmostAtReversalStopDoesNotCreateAnUnrepresentableMovingSweep()
    {
        var motor = MakeAirborneController();
        motor.Step(20.0, HorizontalDirection.Neutral, false);
        motor.Step(0.2, HorizontalDirection.Right, false);
        Assert.Equal(9.0, motor.Velocity.X);

        // Stop a single representable interval before the exact reversal time.
        double reversalRate = 9.0 / (0.35 - 0.2);
        double justBeforeStop = Math.BitDecrement(9.0 / reversalRate);
        motor.Step(justBeforeStop, HorizontalDirection.Left, false);
        Assert.True(motor.Velocity.X > 0.0);
        double residualTime = motor.Velocity.X / reversalRate;
        Assert.Equal(motor.ElapsedSeconds, motor.ElapsedSeconds + residualTime);

        CharacterControlStepResult result = motor.Step(
            1.0 / 60.0, HorizontalDirection.Left, false);
        Assert.True(result.Velocity.X < 0.0);
    }

    [Fact]
    public void MovingPlatformDetectorStillRejectsUnrepresentableAbsoluteTimeRanges()
    {
        var body = new Aabb(new Vector2D(0, 10), new Vector2D(0.8, 11.6));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovingPlatformCollisionDetector.SweepFirst(
                body, Vector2D.Zero, new Vector2D(0, -20),
                20.071069360564575, 5.2427198385076837e-19,
                [DistantPlatform()]));
    }
}
