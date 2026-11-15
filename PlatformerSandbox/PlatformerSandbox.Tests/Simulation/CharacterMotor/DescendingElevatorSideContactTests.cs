using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

/// <summary>Regression for a grounded character walking into a descending lift.</summary>
public sealed class DescendingElevatorSideContactTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    private static CharacterMovementController Motor(
        MovingPlatformTrajectory platform,
        IReadOnlyList<MovingPlatformTrajectory>? others = null)
    {
        var motion = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, .2, .1, .35));
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4, .4, .3)));
        return new CharacterMovementController(
            Box(0, 0, .8, 1.6),
            [new StaticAabbCollider(1, Box(-4, -1, 5, 0))],
            motion, motion, jump, -20,
            movingPlatform: platform,
            additionalMovingPlatforms: others);
    }

    [Fact]
    public void StaticGroundMustNotExcludeDescendingMovingSideWallFromSweep()
    {
        var descending = new MovingPlatformTrajectory(7,
            Box(1.3, .5, 2.3, .85), new Vector2D(0, -1.5), 1.0);
        var motor = Motor(descending);
        bool touched = false;
        for (int i = 0; i < 15; ++i)
        {
            var result = motor.Step(1.0 / 60.0,
                HorizontalDirection.Right, false);
            touched |= result.Contacts.Any(c => c.ColliderId == 7 && c.Normal.X < 0);
            Assert.False(AabbCollisionDetector.Overlaps(motor.Bounds,
                descending.Evaluate(motor.ElapsedSeconds).Bounds));
            Assert.True(motor.IsGrounded);
        }
        Assert.True(touched);
        Assert.InRange(motor.Bounds.Right, 0.0, descending.Evaluate(motor.ElapsedSeconds).Bounds.Left + 1e-8);
    }

    [Fact]
    public void SecondaryDescendingPlatformIsAlsoCollisionCheckedWhileOnStaticFloor()
    {
        var distantHorizontal = new MovingPlatformTrajectory(7,
            Box(10, 2, 12, 2.25), new Vector2D(1, 0), 1.0);
        var descending = new MovingPlatformTrajectory(8,
            Box(1.3, .5, 2.3, .85), new Vector2D(0, -1.5), 1.0);
        var motor = Motor(distantHorizontal, [descending]);
        for (int i = 0; i < 15; ++i)
        {
            motor.Step(1.0 / 60.0, HorizontalDirection.Right, false);
            Assert.False(AabbCollisionDetector.Overlaps(motor.Bounds,
                descending.Evaluate(motor.ElapsedSeconds).Bounds));
            Assert.True(motor.IsGrounded);
        }
    }

    [Fact]
    public void SecondaryPlatformCanBeTheAuthoritativeSupport()
    {
        var horizontal = new MovingPlatformTrajectory(7,
            Box(10, 2, 12, 2.25), new Vector2D(1, 0), 1);
        var lift = new MovingPlatformTrajectory(8,
            Box(-1, -.35, 1, 0), new Vector2D(0, 3), 2);
        var motion = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, .2, .1, .35));
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4, .4, .3)));
        var motor = new CharacterMovementController(
            Box(0, 0, .8, 1.6), [], motion, motion, jump, -20,
            movingPlatform: horizontal, additionalMovingPlatforms: [lift]);
        Assert.Equal(8, motor.MovingSupportId);
        for (int i = 0; i < 240; ++i)
        {
            motor.Step(1.0 / 60.0, HorizontalDirection.Neutral, false);
            Assert.True(motor.IsGrounded);
            Assert.Equal(8, motor.MovingSupportId);
            Assert.Equal(lift.Evaluate(motor.ElapsedSeconds).Bounds.Top, motor.Bounds.Bottom);
        }
    }

    [Fact]
    public void DuplicateMovingColliderIdsAreRejected()
    {
        var a = new MovingPlatformTrajectory(7,
            Box(4, 0, 5, .3), new Vector2D(0, 2), 1);
        var b = new MovingPlatformTrajectory(7,
            Box(9, 0, 10, .3), new Vector2D(0, 2), 1);
        Assert.Throws<ArgumentException>(() => Motor(a, [b]));
    }
}
