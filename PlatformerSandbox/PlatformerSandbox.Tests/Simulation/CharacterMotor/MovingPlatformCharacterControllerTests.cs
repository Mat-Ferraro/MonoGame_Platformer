using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

/// <summary>Runtime integration, not just isolated moving-platform math.</summary>
public sealed class MovingPlatformCharacterControllerTests
{
    private static Aabb Box(double x, double y, double width = 0.8, double height = 1.6) =>
        new(new Vector2D(x, y), new Vector2D(x + width, y + height));

    private static MovingPlatformTrajectory Platform(double travel = 2.0,
        double oneWay = 1.0) => new(7, Box(-1.0, 0.0, 4.0, 0.25),
            new Vector2D(travel, 0.0), oneWay);

    private static CharacterMovementController Motor(Aabb body,
        MovingPlatformTrajectory? platform = null,
        StaticAabbCollider[]? staticObstacles = null)
    {
        var ground = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        var air = HorizontalMotionSolver.Solve(
            new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(4, 0.4, 0.3)));
        return new CharacterMovementController(body,
            staticObstacles ?? Array.Empty<StaticAabbCollider>(),
            ground, air, jump, -20.0, movingPlatform: platform);
    }

    private static void Near(double expected, double actual, double tolerance = 1e-7) =>
        Assert.InRange(Math.Abs(expected - actual), 0, tolerance);

    [Fact]
    public void AlreadySupportedRiderIsGroundedAndInheritsCurrentPlatformSpeed()
    {
        var motor = Motor(Box(0, 0.25), Platform());
        Assert.True(motor.IsGrounded);
        Assert.Equal(7, motor.MovingSupportId);
        Near(2.0, motor.Velocity.X);
    }

    [Fact]
    public void NeutralInputCarriesBodyWithoutChangingRelativeOffset()
    {
        var motor = Motor(Box(0, 0.25), Platform());
        motor.Step(0.25, HorizontalDirection.Neutral, false);
        Near(0.5, motor.Bounds.Left);
        Near(0.25, motor.Bounds.Bottom);
        Near(2.0, motor.Velocity.X);
        Assert.Equal(7, motor.MovingSupportId);
    }

    [Fact]
    public void RiderFollowsPlatformThroughReversal()
    {
        var motor = Motor(Box(0, 0.25), Platform());
        motor.Step(0.9, HorizontalDirection.Neutral, false);
        Near(1.8, motor.Bounds.Left);
        motor.Step(0.2, HorizontalDirection.Neutral, false);
        Near(1.8, motor.Bounds.Left);
        Near(-2.0, motor.Velocity.X);
        Assert.True(motor.IsGrounded);
    }

    [Fact]
    public void JumpingFromMovingFloorPreservesItsHorizontalVelocityOnce()
    {
        var motor = Motor(Box(0, 0.25), Platform());
        var result = motor.Step(0.01, HorizontalDirection.Neutral, true);
        Assert.True(result.JumpStarted);
        Assert.False(result.IsGrounded);
        Assert.Null(motor.MovingSupportId);
        Assert.True(result.Velocity.X > 0.0);
        Assert.True(result.Velocity.X < 2.0);
        Assert.True(result.Velocity.Y > 0.0);
    }

    [Fact]
    public void ABodyFallingOntoMovingFloorBecomesItsSupportedRider()
    {
        var motor = Motor(Box(0, 1.50), Platform());
        var result = motor.Step(0.40, HorizontalDirection.Neutral, false);
        Assert.True(result.IsGrounded);
        Assert.False(result.IsJumping);
        Assert.Equal(7, motor.MovingSupportId);
        Near(0.25, result.Bounds.Bottom);
        Assert.Contains(result.Contacts,
            c => c.ColliderId == 7 && c.Normal == new Vector2D(0, 1));
    }

    [Fact]
    public void LandedRiderKeepsMovingOnFollowingUpdate()
    {
        var motor = Motor(Box(0, 1.50), Platform());
        motor.Step(0.4, HorizontalDirection.Neutral, false);
        double x = motor.Bounds.Left;
        motor.Step(0.10, HorizontalDirection.Neutral, false);
        Near(x + 0.2, motor.Bounds.Left);
        Assert.Equal(7, motor.MovingSupportId);
    }

    [Fact]
    public void WalkingOffPlatformUsesAirborneState()
    {
        var platform = Platform();
        // Start near the right edge with outward world/relative motion.
        var motor = Motor(Box(2.10, 0.25), platform);
        motor.Step(0.35, HorizontalDirection.Right, false);
        Assert.Null(motor.MovingSupportId);
        Assert.False(motor.IsGrounded);
        Assert.True(motor.Bounds.Bottom < 0.25);
    }

    [Fact]
    public void StableColliderIdsMustNotOverlapBetweenStaticAndMoving()
    {
        var floor = new StaticAabbCollider(7, Box(-5, -2, 10, 1));
        Assert.Throws<ArgumentException>(() =>
            Motor(Box(0, 1.0), Platform(), [floor]));
    }

    [Fact]
    public void VerticalMovingPlatformNowCarriesSupportedRider()
    {
        var rising = new MovingPlatformTrajectory(7,
            Box(-1, 0, 4, 0.25), new Vector2D(0, 2), 1);
        var motor = Motor(Box(0, 0.25), rising);
        Assert.Equal(7, motor.MovingSupportId);
        Near(2.0, motor.Velocity.Y);
        motor.Step(0.2, HorizontalDirection.Neutral, false);
        Near(0.65, motor.Bounds.Bottom);
        Near(2.0, motor.Velocity.Y);
        Assert.True(motor.IsGrounded);
    }

    [Fact]
    public void WithoutMovingPlatformOriginalControllerBehaviorIsUnchanged()
    {
        var floor = new StaticAabbCollider(1, Box(-5, -1, 10, 1));
        var motor = Motor(Box(0, 0), staticObstacles: [floor]);
        Assert.True(motor.IsGrounded);
        Assert.Null(motor.MovingSupportId);
        var state = motor.Step(0.05, HorizontalDirection.Neutral, true);
        Assert.True(state.JumpStarted);
    }
}
