using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class Milestone27CeilingCornerAssistTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    private static readonly StaticAabbCollider Ceiling = new(20,
        Box(0.65, 1.8, 3.0, 2.2));
    private static readonly Aabb Player = Box(0, 0, 0.8, 1.6);

    private static UnifiedCollisionHit CeilingImpact() => new(
        20, false, 0.02, new Vector2D(0, -1),
        Box(0.0, 0.2, 0.8, 1.8), new Vector2D(0.0, 5.0), Vector2D.Zero);

    private static HorizontalMotionProfile Air() =>
        HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(9, 0.22, 0.16, 0.43));

    private static JumpFlightTrajectory Jump() =>
        JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(2.0, 0.4, 0.3)));

    private static SteerableJumpMotor CreateMotor(CollisionAssistancePolicy policy,
        IReadOnlyList<StaticAabbCollider>? obstacles = null) =>
        new(Player, 0.0, Air(), Jump(), obstacles ?? [Ceiling], -24.0,
            assistancePolicy: policy);

    [Fact]
    public void PolicyValidatesBothIndependentBoundaries()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CollisionAssistancePolicy(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CollisionAssistancePolicy(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CollisionAssistancePolicy(-0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CollisionAssistancePolicy(0.1, -1));
        Assert.False(CollisionAssistancePolicy.Disabled.CeilingCornersEnabled);
        Assert.False(new CollisionAssistancePolicy(0.1, 0).CeilingCornersEnabled);
        Assert.True(new CollisionAssistancePolicy(0.18, 2).CeilingCornersEnabled);
    }

    [Fact]
    public void PlannerEscapesOnlyTheRequestedDirectionWithinBound()
    {
        var policy = new CollisionAssistancePolicy(0.18);
        Aabb? left = CeilingCornerAssistPlanner.TryPlan(
            CeilingImpact(), HorizontalDirection.Left, policy,
            [Ceiling], [], 0.02);
        Assert.NotNull(left);
        Assert.Equal(Ceiling.Bounds.Left, left.Value.Right);
        Assert.Equal(Ceiling.Bounds.Bottom, left.Value.Top);
        Assert.False(AabbCollisionDetector.Overlaps(left.Value, Ceiling.Bounds));
        Assert.Null(CeilingCornerAssistPlanner.TryPlan(
            CeilingImpact(), HorizontalDirection.Right, policy,
            [Ceiling], [], 0.02));
        Aabb? neutral = CeilingCornerAssistPlanner.TryPlan(
            CeilingImpact(), HorizontalDirection.Neutral, policy,
            [Ceiling], [], 0.02);
        Assert.Equal(left, neutral);
    }

    [Fact]
    public void NarrowBudgetAndDisabledPolicyKeepTheCeilingBlocking()
    {
        Assert.Null(CeilingCornerAssistPlanner.TryPlan(
            CeilingImpact(), HorizontalDirection.Left,
            new CollisionAssistancePolicy(0.14), [Ceiling], [], 0.02));
        Assert.Null(CeilingCornerAssistPlanner.TryPlan(
            CeilingImpact(), HorizontalDirection.Left,
            CollisionAssistancePolicy.Disabled, [Ceiling], [], 0.02));
    }

    [Fact]
    public void AnotherStaticSolidAlongTheShiftCancelsAssistance()
    {
        var blocker = new StaticAabbCollider(21, Box(-0.1, 0, -0.05, 2.1));
        Assert.Null(CeilingCornerAssistPlanner.TryPlan(
            CeilingImpact(), HorizontalDirection.Left,
            new CollisionAssistancePolicy(0.18), [Ceiling, blocker], [], 0.02));
    }

    [Fact]
    public void MovingSolidAlongTheShiftCancelsAssistance()
    {
        var moving = new MovingPlatformTrajectory(7,
            Box(-0.11, 0, -0.06, 2.1), new Vector2D(0, 1.0), 1.0);
        Assert.Null(CeilingCornerAssistPlanner.TryPlan(
            CeilingImpact(), HorizontalDirection.Left,
            new CollisionAssistancePolicy(0.18), [Ceiling], [moving], 0.02));
    }

    [Fact]
    public void RejectsUnknownMovingCeilingsAndDownwardHits()
    {
        UnifiedCollisionHit upward = CeilingImpact();
        var moving = upward with { IsMovingPlatform = true };
        var downward = upward with { BodyVelocityAtImpact = new Vector2D(0, -5) };
        var policy = new CollisionAssistancePolicy(0.18);
        Assert.Null(CeilingCornerAssistPlanner.TryPlan(moving,
            HorizontalDirection.Left, policy, [Ceiling], [], 0.02));
        Assert.Null(CeilingCornerAssistPlanner.TryPlan(downward,
            HorizontalDirection.Left, policy, [Ceiling], [], 0.02));
    }

    [Fact]
    public void AssistedJumpPreservesUpwardReferencePastCeilingCorner()
    {
        var assisted = CreateMotor(new CollisionAssistancePolicy(0.18));
        var baseline = CreateMotor(CollisionAssistancePolicy.Disabled);

        SteerableJumpStepResult helped = assisted.Step(0.08, HorizontalDirection.Left);
        SteerableJumpStepResult unassisted = baseline.Step(0.08, HorizontalDirection.Left);

        Assert.Single(helped.Assists);
        Assert.Equal(20, helped.Assists[0].ColliderId);
        Assert.InRange(Math.Abs(helped.Assists[0].HorizontalCorrectionUnits), 0.000001, 0.18);
        Assert.True(helped.Bounds.Top > Ceiling.Bounds.Bottom);
        Assert.True(helped.Velocity.Y > 0);
        Assert.False(AabbCollisionDetector.Overlaps(helped.Bounds, Ceiling.Bounds));
        Assert.Empty(unassisted.Assists);
        Assert.True(unassisted.Bounds.Top <= Ceiling.Bounds.Bottom);
    }

    [Fact]
    public void DisabledModeDoesNotChangeOriginalJumpContactOrTrajectory()
    {
        var original = new SteerableJumpMotor(Player, 0.0, Air(), Jump(), [Ceiling], -24.0);
        var explicitDisabled = CreateMotor(CollisionAssistancePolicy.Disabled);
        var a = original.Step(0.08, HorizontalDirection.Left);
        var b = explicitDisabled.Step(0.08, HorizontalDirection.Left);
        Assert.Equal(a.Bounds, b.Bounds);
        Assert.Equal(a.Velocity, b.Velocity);
        Assert.Equal(a.Contacts, b.Contacts);
    }

    [Fact]
    public void AssistanceIsRepeatableAcrossIdenticalFixedUpdateSchedules()
    {
        var policy = new CollisionAssistancePolicy(0.18);
        var a = CreateMotor(policy);
        var b = CreateMotor(policy);
        for (int i = 0; i < 6; i++)
        {
            var aa = a.Step(1.0 / 60.0, HorizontalDirection.Left);
            var bb = b.Step(1.0 / 60.0, HorizontalDirection.Left);
            Assert.Equal(aa.Bounds, bb.Bounds);
            Assert.Equal(aa.Velocity, bb.Velocity);
            Assert.Equal(aa.Assists, bb.Assists);
            Assert.False(AabbCollisionDetector.Overlaps(aa.Bounds, Ceiling.Bounds));
        }
    }
}
