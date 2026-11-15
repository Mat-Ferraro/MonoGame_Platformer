using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.MovingPlatforms;

public sealed class Milestone26WorldObstructionPolicyTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    [Fact]
    public void ContinuousStaticObstructionFindsActualEntryWithoutSampling()
    {
        var moving = new MovingPlatformTrajectory(7, Box(0, 0, 1, 1),
            new Vector2D(2, 0), 2);
        var wall = new StaticAabbCollider(3, Box(1.5, 0, 2.5, 1));
        var before = MovingPlatformObstructionDetector.FindFirst(
            0, 0.5, [wall], [moving]);
        Assert.Null(before); // endpoint touch is not positive-area overlap
        var eventHit = MovingPlatformObstructionDetector.FindFirst(
            0, 0.75, [wall], [moving]);
        Assert.NotNull(eventHit);
        Assert.Equal(7, eventHit.Value.MovingPlatformId);
        Assert.Equal(3, eventHit.Value.OtherColliderId);
        Assert.False(eventHit.Value.OtherIsMoving);
        Assert.InRange(Math.Abs(eventHit.Value.WorldTimeSeconds - 0.5), 0, 1e-12);
    }

    [Fact]
    public void MovingMovingApproachUsesRelativeVelocity()
    {
        var left = new MovingPlatformTrajectory(9, Box(0, 0, 1, 1),
            new Vector2D(2, 0), 2);
        var right = new MovingPlatformTrajectory(4, Box(3, 0, 4, 1),
            new Vector2D(-2, 0), 2);
        var hit = MovingPlatformObstructionDetector.FindFirst(0, 1.5,
            Array.Empty<StaticAabbCollider>(), [left, right]);
        Assert.NotNull(hit);
        Assert.Equal(4, hit.Value.MovingPlatformId);
        Assert.Equal(9, hit.Value.OtherColliderId);
        Assert.True(hit.Value.OtherIsMoving);
        Assert.InRange(Math.Abs(hit.Value.WorldTimeSeconds - 1), 0, 1e-12);
    }

    [Fact]
    public void GrazingCornerAndTurnaroundDoNotProduceFalseObstruction()
    {
        var moving = new MovingPlatformTrajectory(7, Box(0, 0, 1, 1),
            new Vector2D(2, 2), 2);
        var wall = new StaticAabbCollider(2, Box(2, 0, 3, 1));
        var hit = MovingPlatformObstructionDetector.FindFirst(
            0, 4, [wall], [moving]);
        Assert.Null(hit);
    }

    [Fact]
    public void InitialWorldPenetrationIsReportedAtStartTime()
    {
        var moving = new MovingPlatformTrajectory(7, Box(0, 0, 1, 1),
            new Vector2D(2, 0), 2);
        var wall = new StaticAabbCollider(2, Box(0.5, 0, 2, 1));
        var hit = MovingPlatformObstructionDetector.FindFirst(
            0, 0, [wall], [moving]);
        Assert.Equal(0.0, hit!.Value.WorldTimeSeconds);
    }

    private static CharacterMovementController MakeMotor(
        PlatformObstructionPolicy policy)
    {
        var horizontal = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(8, .2, .1, .35));
        var flight = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(2, .4, .4)));
        var moving = new MovingPlatformTrajectory(7, Box(0, 2, 1, 3),
            new Vector2D(2, 0), 2);
        var wall = new StaticAabbCollider(3, Box(1.5, 2, 2.5, 3));
        return new CharacterMovementController(Box(10, 0, 10.8, 1.6),
            [wall], horizontal, horizontal, flight, -20,
            movingPlatform: moving, obstructionPolicy: policy);
    }

    [Fact]
    public void RejectPolicyFailsBeforeCommittingPlayerState()
    {
        var motor = MakeMotor(PlatformObstructionPolicy.RejectWorldObstructions);
        Aabb before = motor.Bounds;
        var fault = Assert.Throws<PlatformObstructionException>(() =>
            motor.Step(0.75, HorizontalDirection.Neutral, false));
        Assert.Equal(7, fault.Obstruction.MovingPlatformId);
        Assert.Equal(3, fault.Obstruction.OtherColliderId);
        Assert.Equal(before, motor.Bounds);
        Assert.Equal(0, motor.ElapsedSeconds);
        Assert.NotNull(motor.LastFault);
    }

    [Fact]
    public void DefaultAuthoredTrajectoryPolicyRetainsLegacyMovement()
    {
        var motor = MakeMotor(PlatformObstructionPolicy.FollowAuthoredTrajectories);
        motor.Step(0.75, HorizontalDirection.Neutral, false);
        Assert.Equal(0.75, motor.ElapsedSeconds);
        Assert.Equal(PlatformObstructionPolicy.FollowAuthoredTrajectories,
            motor.ObstructionPolicy);
    }
}
