using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class JumpKinematicMotorTests
{
    private static Aabb Body => new(new Vector2D(0, 0), new Vector2D(1, 1));
    private static Vector2D Gravity => new(0, -20);

    private static JumpReferenceCollisionPlan Plan(
        HorizontalDirection direction = HorizontalDirection.Right)
    {
        var jump = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3)));
        var horizontal = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        return JumpReferenceCollisionPlan.Create(jump, horizontal,
            [new JumpTravelInputChange(0, direction)]);
    }

    private static StaticAabbCollider Floor(int id = 5) => new(id,
        new Aabb(new Vector2D(-30, -0.5), new Vector2D(30, 0)));
    private static StaticAabbCollider Ceiling(int id = 3) => new(id,
        new Aabb(new Vector2D(-10, 2), new Vector2D(30, 2.1)));
    private static StaticAabbCollider Wall(int id = 7) => new(id,
        new Aabb(new Vector2D(2, -10), new Vector2D(2.1, 10)));

    private static void Near(double expected, double actual, double tolerance = 1e-7) =>
        Assert.InRange(Math.Abs(actual - expected), 0, tolerance);

    [Fact]
    public void InitialStateIsExactTakeoffSample()
    {
        var plan = Plan();
        var motor = new JumpKinematicMotor(Body, plan, []);
        var sample = plan.Reference.Evaluate(0);
        Assert.Equal(JumpMotorPhase.Reference, motor.Phase);
        Assert.Equal(Body, motor.Bounds);
        Near(sample.HorizontalVelocity, motor.Velocity.X);
        Near(sample.VerticalVelocity, motor.Velocity.Y);
        Near(0, motor.ElapsedSeconds);
        Assert.False(motor.IsGrounded);
    }

    [Fact]
    public void UnobstructedMotionSamplesOriginalJumpExactly()
    {
        var plan = Plan();
        var motor = new JumpKinematicMotor(Body, plan, []);
        var actual = motor.Step(0.2, Gravity);
        var expected = plan.Reference.Evaluate(0.2);
        Near(expected.HorizontalPosition, actual.Bounds.Left);
        Near(expected.VerticalDisplacement, actual.Bounds.Bottom);
        Near(expected.HorizontalVelocity, actual.Velocity.X);
        Near(expected.VerticalVelocity, actual.Velocity.Y);
        Assert.Equal(JumpMotorPhase.Reference, actual.Phase);
        Assert.Empty(actual.Contacts);
    }

    [Fact]
    public void VariableStepPartitionDoesNotAlterReference()
    {
        var plan = Plan();
        var first = new JumpKinematicMotor(Body, plan, []);
        var second = new JumpKinematicMotor(Body, plan, []);
        first.Step(0.3, new Vector2D(999, -999));
        second.Step(0.1, Gravity);
        second.Step(0.2, Gravity);
        Near(first.Bounds.Left, second.Bounds.Left);
        Near(first.Bounds.Bottom, second.Bounds.Bottom);
        Near(first.Velocity.X, second.Velocity.X);
        Near(first.Velocity.Y, second.Velocity.Y);
    }

    [Fact]
    public void ReferenceAccelerationInputDoesNotOverrideDesignerTrajectory()
    {
        var plan = Plan();
        var a = new JumpKinematicMotor(Body, plan, []);
        var b = new JumpKinematicMotor(Body, plan, []);
        a.Step(0.1, new Vector2D(0, 0));
        b.Step(0.1, new Vector2D(200, -200));
        Assert.Equal(a.Bounds, b.Bounds);
        Assert.Equal(a.Velocity, b.Velocity);
    }

    [Fact]
    public void CrossingReferenceEndpointContinuesWithFreshAcceleratedMotion()
    {
        var plan = Plan();
        var motor = new JumpKinematicMotor(Body, plan, []);
        var result = motor.Step(plan.TotalDurationSeconds + 0.1, Gravity);
        var endpoint = plan.Reference.Evaluate(plan.TotalDurationSeconds);
        Near(0.7, plan.TotalDurationSeconds);
        Near(endpoint.HorizontalPosition + endpoint.HorizontalVelocity * 0.1, result.Bounds.Left);
        Near(endpoint.VerticalVelocity * 0.1 - 0.1, result.Bounds.Bottom);
        Near(endpoint.VerticalVelocity - 2, result.Velocity.Y);
        Assert.Equal(JumpMotorPhase.FreeMotion, result.Phase);
        Assert.Empty(result.Contacts);
    }

    [Fact]
    public void ExactlyAtReferenceEndpointStartsFreshMotionNextUpdate()
    {
        var plan = Plan();
        var motor = new JumpKinematicMotor(Body, plan, []);
        var atEnd = motor.Step(plan.TotalDurationSeconds, Gravity);
        Assert.Equal(JumpMotorPhase.FreeMotion, atEnd.Phase);
        Near(0, atEnd.Bounds.Bottom);
        Assert.True(atEnd.Velocity.Y < 0);
        var after = motor.Step(0.05, Gravity);
        Assert.True(after.Bounds.Bottom < 0);
        Assert.Empty(after.Contacts);
    }

    [Fact]
    public void FloorContactAtReferenceEndpointIsRecordedAndSupported()
    {
        var plan = Plan();
        var motor = new JumpKinematicMotor(Body, plan, [Floor()]);
        var result = motor.Step(0.7, Gravity);
        Assert.Single(result.Contacts);
        Assert.Equal(5, result.Contacts[0].ColliderId);
        Near(0.7, result.Contacts[0].TimeWithinStepSeconds);
        Assert.Equal(new Vector2D(0, 1), result.Contacts[0].Normal);
        Near(0, result.Velocity.Y);
        Assert.True(result.IsGrounded);
        Assert.Equal(JumpMotorPhase.FreeMotion, result.Phase);
    }

    [Fact]
    public void FloorLandingContinuesTangentialMotionDuringSameUpdate()
    {
        var motor = new JumpKinematicMotor(Body, Plan(), [Floor()]);
        var result = motor.Step(0.75, Gravity);
        Near(6.5, result.Bounds.Left);
        Near(0, result.Bounds.Bottom);
        Near(10, result.Velocity.X);
        Near(0, result.Velocity.Y);
        Assert.Single(result.Contacts); // no duplicate floor event within this same step
        Assert.True(result.IsGrounded);
    }

    [Fact]
    public void CeilingHitEndsReferenceAndMovesDownwardAfterImpact()
    {
        var motor = new JumpKinematicMotor(Body, Plan(), [Ceiling()]);
        var result = motor.Step(0.3, Gravity);
        Assert.Single(result.Contacts);
        Assert.Equal(new Vector2D(0, -1), result.Contacts[0].Normal);
        Near(2, result.Contacts[0].BoundsAtImpact.Top);
        Assert.True(result.Bounds.Top < 2);
        Assert.True(result.Velocity.Y < 0);
        Assert.Equal(JumpMotorPhase.FreeMotion, result.Phase);
    }

    [Fact]
    public void WallHitPreservesUpwardMotionAndBlocksHorizontalPush()
    {
        var motor = new JumpKinematicMotor(Body, Plan(), [Wall()]);
        var result = motor.Step(0.3, new Vector2D(50, -20));
        Assert.Single(result.Contacts);
        Assert.Equal(new Vector2D(-1, 0), result.Contacts[0].Normal);
        Near(0.2, result.Contacts[0].TimeSinceTakeoffSeconds);
        Near(2, result.Bounds.Right);
        Near(0, result.Velocity.X);
        Assert.True(result.Velocity.Y > 0);
    }

    [Fact]
    public void CeilingThenFloorCanProduceMultipleContactsInOneUpdate()
    {
        var motor = new JumpKinematicMotor(Body, Plan(), [Ceiling(), Floor()]);
        var result = motor.Step(0.6, new Vector2D(0, -30));
        Assert.True(result.Contacts.Count >= 2);
        Assert.Equal(3, result.Contacts[0].ColliderId);
        Assert.Contains(result.Contacts, hit => hit.ColliderId == 5);
        Near(0, result.Bounds.Bottom);
        Assert.True(result.IsGrounded);
    }

    [Fact]
    public void CeilingHitCanBeSplitAcrossUpdatesWithoutChangingOutcome()
    {
        var plan = Plan();
        var obstacles = new[] { Ceiling() };
        var longStep = new JumpKinematicMotor(Body, plan, obstacles);
        var split = new JumpKinematicMotor(Body, plan, obstacles);
        var one = longStep.Step(0.3, Gravity);
        split.Step(0.1, Gravity);
        var two = split.Step(0.2, Gravity);
        Near(one.Bounds.Left, two.Bounds.Left);
        Near(one.Bounds.Bottom, two.Bounds.Bottom);
        Near(one.Velocity.X, two.Velocity.X);
        Near(one.Velocity.Y, two.Velocity.Y);
    }

    [Fact]
    public void WallHitCanBeSplitAcrossUpdatesWithSamePosition()
    {
        var plan = Plan();
        var obstacles = new[] { Wall() };
        var longStep = new JumpKinematicMotor(Body, plan, obstacles);
        var split = new JumpKinematicMotor(Body, plan, obstacles);
        var one = longStep.Step(0.4, new Vector2D(50, -20));
        split.Step(0.25, new Vector2D(50, -20));
        var two = split.Step(0.15, new Vector2D(50, -20));
        Near(one.Bounds.Right, two.Bounds.Right);
        Near(one.Bounds.Bottom, two.Bounds.Bottom);
    }

    [Fact]
    public void StopOnImpactFreezesPositionAcrossLaterSteps()
    {
        var motor = new JumpKinematicMotor(Body, Plan(), [Wall()], CollisionResponseMode.Stop);
        var hit = motor.Step(0.4, new Vector2D(0, -20));
        Assert.Equal(JumpMotorPhase.Halted, hit.Phase);
        Assert.Equal(Vector2D.Zero, hit.Velocity);
        var later = motor.Step(0.2, new Vector2D(100, 100));
        Assert.Equal(hit.Bounds, later.Bounds);
        Assert.Equal(Vector2D.Zero, later.Velocity);
        Assert.Empty(later.Contacts);
        Near(0.6, later.ElapsedSeconds);
    }

    [Fact]
    public void ZeroDurationIsPureSnapshotWithoutForcingEvent()
    {
        var motor = new JumpKinematicMotor(Body, Plan(), [Wall()]);
        var result = motor.Step(0, Gravity);
        Assert.Equal(JumpMotorPhase.Reference, result.Phase);
        Assert.Equal(Body, result.Bounds);
        Assert.Empty(result.Contacts);
        Near(0, result.ElapsedSeconds);
    }

    [Fact]
    public void EarlyReleaseAndApexProfileRemainExactBeforeCollision()
    {
        var held = JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3,
            ApexRegionFraction: 0.1, TimeNearApexSeconds: 0.35));
        var flight = JumpFlightSolver.ForEarlyRelease(held,
            new JumpReleaseConstraints(1), 0.15);
        var horizontal = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        var plan = JumpReferenceCollisionPlan.Create(flight, horizontal,
            [new JumpTravelInputChange(0, HorizontalDirection.Right)]);
        var motor = new JumpKinematicMotor(Body, plan, []);
        double time = 0.15 + (flight.ApexTimeSinceTakeoffSeconds - 0.15) * 0.5;
        var result = motor.Step(time, Gravity);
        var expected = plan.Reference.Evaluate(time);
        Near(expected.VerticalDisplacement, result.Bounds.Bottom);
        Near(expected.VerticalVelocity, result.Velocity.Y);
        Near(expected.HorizontalPosition, result.Bounds.Left);
        Assert.Equal(JumpMotorPhase.Reference, result.Phase);
    }

    [Fact]
    public void ReferenceWithHorizontalReversalStillMatchesAbsoluteTime()
    {
        var flight = JumpFlightSolver.ForHeldJump(
            JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3)));
        var horizontal = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        var plan = JumpReferenceCollisionPlan.Create(flight, horizontal,
            [new JumpTravelInputChange(0, HorizontalDirection.Right),
             new JumpTravelInputChange(0.25, HorizontalDirection.Left)]);
        var motor = new JumpKinematicMotor(Body, plan, []);
        motor.Step(0.2, Gravity);
        var result = motor.Step(0.3, Gravity);
        var expected = plan.Reference.Evaluate(0.5);
        Near(expected.HorizontalPosition, result.Bounds.Left);
        Near(expected.HorizontalVelocity, result.Velocity.X);
    }

    [Fact]
    public void FollowupAccelerationCanChangeAfterReferenceEnds()
    {
        var motor = new JumpKinematicMotor(Body, Plan(), []);
        motor.Step(0.7, Gravity);
        var before = motor.Bounds;
        motor.Step(0.1, new Vector2D(10, 0));
        Near(before.Left + 1.05, motor.Bounds.Left);
        Near(0, motor.Bounds.Bottom + (motor.Velocity.Y * -0.1)); // displacement based on existing downward speed
    }

    [Fact]
    public void DuplicateColliderIdsAreRejectedDuringConstruction()
    {
        Assert.Throws<ArgumentException>(() => new JumpKinematicMotor(
            Body, Plan(), [Floor(1), Ceiling(1)]));
    }

    [Fact]
    public void StartingPenetrationRequiresExplicitResolution()
    {
        var overlapping = new StaticAabbCollider(1,
            new Aabb(new Vector2D(0.25, 0.25), new Vector2D(0.75, 0.75)));
        Assert.Throws<InvalidOperationException>(() => new JumpKinematicMotor(
            Body, Plan(), [overlapping]));
    }

    [Fact]
    public void InvalidStepDurationDoesNotMutateMotor()
    {
        var motor = new JumpKinematicMotor(Body, Plan(), []);
        Assert.Throws<ArgumentOutOfRangeException>(() => motor.Step(-1, Gravity));
        Assert.Throws<ArgumentOutOfRangeException>(() => motor.Step(double.NaN, Gravity));
        Assert.Equal(Body, motor.Bounds);
        Near(0, motor.ElapsedSeconds);
    }

    [Fact]
    public void InvalidConstructionOptionsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new JumpKinematicMotor(
            Body, Plan(), [], maxContactEvents: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new JumpKinematicMotor(
            Body, Plan(), [], contactAlignmentTolerance: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new JumpKinematicMotor(
            Body, Plan(), [], (CollisionResponseMode)123));
        Assert.Throws<ArgumentException>(() => new JumpKinematicMotor(
            default, Plan(), []));
    }

    [Fact]
    public void StepContactHistoryIsReadOnlyAndNotAccumulated()
    {
        var motor = new JumpKinematicMotor(Body, Plan(), [Wall()]);
        var first = motor.Step(0.3, new Vector2D(0, -20));
        Assert.Single(first.Contacts);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<JumpMotorContact>)first.Contacts).Add(default));
        var second = motor.Step(0.1, new Vector2D(0, -20));
        Assert.Empty(second.Contacts);
        Assert.Single(first.Contacts);
    }

    [Fact]
    public void FailedContinuationDoesNotCommitPartialMotorState()
    {
        // Ceiling first, then floor during the fallback segment. A one-event
        // budget must reject the incomplete step rather than advance the motor.
        var motor = new JumpKinematicMotor(Body, Plan(),
            [Ceiling(), Floor()], maxContactEvents: 1);
        var originalVelocity = motor.Velocity;
        Assert.Throws<InvalidOperationException>(() =>
            motor.Step(0.6, new Vector2D(0, -30)));
        Assert.Equal(Body, motor.Bounds);
        Assert.Equal(originalVelocity, motor.Velocity);
        Assert.Equal(JumpMotorPhase.Reference, motor.Phase);
        Near(0, motor.ElapsedSeconds);
    }

    [Fact]
    public void StableColliderIdsResolveSimultaneousContactIndependentOfListOrder()
    {
        var ceilingA = Ceiling(9);
        var ceilingB = Ceiling(2);
        var a = new JumpKinematicMotor(Body, Plan(), [ceilingA, ceilingB]);
        var b = new JumpKinematicMotor(Body, Plan(), [ceilingB, ceilingA]);
        var hitA = a.Step(0.2, Gravity);
        var hitB = b.Step(0.2, Gravity);
        Assert.Equal(2, Assert.Single(hitA.Contacts).ColliderId);
        Assert.Equal(2, Assert.Single(hitB.Contacts).ColliderId);
        Assert.Equal(hitA.Bounds, hitB.Bounds);
    }

    [Fact]
    public void ColliderCollectionIsSnapshotNotMutableExternalList()
    {
        var colliders = new List<StaticAabbCollider> { Wall() };
        var motor = new JumpKinematicMotor(Body, Plan(), colliders);
        colliders.Clear();
        var result = motor.Step(0.3, Gravity);
        Assert.Single(result.Contacts);
        Assert.Equal(7, result.Contacts[0].ColliderId);
    }
}
