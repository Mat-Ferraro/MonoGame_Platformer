using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class Milestone27FinalAssistanceTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    private static CollisionAssistancePolicy Full => new(0.18, 2,
        groundStepHeightUnits: 0.12, groundStepInwardUnits: 0.04,
        ledgeCatchHorizontalUnits: 0.16, ledgeCatchVerticalUnits: 0.15);

    private static HorizontalMotionProfile Motion() =>
        HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(
            9, 0.22, 0.16, 0.43));

    private static JumpFlightTrajectory Jump() =>
        JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(2.0, 0.4, 0.3)));

    [Fact]
    public void PolicyBoundariesAreIndependentlyValidatedAndDefaultIsDisabled()
    {
        Assert.False(CollisionAssistancePolicy.Disabled.AnyEnabled);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollisionAssistancePolicy(
            0.18, groundStepHeightUnits: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollisionAssistancePolicy(
            0.18, groundStepInwardUnits: -0.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollisionAssistancePolicy(
            0.18, ledgeCatchHorizontalUnits: double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CollisionAssistancePolicy(
            0.18, ledgeCatchVerticalUnits: -1.0));
        Assert.True(Full.AnyEnabled);
    }

    [Fact]
    public void MovingUndersideCorrectionUsesAuthoritativeEventPosition()
    {
        var moving = new MovingPlatformTrajectory(30,
            Box(0.65, 1.8, 3.0, 2.2), new Vector2D(0.0, 0.4), 1.0);
        double hitTime = 0.25;
        Aabb ceiling = moving.Evaluate(hitTime).Bounds;
        UnifiedCollisionHit hit = new(30, true, hitTime,
            new Vector2D(0, -1),
            Box(0, ceiling.Bottom - 1.6, 0.8, ceiling.Bottom),
            new Vector2D(0, 5), moving.Evaluate(hitTime).Velocity);
        Aabb? candidate = CeilingCornerAssistPlanner.TryPlan(hit,
            HorizontalDirection.Left, Full, [], [moving], hitTime);
        Assert.NotNull(candidate);
        Assert.Equal(ceiling.Bottom, candidate.Value.Top);
        Assert.Equal(ceiling.Left, candidate.Value.Right);
        Assert.False(AabbCollisionDetector.Overlaps(candidate.Value, ceiling));
        Assert.Null(CeilingCornerAssistPlanner.TryPlan(hit,
            HorizontalDirection.Right, Full, [], [moving], hitTime));
    }

    [Fact]
    public void MovingUndersideCannotBeAssistedWhenRelativeMotionIsNotRising()
    {
        var moving = new MovingPlatformTrajectory(30,
            Box(0.65, 1.8, 3.0, 2.2), new Vector2D(0.0, 6.0), 1.0);
        double t = 0.25;
        Aabb obstacle = moving.Evaluate(t).Bounds;
        UnifiedCollisionHit hit = new(30, true, 0.0,
            new Vector2D(0, -1),
            Box(0, obstacle.Bottom - 1.6, 0.8, obstacle.Bottom),
            new Vector2D(0, 5), moving.Evaluate(t).Velocity);
        Assert.Null(CeilingCornerAssistPlanner.TryPlan(hit,
            HorizontalDirection.Left, Full, [], [moving], t));
    }

    [Fact]
    public void MovingCornerCorrectionRefusesAStaticEscapeBlocker()
    {
        var moving = new MovingPlatformTrajectory(30,
            Box(0.65, 1.8, 3.0, 2.2), new Vector2D(0.0, 0.4), 1.0);
        double t = 0.25;
        Aabb obstacle = moving.Evaluate(t).Bounds;
        var blocker = new StaticAabbCollider(31,
            Box(-0.10, obstacle.Bottom - 1.2, -0.05, obstacle.Bottom + 0.4));
        UnifiedCollisionHit hit = new(30, true, 0.0,
            new Vector2D(0, -1),
            Box(0, obstacle.Bottom - 1.6, 0.8, obstacle.Bottom),
            new Vector2D(0, 5), moving.Evaluate(t).Velocity);
        Assert.Null(CeilingCornerAssistPlanner.TryPlan(hit,
            HorizontalDirection.Left, Full, [blocker], [moving], t));
    }

    [Fact]
    public void RealJumpCanClearAHorizontalMovingUndersideOrRemainBlocked()
    {
        var ceiling = new MovingPlatformTrajectory(30,
            Box(0.65, 1.8, 3.0, 2.2), new Vector2D(0.4, 0.0), 1.0);
        var helped = new SteerableJumpMotor(Box(0, 0, 0.8, 1.6),
            0.0, Motion(), Jump(), [], -24.0,
            movingPlatform: ceiling, assistancePolicy: Full);
        var control = new SteerableJumpMotor(Box(0, 0, 0.8, 1.6),
            0.0, Motion(), Jump(), [], -24.0,
            movingPlatform: ceiling);
        var a = helped.Step(0.08, HorizontalDirection.Left);
        var b = control.Step(0.08, HorizontalDirection.Left);
        Assert.Single(a.Assists);
        Assert.Equal(30, a.Assists[0].ColliderId);
        Assert.True(a.Bounds.Top > b.Bounds.Top);
        Assert.False(AabbCollisionDetector.Overlaps(a.Bounds,
            ceiling.Evaluate(0.08).Bounds));
        Assert.Empty(b.Assists);
    }

    [Fact]
    public void GroundMicroStepAndDisabledBaselineRemainIndependent()
    {
        var floor = new StaticAabbCollider(1, Box(-2, -1, 2, 0));
        var smallStep = new StaticAabbCollider(2, Box(0.8, -0.2, 1.4, 0.10));
        Aabb body = Box(0, 0, 0.8, 1.6);
        var plan = GroundContactAssistPlanner.TryStepUp(body,
            Vector2D.Zero, HorizontalDirection.Right, Full,
            [floor, smallStep], [], 0.0);
        Assert.NotNull(plan);
        Assert.Equal(smallStep.Bounds.Top, plan.Value.Bounds.Bottom);
        Assert.True(plan.Value.Bounds.Right > smallStep.Bounds.Left);
        Assert.Equal(CollisionAssistKind.GroundStep, plan.Value.Event.Kind);
        Assert.Null(GroundContactAssistPlanner.TryStepUp(body,
            Vector2D.Zero, HorizontalDirection.Right,
            CollisionAssistancePolicy.Disabled, [floor, smallStep], [], 0.0));
    }

    [Fact]
    public void GroundStepRejectsTallWallsAndOverheadBlockers()
    {
        var floor = new StaticAabbCollider(1, Box(-2, -1, 2, 0));
        var tall = new StaticAabbCollider(2, Box(0.8, -0.2, 1.4, 0.30));
        Aabb body = Box(0, 0, 0.8, 1.6);
        Assert.Null(GroundContactAssistPlanner.TryStepUp(body,
            Vector2D.Zero, HorizontalDirection.Right, Full,
            [floor, tall], [], 0.0));
        var small = new StaticAabbCollider(2, Box(0.8, -0.2, 1.4, 0.10));
        var overhead = new StaticAabbCollider(3, Box(0.0, 1.63, 0.7, 2.0));
        Assert.Null(GroundContactAssistPlanner.TryStepUp(body,
            Vector2D.Zero, HorizontalDirection.Right, Full,
            [floor, small, overhead], [], 0.0));
    }

    [Fact]
    public void NarrowLedgeCatchIsDirectionalBoundedAndPreservesHeight()
    {
        var floor = new StaticAabbCollider(5, Box(0.9, -1, 3, 0));
        Aabb body = Box(0, 0.10, 0.8, 1.7);
        var result = GroundContactAssistPlanner.TryLedgeCatch(
            body, new Vector2D(0, -2), HorizontalDirection.Right,
            Full, [floor], [], 0.0);
        Assert.NotNull(result);
        Assert.Equal(CollisionAssistKind.LedgeCatch, result.Value.Event.Kind);
        Assert.Equal(body.Bottom, result.Value.Bounds.Bottom);
        Assert.InRange(result.Value.Event.HorizontalCorrectionUnits, 0.0, 0.16);
        Assert.Null(GroundContactAssistPlanner.TryLedgeCatch(
            body, new Vector2D(0, -2), HorizontalDirection.Left,
            Full, [floor], [], 0.0));
        Assert.Null(GroundContactAssistPlanner.TryLedgeCatch(
            body, new Vector2D(0, 2), HorizontalDirection.Right,
            Full, [floor], [], 0.0));
    }

    [Fact]
    public void MovingLedgeCatchHonorsRelativeDescentAndPathSafety()
    {
        var platform = new MovingPlatformTrajectory(8,
            Box(0.9, -1, 2.0, 0), new Vector2D(0, 0.2), 1.0);
        Aabb body = Box(0, 0.20, 0.8, 1.80);
        var plan = GroundContactAssistPlanner.TryLedgeCatch(
            body, new Vector2D(0, -2), HorizontalDirection.Right,
            Full, [], [platform], 0.5);
        Assert.NotNull(plan);
        Assert.Equal(8, plan.Value.Event.ColliderId);
        Assert.Null(GroundContactAssistPlanner.TryLedgeCatch(
            body, new Vector2D(0, 2), HorizontalDirection.Right,
            Full, [], [platform], 0.5));
    }

    [Fact]
    public void ControllerReportsGroundStepAssistanceWhenEnabled()
    {
        var floor = new StaticAabbCollider(1, Box(-2, -1, 2, 0));
        var step = new StaticAabbCollider(2, Box(0.8, -0.2, 1.4, 0.10));
        var enabled = new CharacterMovementController(Box(0, 0, 0.8, 1.6),
            [floor, step], Motion(), Motion(), Jump(), -24.0,
            assistancePolicy: Full);
        var disabled = new CharacterMovementController(Box(0, 0, 0.8, 1.6),
            [floor, step], Motion(), Motion(), Jump(), -24.0);
        var a = enabled.Step(1.0 / 60.0, HorizontalDirection.Right, false);
        var b = disabled.Step(1.0 / 60.0, HorizontalDirection.Right, false);
        Assert.Single(a.Assists);
        Assert.Equal(CollisionAssistKind.GroundStep, a.Assists[0].Kind);
        Assert.True(a.Bounds.Bottom > b.Bounds.Bottom);
        Assert.True(a.IsGrounded);
        Assert.Empty(b.Assists);
    }

    [Fact]
    public void ControllerLedgeCatchIsReportedOnlyOncePerAirborneEpisode()
    {
        var floor = new StaticAabbCollider(5, Box(0.90, -1.0, 3, 0));
        var motor = new CharacterMovementController(Box(0, 0.10, 0.8, 1.70),
            [floor], Motion(), Motion(), Jump(), -24.0,
            assistancePolicy: Full);
        var a = motor.Step(1.0 / 60.0, HorizontalDirection.Right, false);
        Assert.Single(a.Assists);
        Assert.Equal(CollisionAssistKind.LedgeCatch, a.Assists[0].Kind);
        var b = motor.Step(1.0 / 60.0, HorizontalDirection.Right, false);
        Assert.Empty(b.Assists);
    }
}
