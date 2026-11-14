using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.Collision;

public sealed class JumpReferenceInterruptionTests
{
    private static Aabb Body => new(new Vector2D(0, 0), new Vector2D(1, 1));

    private static JumpReferenceCollisionPlan Plan() =>
        JumpReferenceCollisionPlan.Create(
            JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3))),
            HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35)),
            [new JumpTravelInputChange(0, HorizontalDirection.Right)]);

    [Fact]
    public void UnobstructedFlightReturnsReferenceEndAndDownwardVelocity()
    {
        var plan = Plan();
        var result = JumpReferenceInterruption.AdvanceToFirstContact(Body, plan, []);
        Assert.False(result.IsInterrupted);
        Assert.Null(result.Contact);
        Close(0.7, result.ElapsedSeconds);
        Close(0, result.RemainingSeconds);
        Close(6, result.Bounds.Left);
        Close(0, result.Bounds.Bottom);
        Close(10, result.Velocity.X);
        Assert.True(result.Velocity.Y < 0);
    }

    [Fact]
    public void SlidingWallKeepsTangentialVelocityAndReturnsRemainingTime()
    {
        var plan = Plan();
        var wall = new StaticAabbCollider(6,
            new Aabb(new Vector2D(2, -1), new Vector2D(2.1, 10)));
        var result = JumpReferenceInterruption.AdvanceToFirstContact(Body, plan, [wall]);
        Assert.True(result.IsInterrupted);
        Assert.Equal(6, result.Contact!.Value.ColliderId);
        Close(0.2, result.ElapsedSeconds);
        Close(0.5, result.RemainingSeconds);
        Close(2, result.Bounds.Right);
        Close(0, result.Velocity.X);
        Assert.True(result.Velocity.Y > 0);
        Close(result.Contact.Value.VelocityAtImpact.Y, result.Velocity.Y);
    }

    [Fact]
    public void StopPolicyZerosBothVelocityComponents()
    {
        var wall = new StaticAabbCollider(6,
            new Aabb(new Vector2D(2, -1), new Vector2D(2.1, 10)));
        var result = JumpReferenceInterruption.AdvanceToFirstContact(
            Body, Plan(), [wall], CollisionResponseMode.Stop);
        Assert.True(result.IsInterrupted);
        Assert.Equal(Vector2D.Zero, result.Velocity);
        Close(0.5, result.RemainingSeconds);
    }

    [Fact]
    public void CeilingContactStopsUpwardVelocityButKeepsHorizontalVelocity()
    {
        var ceiling = new StaticAabbCollider(3,
            new Aabb(new Vector2D(-10, 2), new Vector2D(20, 2.1)));
        var result = JumpReferenceInterruption.AdvanceToFirstContact(Body, Plan(), [ceiling]);
        Assert.True(result.IsInterrupted);
        Assert.Equal(new Vector2D(0, -1), result.Contact!.Value.Normal);
        Close(0, result.Velocity.Y);
        Assert.True(result.Velocity.X > 0);
        Assert.True(result.RemainingSeconds > 0);
        Close(2, result.Bounds.Top);
    }

    [Fact]
    public void CollisionAtReferenceEndpointHasZeroRemainingTime()
    {
        var floor = new StaticAabbCollider(5,
            new Aabb(new Vector2D(-10, -0.5), new Vector2D(30, 0)));
        var result = JumpReferenceInterruption.AdvanceToFirstContact(Body, Plan(), [floor]);
        Assert.True(result.IsInterrupted);
        Close(0.7, result.ElapsedSeconds);
        Close(0, result.RemainingSeconds);
        Close(0, result.Velocity.Y);
    }

    [Fact]
    public void UnknownCollisionResponseIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            JumpReferenceInterruption.AdvanceToFirstContact(
                Body, Plan(), [], (CollisionResponseMode)999));
    }

    private static void Close(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0, 1e-7);
}
