using PlatformerSandbox.Gameplay.Input.Buffering;

namespace PlatformerSandbox.Tests.Gameplay.Input;

public sealed class BufferedJumpInputTests
{
    private static BufferedJumpInput Input(double coyote = 0.1, double buffer = 0.1) =>
        new(new JumpTimingConstraints(coyote, buffer));

    [Theory]
    [InlineData(-1.0, 0.1)]
    [InlineData(0.1, -0.1)]
    [InlineData(double.NaN, 0.1)]
    [InlineData(double.PositiveInfinity, 0.1)]
    [InlineData(0.1, double.NegativeInfinity)]
    public void InvalidTimingSettingsAreRejected(double coyote, double buffer)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Input(coyote, buffer));
    }

    [Fact]
    public void PressOnGroundStartsJumpImmediately()
    {
        var buffer = Input();
        JumpInputPlan plan = buffer.Plan(0, true, false, true, false);
        Assert.True(plan.StartJump);
        Assert.False(plan.AuthorizeCoyote);
        Assert.False(plan.ReleaseOnStart);
        buffer.Commit(plan, 0.01, true, false, jumpStarted: true);
        Assert.False(buffer.HasPendingPress);
    }

    [Fact]
    public void HoldingJumpDoesNotGenerateAnotherPress()
    {
        var buffer = Input();
        var first = buffer.Plan(0, true, false, true, false);
        buffer.Commit(first, 0.01, true, false, true);
        var next = buffer.Plan(0.01, false, true, false, false);
        Assert.False(next.StartJump);
        Assert.True(next.ButtonHeld);
    }

    [Fact]
    public void AirbornePressIsBufferedUntilLandingThenConsumedOnce()
    {
        var buffer = Input();
        var press = buffer.Plan(0.5, false, true, true, false);
        Assert.False(press.StartJump);
        buffer.Commit(press, 0.55, false, true, false);
        Assert.True(buffer.HasPendingPress);
        var next = buffer.Plan(0.55, true, false, false, false);
        Assert.True(next.StartJump);
        buffer.Commit(next, 0.56, true, false, true);
        Assert.False(buffer.HasPendingPress);
        Assert.False(buffer.Plan(0.56, false, true, false, false).StartJump);
    }

    [Fact]
    public void ExpiredPressCannotTriggerLateLandingJump()
    {
        var buffer = Input(buffer: 0.1);
        var press = buffer.Plan(0.0, false, true, true, false);
        buffer.Commit(press, 0.2, false, true, false);
        var late = buffer.Plan(0.2, true, false, false, false);
        Assert.False(late.StartJump);
        buffer.Commit(late, 0.21, true, true, false);
        Assert.False(buffer.HasPendingPress);
    }

    [Fact]
    public void CoyotePressImmediatelyAfterWalkOffIsAuthorized()
    {
        var buffer = Input();
        var walkOff = buffer.Plan(0, true, false, false, false);
        buffer.Commit(walkOff, 0.04, true, false, false);
        var press = buffer.Plan(0.04, false, false, true, false);
        Assert.True(press.StartJump);
        Assert.True(press.AuthorizeCoyote);
        buffer.Commit(press, 0.05, false, false, true);
        Assert.False(buffer.Plan(0.06, false, true, false, false).StartJump);
    }

    [Fact]
    public void CoyoteExpiresAfterItsConfiguredWindow()
    {
        var buffer = Input(coyote: 0.1);
        var walkOff = buffer.Plan(0, true, false, false, false);
        buffer.Commit(walkOff, 0.02, true, false, false);
        var late = buffer.Plan(0.121, false, false, true, false);
        Assert.False(late.StartJump);
        Assert.False(late.AuthorizeCoyote);
    }

    [Fact]
    public void BothDeadlinesAreInclusiveAtExactBoundary()
    {
        var buffer = Input(coyote: 0.1, buffer: 0.1);
        var walkOff = buffer.Plan(0, true, false, false, false);
        buffer.Commit(walkOff, 0.0, true, false, false);
        var press = buffer.Plan(0.1, false, false, true, false);
        Assert.True(press.AuthorizeCoyote);

        var second = Input(buffer: 0.1);
        var airborne = second.Plan(0, false, true, true, false);
        second.Commit(airborne, 0.1, false, true, false);
        Assert.True(second.Plan(0.1, true, false, false, false).StartJump);
    }

    [Fact]
    public void ZeroWindowsDisableAssistanceButNotNormalGroundedJump()
    {
        var buffer = Input(coyote: 0, buffer: 0);
        var left = buffer.Plan(0, true, false, false, false);
        buffer.Commit(left, 0.01, true, false, false);
        Assert.False(buffer.Plan(0.01, false, false, true, false).AuthorizeCoyote);
        Assert.True(buffer.Plan(0.01, true, false, true, false).StartJump);
    }

    [Fact]
    public void ReleasingBufferedPressBeforeLandingCutsAtTakeoff()
    {
        var buffer = Input();
        var press = buffer.Plan(0.0, false, true, true, false);
        buffer.Commit(press, 0.02, false, false, false);
        var release = buffer.Plan(0.02, false, false, false, true);
        Assert.False(release.StartJump);
        buffer.Commit(release, 0.03, false, true, false);
        var land = buffer.Plan(0.03, true, false, false, false);
        Assert.True(land.StartJump);
        Assert.True(land.ReleaseOnStart);
    }

    [Fact]
    public void ActiveJumpNeverAcceptsSecondJumpImmediately()
    {
        var buffer = Input();
        var press = buffer.Plan(0.1, false, true, true, false);
        Assert.False(press.StartJump);
        buffer.Commit(press, 0.11, false, false, false);
        Assert.False(buffer.Plan(0.11, false, true, false, false).StartJump);
    }

    [Fact]
    public void ACommittedJumpConsumesCoyoteAndDoesNotAllowAnother()
    {
        var buffer = Input();
        var left = buffer.Plan(0, true, false, false, false);
        buffer.Commit(left, 0.01, true, false, false);
        var press = buffer.Plan(0.01, false, false, true, false);
        buffer.Commit(press, 0.02, false, false, true);
        var second = buffer.Plan(0.03, false, false, true, false);
        Assert.False(second.StartJump);
        Assert.False(second.AuthorizeCoyote);
    }

    [Fact]
    public void PlanningWithoutCommitDoesNotConsumeAPressOrStartGrace()
    {
        var buffer = Input();
        var first = buffer.Plan(0, true, false, true, false);
        var repeat = buffer.Plan(0, true, false, true, false);
        Assert.Equal(first, repeat);
        Assert.False(buffer.HasPendingPress);
        Assert.False(buffer.IsButtonHeld);
    }

    [Fact]
    public void ResetDiscardsPendingPressAndHeldButton()
    {
        var buffer = Input();
        var press = buffer.Plan(0, false, true, true, false);
        buffer.Commit(press, 0.01, false, false, false);
        Assert.True(buffer.HasPendingPress);
        Assert.True(buffer.IsButtonHeld);
        buffer.Reset();
        Assert.False(buffer.HasPendingPress);
        Assert.False(buffer.IsButtonHeld);
        Assert.False(buffer.Plan(0, true, false, false, false).StartJump);
    }

    [Fact]
    public void InvalidOrBackwardTimeIsRejected()
    {
        var buffer = Input();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            buffer.Plan(double.NaN, false, false, false, false));
        var initial = buffer.Plan(1.0, false, true, true, false);
        buffer.Commit(initial, 1.1, false, false, false);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            buffer.Plan(0.9, false, false, false, false));
    }
}
