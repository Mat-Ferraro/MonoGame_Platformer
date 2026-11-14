using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class MovingPlatformBoundaryContactTests
{
    [Fact]
    public void FlushApproachingPlatformReportsSideContactAndDoesNotPenetrate()
    {
        var platform = new MovingPlatformTrajectory(
            7,
            new Aabb(new Vector2D(-1.0, 0.0), new Vector2D(3.0, 0.25)),
            new Vector2D(2.0, 0.0),
            1.0);
        var player = new Aabb(
            new Vector2D(3.0, 0.1), new Vector2D(3.8, 1.7));
        HorizontalMotionProfile horizontal = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10.0, 0.2, 0.1, 0.35));
        JumpFlightTrajectory jump = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(4.0, 0.4, 0.3)));
        var motor = new CharacterMovementController(
            player, [], horizontal, horizontal, jump, -20.0,
            movingPlatform: platform);

        CharacterControlStepResult first = motor.Step(
            1.0 / 60.0, HorizontalDirection.Neutral, false);

        CharacterControlContact contact = Assert.Single(first.Contacts,
            c => c.ColliderId == 7 && c.Normal == new Vector2D(1.0, 0.0));
        Assert.Equal(0.0, contact.TimeWithinStepSeconds);
        Assert.Equal(2.0, contact.OutgoingVelocity.X, 12);
        Assert.False(AabbCollisionDetector.Overlaps(
            motor.Bounds, platform.Evaluate(motor.ElapsedSeconds).Bounds));

        // Once constrained, the next frame should not re-report an identical
        // contact event while continuing to ride along the side.
        CharacterControlStepResult second = motor.Step(
            1.0 / 60.0, HorizontalDirection.Neutral, false);
        Assert.DoesNotContain(second.Contacts,
            c => c.ColliderId == 7 && c.Normal.X != 0.0);
        Assert.False(AabbCollisionDetector.Overlaps(
            motor.Bounds, platform.Evaluate(motor.ElapsedSeconds).Bounds));
    }
}
