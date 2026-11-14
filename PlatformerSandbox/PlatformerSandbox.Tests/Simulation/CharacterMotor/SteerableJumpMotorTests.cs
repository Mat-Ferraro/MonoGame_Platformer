using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class SteerableJumpMotorTests
{
    private static readonly Aabb Body = new(new Vector2D(0, 0), new Vector2D(1, 1.6));
    private const double StepTime = 1.0 / 60.0;

    private static StaticAabbCollider Floor() => new(1,
        new Aabb(new Vector2D(-100, -1), new Vector2D(100, 0)));
    private static StaticAabbCollider Wall() => new(2,
        new Aabb(new Vector2D(1, 0), new Vector2D(1.5, 10)));
    private static StaticAabbCollider Ceiling() => new(3,
        new Aabb(new Vector2D(-5, 2.4), new Vector2D(5, 2.8)));

    private static CharacterMovementController Create(StaticAabbCollider[]? obstacles = null)
    {
        var ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9, 0.18, 0.13, 0.38));
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(9, 0.22, 0.16, 0.43));
        var flight = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        return new CharacterMovementController(
            Body, obstacles ?? [Floor()], ground, air, flight, -24);
    }

    private static void Near(double expected, double actual, double tolerance = 1e-7) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, tolerance);

    [Fact]
    public void RightInputAppliedAfterTakeoffSteersInAir()
    {
        var motor = Create();
        motor.Step(0.10, HorizontalDirection.Neutral, true);
        Near(0.0, motor.Velocity.X);
        var steered = motor.Step(0.10, HorizontalDirection.Right, false);
        Assert.True(steered.IsJumping);
        Assert.True(steered.Bounds.Left > 0.0);
        Assert.True(steered.Velocity.X > 0.0);
    }

    [Fact]
    public void AirborneOppositeInputEventuallyReversesHorizontalVelocity()
    {
        var controlled = Create();
        var holdingRight = Create();
        controlled.Step(0.10, HorizontalDirection.Right, true);
        holdingRight.Step(0.10, HorizontalDirection.Right, true);
        controlled.Step(0.30, HorizontalDirection.Left, false);
        holdingRight.Step(0.30, HorizontalDirection.Right, false);
        Assert.True(controlled.IsJumping);
        Assert.True(controlled.Velocity.X < 0.0);
        Assert.True(controlled.Bounds.Left < holdingRight.Bounds.Left);
        Near(holdingRight.Bounds.Bottom, controlled.Bounds.Bottom);
    }

    [Fact]
    public void NeutralInputInAirBrakesHorizontalVelocity()
    {
        var motor = Create();
        motor.Step(0.12, HorizontalDirection.Right, true);
        Assert.True(motor.Velocity.X > 0);
        motor.Step(0.20, HorizontalDirection.Neutral, false);
        Near(0.0, motor.Velocity.X);
        Assert.True(motor.IsJumping);
    }

    [Fact]
    public void WallAtTakeoffCannotBoostApexHeight()
    {
        var motor = Create([Floor(), Wall()]);
        double highest = 0.0;
        bool wallContactObserved = false;
        for (int i = 0; i < 40; i++)
        {
            var step = motor.Step(StepTime, HorizontalDirection.Right, i == 0);
            highest = Math.Max(highest, step.Bounds.Bottom);
            wallContactObserved |= step.Contacts.Any(c =>
                c.ColliderId == 2 && c.Normal == new Vector2D(-1, 0));
            Near(1.0, step.Bounds.Right);
        }
        Assert.True(wallContactObserved);
        Assert.InRange(highest, 3.6, 3.7000001);
    }

    [Fact]
    public void WallContactPreservesEntireVerticalReference()
    {
        var byWall = Create([Floor(), Wall()]);
        var unobstructed = Create();
        for (int i = 0; i < 30; i++)
        {
            byWall.Step(StepTime, HorizontalDirection.Right, i == 0);
            unobstructed.Step(StepTime, HorizontalDirection.Right, i == 0);
            Near(unobstructed.Bounds.Bottom, byWall.Bounds.Bottom, 1e-8);
            Near(unobstructed.Velocity.Y, byWall.Velocity.Y, 1e-8);
        }
    }

    [Fact]
    public void SideImpactChangesOnlyHorizontalVelocity()
    {
        var motor = Create([Floor(), Wall()]);
        var result = motor.Step(StepTime, HorizontalDirection.Right, true);
        var wall = Assert.Single(result.Contacts.Where(c => c.ColliderId == 2));
        Near(wall.IncomingVelocity.Y, wall.OutgoingVelocity.Y);
        Near(0.0, wall.OutgoingVelocity.X);
        Assert.True(result.Velocity.Y > 0.0);
    }

    [Fact]
    public void MovingAwayFromWallWorksDuringJump()
    {
        var motor = Create([Floor(), Wall()]);
        for (int i = 0; i < 7; i++)
            motor.Step(StepTime, HorizontalDirection.Right, i == 0);
        var result = motor.Step(0.10, HorizontalDirection.Left, false);
        Assert.True(result.IsJumping);
        Assert.True(result.Velocity.X < 0.0);
        Assert.True(result.Bounds.Left < 0.0);
    }

    [Fact]
    public void CeilingContactInterruptsVerticalProfileAndBeginsFall()
    {
        var motor = Create([Floor(), Ceiling()]);
        bool hitCeiling = false;
        for (int i = 0; i < 22; i++)
        {
            var step = motor.Step(StepTime, HorizontalDirection.Neutral, i == 0);
            hitCeiling |= step.Contacts.Any(c => c.ColliderId == 3 &&
                c.Normal == new Vector2D(0, -1));
            Assert.True(step.Bounds.Top <= 2.4000001);
        }
        Assert.True(hitCeiling);
        Assert.True(motor.Velocity.Y <= 0.0);
    }

    [Fact]
    public void UnobstructedJumpStillLandsAndCanJumpAgain()
    {
        var motor = Create();
        motor.Step(0.8, HorizontalDirection.Neutral, true);
        Assert.True(motor.IsGrounded);
        Assert.False(motor.IsJumping);
        var next = motor.Step(0.02, HorizontalDirection.Neutral, true);
        Assert.True(next.JumpStarted);
        Assert.True(next.IsJumping);
        Assert.Equal(2, next.JumpsStarted);
    }

    [Fact]
    public void NoInputChangePreservesReferenceAcrossUpdatePartitions()
    {
        var longStep = Create();
        var manySteps = Create();
        longStep.Step(0.31, HorizontalDirection.Right, true);
        for (int i = 0; i < 31; i++)
            manySteps.Step(0.01, HorizontalDirection.Right, i == 0);
        Near(longStep.Bounds.Left, manySteps.Bounds.Left, 1e-7);
        Near(longStep.Bounds.Bottom, manySteps.Bounds.Bottom, 1e-7);
        Near(longStep.Velocity.X, manySteps.Velocity.X, 1e-7);
        Near(longStep.Velocity.Y, manySteps.Velocity.Y, 1e-7);
    }

    [Fact]
    public void InvalidInputDoesNotModifyAuthoritativeState()
    {
        var motor = Create();
        motor.Step(0.02, HorizontalDirection.Neutral, true);
        Aabb before = motor.Bounds;
        Vector2D velocity = motor.Velocity;
        double time = motor.ElapsedSeconds;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            motor.Step(0.02, (HorizontalDirection)500, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            motor.Step(double.NaN, HorizontalDirection.Right, false));
        Assert.Equal(before, motor.Bounds);
        Assert.Equal(velocity, motor.Velocity);
        Assert.Equal(time, motor.ElapsedSeconds);
    }
}
