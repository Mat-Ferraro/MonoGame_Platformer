using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Integration;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class Milestone26Phase2BAnalyticalManifoldTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    private static MotionSample2D Constant(double time) =>
        new(new Vector2D(time, -time), new Vector2D(1, -1));

    private static SteerableJumpMotor PinnedJump(int eventBudget = 8,
        IReadOnlyList<StaticAabbCollider>? obstacles = null)
    {
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, .2, .1, .35));
        var flight = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(2, .4, .4)));
        return new SteerableJumpMotor(Box(0, 0, .8, 1.6), 3,
            air, flight, obstacles ??
            [new StaticAabbCollider(8, Box(.8, -2, 1.2, 3)),
             new StaticAabbCollider(3, Box(-1, 1.6, 2, 2))],
            -20, maxContactEvents: eventBudget);
    }

    [Fact]
    public void AnalyticalCornerReturnsBothContactsAtExactFirstTime()
    {
        var body = Box(0, 0, 1, 1);
        var wall = new StaticAabbCollider(8, Box(1, -2, 2, 2));
        var floor = new StaticAabbCollider(3, Box(-2, -1, 2, 0));

        var contacts = UnifiedCollisionEventScheduler.SweepAnalyticalCoincident(
            body, Constant, 0, .25, [wall, floor],
            Array.Empty<MovingPlatformTrajectory>());

        Assert.Equal(new[] { 3, 8 }, contacts.Select(c => c.ColliderId));
        Assert.All(contacts, c => Assert.Equal(0.0, c.TimeSeconds));
        Assert.Equal(new Vector2D(0, 1), contacts[0].Normal);
        Assert.Equal(new Vector2D(-1, 0), contacts[1].Normal);
    }

    [Fact]
    public void AnalyticalContactSetIndependentOfStaticEnumerationOrder()
    {
        var body = Box(0, 0, 1, 1);
        var wall = new StaticAabbCollider(8, Box(1, -2, 2, 2));
        var floor = new StaticAabbCollider(3, Box(-2, -1, 2, 0));
        var a = UnifiedCollisionEventScheduler.SweepAnalyticalCoincident(
            body, Constant, 0, .2, [wall, floor],
            Array.Empty<MovingPlatformTrajectory>());
        var b = UnifiedCollisionEventScheduler.SweepAnalyticalCoincident(
            body, Constant, 0, .2, [floor, wall],
            Array.Empty<MovingPlatformTrajectory>());
        Assert.Equal(a.ToArray(), b.ToArray());
    }

    [Fact]
    public void DifferentAnalyticalTimesAreNotMerged()
    {
        var body = Box(0, 0, 1, 1);
        var wall = new StaticAabbCollider(8, Box(1.5, -2, 2, 2));
        var floor = new StaticAabbCollider(3, Box(-2, -1, 2, 0));
        var contacts = UnifiedCollisionEventScheduler.SweepAnalyticalCoincident(
            body, Constant, 0, .6, [wall, floor],
            Array.Empty<MovingPlatformTrajectory>());
        Assert.Single(contacts);
        Assert.Equal(3, contacts[0].ColliderId);
        Assert.Equal(0.0, contacts[0].TimeSeconds);
    }

    [Fact]
    public void SingleAnalyticalContactPreservesFirstHit()
    {
        var body = Box(0, 0, 1, 1);
        var wall = new StaticAabbCollider(8, Box(1.25, -2, 2, 2));
        var original = UnifiedCollisionEventScheduler.SweepAnalyticalFirst(
            body, Constant, 0, .5, [wall],
            Array.Empty<MovingPlatformTrajectory>());
        var contacts = UnifiedCollisionEventScheduler.SweepAnalyticalCoincident(
            body, Constant, 0, .5, [wall],
            Array.Empty<MovingPlatformTrajectory>());
        Assert.NotNull(original);
        Assert.Single(contacts);
        Assert.Equal(original.Value, contacts[0]);
    }

    [Fact]
    public void ExactFirstContactCanIncludeMovingPusherAndStaticWall()
    {
        var body = Box(0, 0, 1, 1);
        var wall = new StaticAabbCollider(3, Box(1, -1, 2, 2));
        var pusher = new MovingPlatformTrajectory(7,
            Box(-1, 0, 0, 1), new Vector2D(2, 0), 1);
        MotionSample2D MoveRight(double t) =>
            new(new Vector2D(t, 0), new Vector2D(1, 0));
        var contacts = UnifiedCollisionEventScheduler.SweepAnalyticalCoincident(
            body, MoveRight, 0, .2, [wall], [pusher]);
        Assert.Equal(new[] { 3, 7 }, contacts.Select(c => c.ColliderId));
        Assert.All(contacts, c => Assert.Equal(0.0, c.TimeSeconds));
        var failure = Assert.Throws<CharacterCrushException>(() =>
            SimultaneousContactResolver.Resolve(new Vector2D(1, 0),
                Vector2D.Zero, contacts,
                PlatformerSandbox.Simulation.Collision.Resolution.CollisionResponseMode.Slide,
                0, body));
        Assert.Equal(CharacterCrushAxis.Horizontal, failure.Crush.Axis);
    }

    [Fact]
    public void JumpHittingCeilingAndWallProcessesBothAtSameInstant()
    {
        var motor = PinnedJump();
        var result = motor.Step(1.0 / 60.0, HorizontalDirection.Right);
        Assert.Equal(new[] { 3, 8 }, result.Contacts.Select(c => c.ColliderId));
        Assert.All(result.Contacts, c => Assert.Equal(0.0, c.TimeWithinStepSeconds));
        Assert.Equal(.8, result.Bounds.Right);
        Assert.True(result.Bounds.Top <= 1.6);
        Assert.False(result.IsGrounded);
        // Ceilings interrupt the Y reference; they don't interrupt the X
        // constraint, which remains in effect on subsequent fixed updates.
        for (int i = 0; i < 3; ++i)
        {
            result = motor.Step(1.0 / 60.0, HorizontalDirection.Right);
            Assert.Equal(.8, result.Bounds.Right);
        }
    }

    [Fact]
    public void MultiContactBudgetFailureIsAtomic()
    {
        var motor = PinnedJump(eventBudget: 1);
        Aabb original = motor.Bounds;
        Vector2D velocity = motor.Velocity;
        SimulationEventLimitException exception = Assert.Throws<SimulationEventLimitException>(
            () => motor.Step(1.0 / 60.0, HorizontalDirection.Right));
        Assert.Equal(original, motor.Bounds);
        Assert.Equal(velocity, motor.Velocity);
        Assert.Equal(0.0, motor.FlightElapsedSeconds);
        Assert.Contains("Jump collision event limit exceeded", exception.Message);
    }

    [Fact]
    public void CoincidentQueryStillRejectsInitialPenetration()
    {
        var body = Box(0, 0, 1, 1);
        var wall = new StaticAabbCollider(1, Box(.5, -.5, 2, 2));
        Assert.Throws<InvalidOperationException>(() =>
            UnifiedCollisionEventScheduler.SweepAnalyticalCoincident(
                body, Constant, 0, .2, [wall],
                Array.Empty<MovingPlatformTrajectory>()));
    }
}
