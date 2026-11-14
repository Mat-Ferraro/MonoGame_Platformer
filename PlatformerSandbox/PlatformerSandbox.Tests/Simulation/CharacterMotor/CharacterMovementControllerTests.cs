using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class CharacterMovementControllerTests
{
    private static readonly Aabb Body = new(
        new Vector2D(0, 0), new Vector2D(1, 1));

    private static StaticAabbCollider Floor(int id = 1) => new(id,
        new Aabb(new Vector2D(-100, -1), new Vector2D(100, 0)));

    private static StaticAabbCollider Wall(int id = 2) => new(id,
        new Aabb(new Vector2D(2, 0), new Vector2D(2.2, 10)));

    private static CharacterMovementController Create(
        Aabb? body = null, StaticAabbCollider[]? obstacles = null,
        double gravity = -20.0)
    {
        var ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4, 0.4, 0.3)));
        return new CharacterMovementController(
            body ?? Body, obstacles ?? [Floor()], ground, air, jump, gravity);
    }

    private static void Near(double expected, double actual, double tolerance = 1e-7) =>
        Assert.InRange(Math.Abs(expected - actual), 0, tolerance);

    [Fact]
    public void InitiallySupportedBodyIsGroundedAndStationary()
    {
        var motor = Create();
        Assert.True(motor.IsGrounded);
        Assert.False(motor.IsJumping);
        Assert.Equal(Body, motor.Bounds);
        Assert.Equal(Vector2D.Zero, motor.Velocity);
        Assert.Equal(0, motor.JumpsStarted);
    }

    [Fact]
    public void InitiallyAirborneBodyIsNotGrounded()
    {
        var motor = Create(body: Body.Translated(new Vector2D(0, 3)));
        Assert.False(motor.IsGrounded);
    }

    [Fact]
    public void NeutralInputOnGroundDoesNotMove()
    {
        var motor = Create();
        var state = motor.Step(0.3, HorizontalDirection.Neutral, false);
        Assert.Equal(Body, state.Bounds);
        Assert.Equal(Vector2D.Zero, state.Velocity);
        Assert.True(state.IsGrounded);
    }

    [Fact]
    public void RightInputAcceleratesAnalytically()
    {
        var state = Create().Step(0.1, HorizontalDirection.Right, false);
        Near(0.25, state.Bounds.Left);
        Near(5.0, state.Velocity.X);
        Near(0.0, state.Bounds.Bottom);
    }

    [Fact]
    public void RunningReachesConfiguredSpeedThenCruises()
    {
        var state = Create().Step(0.5, HorizontalDirection.Right, false);
        Near(4.0, state.Bounds.Left);
        Near(10.0, state.Velocity.X);
    }

    [Fact]
    public void NeutralInputBrakesFromMaximumSpeed()
    {
        var motor = Create();
        motor.Step(0.2, HorizontalDirection.Right, false);
        var state = motor.Step(0.1, HorizontalDirection.Neutral, false);
        Near(1.5, state.Bounds.Left);
        Near(0.0, state.Velocity.X);
        Assert.True(state.IsGrounded);
    }

    [Fact]
    public void NeutralInputNeverReversesVelocity()
    {
        var motor = Create();
        motor.Step(0.2, HorizontalDirection.Right, false);
        var state = motor.Step(0.3, HorizontalDirection.Neutral, false);
        Near(0.0, state.Velocity.X);
    }

    [Fact]
    public void OppositeInputBrakesThenReverses()
    {
        var motor = Create();
        motor.Step(0.2, HorizontalDirection.Right, false);
        var state = motor.Step(0.35, HorizontalDirection.Left, false);
        Near(-10.0, state.Velocity.X);
        Near(0.75, state.Bounds.Left);
    }

    [Fact]
    public void GroundMovementIsConsistentAcrossIdenticalInputPartitions()
    {
        var a = Create();
        var b = Create();
        a.Step(0.2, HorizontalDirection.Right, false);
        b.Step(0.1, HorizontalDirection.Right, false);
        b.Step(0.1, HorizontalDirection.Right, false);
        Near(a.Bounds.Left, b.Bounds.Left);
        Near(a.Velocity.X, b.Velocity.X);
    }

    [Fact]
    public void WallBlocksRunningButPreservesGroundSupport()
    {
        var motor = Create(obstacles: [Floor(), Wall()]);
        var state = motor.Step(0.5, HorizontalDirection.Right, false);
        Near(2.0, state.Bounds.Right);
        Near(0.0, state.Velocity.X);
        Assert.True(state.IsGrounded);
        Assert.Contains(state.Contacts, c => c.ColliderId == 2 && c.Normal == new Vector2D(-1, 0));
    }

    [Fact]
    public void ContinuedInputIntoWallCannotPassThrough()
    {
        var motor = Create(obstacles: [Floor(), Wall()]);
        motor.Step(0.4, HorizontalDirection.Right, false);
        var state = motor.Step(0.2, HorizontalDirection.Right, false);
        Near(2.0, state.Bounds.Right);
    }

    [Fact]
    public void GroundedJumpRequestBeginsReferenceMotion()
    {
        var motor = Create();
        var state = motor.Step(0.1, HorizontalDirection.Neutral, true);
        Assert.True(state.JumpStarted);
        Assert.True(state.IsJumping);
        Assert.False(state.IsGrounded);
        Assert.Equal(1, state.JumpsStarted);
        Assert.True(state.Velocity.Y > 0.0);
        Assert.True(state.Bounds.Bottom > 0.0);
    }

    [Fact]
    public void InAirJumpRequestsAreIgnored()
    {
        var motor = Create();
        motor.Step(0.1, HorizontalDirection.Neutral, true);
        var state = motor.Step(0.1, HorizontalDirection.Neutral, true);
        Assert.False(state.JumpStarted);
        Assert.Equal(1, state.JumpsStarted);
    }

    [Fact]
    public void RepeatedJumpsCanStartAfterLanding()
    {
        var motor = Create();
        var landed = motor.Step(0.8, HorizontalDirection.Neutral, true);
        Assert.True(landed.IsGrounded);
        Assert.False(landed.IsJumping);
        Assert.Equal(1, landed.JumpsStarted);
        var second = motor.Step(0.05, HorizontalDirection.Neutral, true);
        Assert.True(second.JumpStarted);
        Assert.True(second.IsJumping);
        Assert.Equal(2, second.JumpsStarted);
        Assert.True(second.Bounds.Bottom > 0);
    }

    [Fact]
    public void RunningVelocityIsInheritedAtTakeoff()
    {
        var motor = Create();
        motor.Step(0.2, HorizontalDirection.Right, false);
        var state = motor.Step(0.01, HorizontalDirection.Right, true);
        Assert.True(state.JumpStarted);
        Near(10, state.Velocity.X);
        Assert.True(state.Bounds.Left > 1.0);
    }

    [Fact]
    public void ReferenceJumpAppliesAirProfileFromRest()
    {
        var state = Create().Step(0.1, HorizontalDirection.Right, true);
        Near(5.0, state.Velocity.X);
        Near(0.25, state.Bounds.Left);
    }

    [Fact]
    public void AirDirectionChangesBrakeAndEventuallyReverse()
    {
        var motor = Create();
        var first = motor.Step(0.1, HorizontalDirection.Right, true);
        Near(5.0, first.Velocity.X);

        // +5 units/s at a reversal braking rate of 66.666... units/s²
        // takes 0.075 seconds to reach zero. Sample *before* that boundary
        // to verify braking without an instantaneous direction flip.
        var braking = motor.Step(0.05, HorizontalDirection.Left, false);
        Assert.True(braking.Velocity.X > 0.0);
        Assert.True(braking.Velocity.X < first.Velocity.X);
        Near(5.0 / 3.0, braking.Velocity.X);

        // Continue holding left: 0.025 s to reach zero, followed by
        // 0.175 s of leftward acceleration at 50 units/s².
        var reversed = motor.Step(0.2, HorizontalDirection.Left, false);
        Assert.True(reversed.IsJumping);
        Assert.True(reversed.Velocity.X < 0.0);
        Near(-8.75, reversed.Velocity.X);
    }

    [Fact]
    public void InitialAirborneBodyFallsWithGravity()
    {
        var start = Body.Translated(new Vector2D(0, 3));
        var state = Create(body: start).Step(0.1, HorizontalDirection.Neutral, false);
        Near(2.9, state.Bounds.Bottom);
        Near(-2, state.Velocity.Y);
        Assert.False(state.IsGrounded);
    }

    [Fact]
    public void InitialAirborneBodyCanLandWithoutJumping()
    {
        var start = Body.Translated(new Vector2D(0, 3));
        var state = Create(body: start).Step(0.6, HorizontalDirection.Neutral, false);
        Near(0, state.Bounds.Bottom);
        Near(0, state.Velocity.Y);
        Assert.True(state.IsGrounded);
        Assert.False(state.IsJumping);
        Assert.Contains(state.Contacts, c => c.ColliderId == 1 && c.Normal == new Vector2D(0, 1));
    }

    [Fact]
    public void AirborneJumpRequestDoesNotStartNewJump()
    {
        var start = Body.Translated(new Vector2D(0, 3));
        var state = Create(body: start).Step(0.1, HorizontalDirection.Neutral, true);
        Assert.False(state.JumpStarted);
        Assert.Equal(0, state.JumpsStarted);
    }

    [Fact]
    public void JumpMayStartAfterOrdinaryFallingLanding()
    {
        var start = Body.Translated(new Vector2D(0, 3));
        var motor = Create(body: start);
        motor.Step(0.6, HorizontalDirection.Neutral, false);
        var state = motor.Step(0.05, HorizontalDirection.Neutral, true);
        Assert.True(state.JumpStarted);
        Assert.True(state.IsJumping);
    }

    [Fact]
    public void LeavingAPlatformLosesGroundSupport()
    {
        var shortPlatform = new StaticAabbCollider(8,
            new Aabb(new Vector2D(-1, -1), new Vector2D(1.5, 0)));
        var motor = Create(obstacles: [shortPlatform]);
        var state = motor.Step(0.3, HorizontalDirection.Right, false);
        Assert.False(state.IsGrounded);
        Assert.True(state.Bounds.Left > 1.5);
    }

    [Fact]
    public void GravityBeginsAtTheExactSupportLossWithinTheSameStep()
    {
        var shortPlatform = new StaticAabbCollider(8,
            new Aabb(new Vector2D(-1, -1), new Vector2D(1.5, 0)));
        var motor = Create(obstacles: [shortPlatform]);
        // Ground acceleration ends at t=0.20 (x=1.0, vx=10).
        // The body leaves support at t=0.25 (left edge reaches x=1.5).
        // Gravity therefore acts for 0.05 seconds within this 0.30-s step.
        var first = motor.Step(0.3, HorizontalDirection.Right, false);
        Assert.False(first.IsGrounded);
        Near(2.0, first.Bounds.Left);
        Near(-0.025, first.Bounds.Bottom);
        Near(-1.0, first.Velocity.Y);

        var second = motor.Step(0.1, HorizontalDirection.Neutral, false);
        Assert.False(second.IsGrounded);
        Near(-0.225, second.Bounds.Bottom);
        Near(-3.0, second.Velocity.Y);
    }

    [Fact]
    public void ZeroDurationIsPureSnapshotEvenWithJumpRequest()
    {
        var motor = Create();
        var state = motor.Step(0, HorizontalDirection.Right, true);
        Assert.Equal(Body, state.Bounds);
        Assert.False(state.JumpStarted);
        Assert.False(state.IsJumping);
        Assert.Equal(0, state.JumpsStarted);
        Near(0, state.ElapsedSeconds);
    }

    [Fact]
    public void ClockAdvancesAcrossSeparateUpdates()
    {
        var motor = Create();
        motor.Step(0.1, HorizontalDirection.Neutral, false);
        var state = motor.Step(0.2, HorizontalDirection.Neutral, false);
        Near(0.3, state.ElapsedSeconds);
        Near(0.2, state.DurationSeconds);
        Near(0.3, motor.ElapsedSeconds);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidStepDurationDoesNotMutateState(double duration)
    {
        var motor = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            motor.Step(duration, HorizontalDirection.Right, true));
        Assert.Equal(Body, motor.Bounds);
        Assert.Equal(0, motor.JumpsStarted);
    }

    [Fact]
    public void UnknownDirectionIsRejectedWithoutMutation()
    {
        var motor = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            motor.Step(0.1, (HorizontalDirection)99, true));
        Assert.Equal(Body, motor.Bounds);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(2.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidGravityIsRejected(double gravity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(gravity: gravity));
    }

    [Fact]
    public void DuplicateColliderIdsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Create(obstacles: [Floor(4), Wall(4)]));
    }

    [Fact]
    public void InitialPenetrationIsRejected()
    {
        var overlap = new StaticAabbCollider(4,
            new Aabb(new Vector2D(-1, -1), new Vector2D(3, 0.25)));
        Assert.Throws<InvalidOperationException>(() => Create(obstacles: [overlap]));
    }

    [Fact]
    public void ColliderArrayChangesAfterConstructionDoNotChangeWorldSnapshot()
    {
        StaticAabbCollider[] obstacles = [Floor()];
        var motor = Create(obstacles: obstacles);
        obstacles[0] = Wall();
        var state = motor.Step(0.1, HorizontalDirection.Neutral, false);
        Assert.True(state.IsGrounded);
    }

    [Fact]
    public void GroundedContactResultIsAnImmutableSnapshot()
    {
        var motor = Create(obstacles: [Floor(), Wall()]);
        var state = motor.Step(0.5, HorizontalDirection.Right, false);
        Assert.NotEmpty(state.Contacts);
        var original = state.Contacts.ToArray();
        motor.Step(0.1, HorizontalDirection.Neutral, false);
        Assert.Equal(original, state.Contacts.ToArray());
    }
}
