using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class MovingPlatformCornerContactTests
{
    private static Aabb Box(double x, double y, double width = 0.8, double height = 1.6) =>
        new(new Vector2D(x, y), new Vector2D(x + width, y + height));

    private static MovingPlatformTrajectory Platform() =>
        new(7, Box(-1, 0, 4, 0.25), new Vector2D(2, 0), 1.0);

    private static CharacterMovementController Controller(
        Aabb initial, MovingPlatformTrajectory platform,
        params StaticAabbCollider[] obstacles)
    {
        var ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        var held = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4, 0.4, 0.3)));
        return new CharacterMovementController(initial, obstacles,
            ground, air, held, -20, movingPlatform: platform);
    }

    [Fact]
    public void MovingPlatformThatCatchesAirbornePlayerFromSideCannotPassThrough()
    {
        var platform = Platform();
        var motor = Controller(Box(3.1, 0.1), platform);
        CharacterControlStepResult step = motor.Step(
            0.10, HorizontalDirection.Neutral, false);
        Assert.Contains(step.Contacts,
            c => c.ColliderId == 7 && c.Normal == new Vector2D(1, 0));
        Assert.False(AabbCollisionDetector.Overlaps(
            motor.Bounds, platform.Evaluate(motor.ElapsedSeconds).Bounds));
        Assert.Null(motor.MovingSupportId);
    }

    [Fact]
    public void AirborneCornerApproachLandsOnlyIfEnteringFromAbove()
    {
        var platform = Platform();
        var motor = Controller(Box(3.1, 0.30), platform);
        CharacterControlStepResult step = motor.Step(
            0.10, HorizontalDirection.Neutral, false);
        Assert.True(step.IsGrounded);
        Assert.Equal(7, motor.MovingSupportId);
        Assert.Contains(step.Contacts,
            c => c.ColliderId == 7 && c.Normal == new Vector2D(0, 1));
        Assert.False(AabbCollisionDetector.Overlaps(
            motor.Bounds, platform.Evaluate(motor.ElapsedSeconds).Bounds));
    }

    [Fact]
    public void RisingIntoMovingPlatformUndersideDoesNotBecomeFloorSupport()
    {
        var platform = Platform();
        var staticFloor = new StaticAabbCollider(1, Box(-5, -3, 12, 1));
        var motor = Controller(Box(0, -2), platform, staticFloor);
        CharacterControlStepResult step = motor.Step(
            0.10, HorizontalDirection.Neutral, true);
        Assert.Contains(step.Contacts,
            c => c.ColliderId == 7 && c.Normal == new Vector2D(0, -1));
        Assert.False(step.IsGrounded);
        Assert.Null(motor.MovingSupportId);
        Assert.False(AabbCollisionDetector.Overlaps(
            motor.Bounds, platform.Evaluate(motor.ElapsedSeconds).Bounds));
    }

    [Fact]
    public void RepeatedSixtyHertzUpdatesDoNotCreatePenetrationAfterSideContact()
    {
        var platform = Platform();
        var motor = Controller(Box(3.1, 0.1), platform);
        bool sideObserved = false;
        for (int frame = 0; frame < 120; frame++)
        {
            var step = motor.Step(1.0 / 60.0, HorizontalDirection.Neutral, false);
            sideObserved |= step.Contacts.Any(c =>
                c.ColliderId == 7 && c.Normal.X != 0.0);
            Assert.False(AabbCollisionDetector.Overlaps(
                motor.Bounds, platform.Evaluate(motor.ElapsedSeconds).Bounds));
        }
        Assert.True(sideObserved);
    }
}
