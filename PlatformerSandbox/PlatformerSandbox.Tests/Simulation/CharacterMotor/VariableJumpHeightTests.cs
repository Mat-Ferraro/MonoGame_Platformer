using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class VariableJumpHeightTests
{
    private static readonly Aabb Body = new(
        new Vector2D(0, 0), new Vector2D(1, 1));

    private static HorizontalMotionProfile Air() => HorizontalMotionSolver.Solve(
        new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));

    private static HorizontalMotionProfile Ground() => HorizontalMotionSolver.Solve(
        new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));

    private static JumpTrajectory Original() => JumpConstraintSolver.Solve(
        new JumpConstraints(4, 0.4, 0.3));

    private static JumpFlightTrajectory Held() => JumpFlightSolver.ForHeldJump(Original());

    private static SteerableJumpMotor JumpMotor(
        IReadOnlyList<StaticAabbCollider>? obstacles = null,
        JumpReleaseConstraints? release = null) => new(
            Body, 0, Air(), Held(), obstacles ?? [], -20, 8,
            release ?? new JumpReleaseConstraints(1.0));

    private static StaticAabbCollider Floor() => new(1,
        new Aabb(new Vector2D(-100, -1), new Vector2D(100, 0)));

    private static CharacterMovementController Controller(
        JumpReleaseConstraints? release = null) => new(
            Body, [Floor()], Ground(), Air(), Held(), -20,
            releaseConstraints: release ?? new JumpReleaseConstraints(1.0));

    private static void Near(double expected, double actual, double tolerance = 1e-7) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, tolerance);

    [Fact]
    public void HoldingJumpFollowsOriginalReferenceWithoutCut()
    {
        var jump = JumpMotor();
        jump.Step(0.4, HorizontalDirection.Neutral);
        Assert.False(jump.IsEarlyReleased);
        Near(4.0, jump.Bounds.Bottom);
        Near(0.0, jump.Velocity.Y);
    }

    [Fact]
    public void ReleasingDuringAscentProducesExactSolvedCutApex()
    {
        var jump = JumpMotor();
        jump.Step(0.1, HorizontalDirection.Neutral);
        JumpFlightTrajectory cut = JumpFlightSolver.ForEarlyRelease(
            Original(), new JumpReleaseConstraints(1.0), 0.1);

        jump.Step(cut.ApexTimeSinceTakeoffSeconds - 0.1,
            HorizontalDirection.Neutral, jumpReleasedThisStep: true);
        Assert.True(jump.IsEarlyReleased);
        Near(cut.PeakHeight, jump.Bounds.Bottom);
        Near(0.0, jump.Velocity.Y);
        Assert.InRange(cut.PeakHeight, 1.0, 4.0);
        Assert.True(cut.PeakHeight < 4.0);
    }

    [Fact]
    public void ReleasingDoesNotInstantlyChangePositionOrVelocity()
    {
        var jump = JumpMotor();
        jump.Step(0.1, HorizontalDirection.Neutral);
        double y = jump.Bounds.Bottom;
        double vy = jump.Velocity.Y;
        const double tiny = 1e-7;
        jump.Step(tiny, HorizontalDirection.Neutral, jumpReleasedThisStep: true);
        Assert.True(jump.IsEarlyReleased);
        Assert.InRange(Math.Abs(jump.Bounds.Bottom - y), 0.0, Math.Abs(vy) * tiny + 1e-8);
        Assert.True(jump.Velocity.Y < vy);
        Assert.True(jump.Velocity.Y > 0.0);
    }

    [Fact]
    public void ReleasingAtOrAfterTheOriginalApexDoesNotCutJump()
    {
        var jump = JumpMotor();
        jump.Step(0.4, HorizontalDirection.Neutral);
        jump.Step(0.05, HorizontalDirection.Neutral, jumpReleasedThisStep: true);
        Assert.False(jump.IsEarlyReleased);
        Near(Held().Evaluate(0.45).VerticalDisplacement, jump.Bounds.Bottom);
    }

    [Fact]
    public void ReleasingTwiceNeverRestartsCutAscent()
    {
        var one = JumpMotor();
        var two = JumpMotor();
        one.Step(0.1, HorizontalDirection.Neutral);
        two.Step(0.1, HorizontalDirection.Neutral);
        one.Step(0.02, HorizontalDirection.Neutral, jumpReleasedThisStep: true);
        two.Step(0.02, HorizontalDirection.Neutral, jumpReleasedThisStep: true);
        one.Step(0.04, HorizontalDirection.Neutral, jumpReleasedThisStep: true);
        two.Step(0.04, HorizontalDirection.Neutral);
        Assert.True(one.IsEarlyReleased);
        Assert.Equal(two.Bounds, one.Bounds);
        Assert.Equal(two.Velocity, one.Velocity);
    }

    [Fact]
    public void ReleaseWithoutConfiguredConstraintLeavesHeldReferenceUntouched()
    {
        var jump = new SteerableJumpMotor(
            Body, 0, Air(), Held(), [], -20);
        jump.Step(0.1, HorizontalDirection.Neutral);
        jump.Step(0.1, HorizontalDirection.Neutral, jumpReleasedThisStep: true);
        Assert.False(jump.IsEarlyReleased);
        Near(Held().Evaluate(0.2).VerticalDisplacement, jump.Bounds.Bottom);
    }

    [Fact]
    public void AirSteeringStillChangesHorizontalDirectionAfterRelease()
    {
        var jump = JumpMotor();
        jump.Step(0.1, HorizontalDirection.Right);
        jump.Step(0.02, HorizontalDirection.Left, jumpReleasedThisStep: true);
        Assert.True(jump.IsEarlyReleased);
        double prior = jump.Velocity.X;
        jump.Step(0.02, HorizontalDirection.Left);
        Assert.True(jump.Velocity.X < prior);
    }

    [Fact]
    public void TouchingSideWallDoesNotAddHeightToCutJump()
    {
        var wall = new StaticAabbCollider(8,
            new Aabb(new Vector2D(1, 0), new Vector2D(1.2, 10)));
        var touching = JumpMotor([wall]);
        var free = JumpMotor();
        touching.Step(0.1, HorizontalDirection.Right);
        free.Step(0.1, HorizontalDirection.Right);
        touching.Step(0.04, HorizontalDirection.Right, jumpReleasedThisStep: true);
        free.Step(0.04, HorizontalDirection.Right, jumpReleasedThisStep: true);
        Assert.True(touching.IsEarlyReleased);
        Near(free.Bounds.Bottom, touching.Bounds.Bottom);
        Near(free.Velocity.Y, touching.Velocity.Y);
    }

    [Fact]
    public void AZeroDurationReleaseDoesNotModifyMotionState()
    {
        var jump = JumpMotor();
        jump.Step(0.1, HorizontalDirection.Neutral);
        var y = jump.Bounds.Bottom;
        jump.Step(0, HorizontalDirection.Neutral, jumpReleasedThisStep: true);
        Assert.False(jump.IsEarlyReleased);
        Near(y, jump.Bounds.Bottom);
    }

    [Fact]
    public void ControllerForwardsReleaseAndCanLandNormally()
    {
        var controller = Controller();
        controller.Step(0.1, HorizontalDirection.Neutral, true);
        controller.Step(0.05, HorizontalDirection.Neutral, false, jumpReleasedThisStep: true);
        Assert.True(controller.IsJumpEarlyReleased);
        controller.Step(0.9, HorizontalDirection.Neutral, false);
        Assert.True(controller.IsGrounded);
        Assert.False(controller.IsJumping);
    }

    [Fact]
    public void ControllerCanBeginAnotherFullJumpAfterCutLanding()
    {
        var controller = Controller();
        controller.Step(0.1, HorizontalDirection.Neutral, true);
        controller.Step(0.8, HorizontalDirection.Neutral, false, jumpReleasedThisStep: true);
        Assert.True(controller.IsGrounded);
        var next = controller.Step(0.03, HorizontalDirection.Neutral, true);
        Assert.True(next.JumpStarted);
        Assert.False(controller.IsJumpEarlyReleased);
        Assert.Equal(2, controller.JumpsStarted);
    }

    [Fact]
    public void InvalidDesignerMinimumHeightIsRejectedAtConstruction()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CharacterMovementController(
            Body, [Floor()], Ground(), Air(), Held(), -20,
            releaseConstraints: new JumpReleaseConstraints(4.0)));
    }
}
