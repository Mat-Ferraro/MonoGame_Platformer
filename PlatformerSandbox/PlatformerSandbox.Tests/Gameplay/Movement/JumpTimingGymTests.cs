using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class JumpTimingGymTests
{
    private const HorizontalDirection Still = HorizontalDirection.Neutral;

    [Fact]
    public void WalkOffLedgeCanJumpDuringCoyoteWindow()
    {
        var gym = NewGymThatHasWalkedOffTheRightLedge();
        Assert.False(gym.IsGrounded);
        Assert.Equal(0, gym.JumpsStarted);
        var step = gym.Step(MovementGymSession.FixedStepSeconds, Still, true);
        Assert.True(step.JumpStarted);
        Assert.Equal(1, gym.JumpsStarted);
        Assert.True(gym.PlayerVelocity.Y > 0.0);
    }

    [Fact]
    public void WalkOffJumpIsRejectedAfterCoyoteExpires()
    {
        var gym = NewGymThatHasWalkedOffTheRightLedge();
        gym.Step(0.2, Still, false);
        Assert.False(gym.IsGrounded);
        var step = gym.Step(MovementGymSession.FixedStepSeconds, Still, true);
        Assert.False(step.JumpStarted);
        Assert.Equal(0, gym.JumpsStarted);
    }

    [Fact]
    public void PressJustBeforeLandingBeginsNextJumpOnFollowingUpdate()
    {
        var gym = new MovementGymSession();
        gym.Step(0.62, Still, true);
        gym.Step(0.03, Still, false, jumpReleasedThisStep: true);
        Assert.False(gym.IsGrounded);
        var buffered = gym.Step(0.04, Still, true);
        Assert.False(buffered.JumpStarted);
        Assert.Equal(1, gym.JumpsStarted);

        for (int i = 0; i < 5 && !gym.IsGrounded; i++)
            gym.Step(MovementGymSession.FixedStepSeconds, Still, false);
        Assert.True(gym.IsGrounded);
        var next = gym.Step(MovementGymSession.FixedStepSeconds, Still, false);
        Assert.True(next.JumpStarted);
        Assert.Equal(2, gym.JumpsStarted);
    }

    [Fact]
    public void EarlyBufferedTapStartsAShortJumpAfterLanding()
    {
        var gym = new MovementGymSession();
        gym.Step(0.62, Still, true);
        gym.Step(0.03, Still, false, jumpReleasedThisStep: true);
        gym.Step(0.02, Still, true);
        gym.Step(0.01, Still, false, jumpReleasedThisStep: true);
        for (int i = 0; i < 10 && !gym.IsGrounded; i++)
            gym.Step(MovementGymSession.FixedStepSeconds, Still, false);
        Assert.True(gym.IsGrounded);
        var started = gym.Step(MovementGymSession.FixedStepSeconds, Still, false);
        Assert.True(started.JumpStarted);
        Assert.True(gym.IsJumpEarlyReleased);
    }

    [Fact]
    public void InputBufferIsClearedWhenGymResets()
    {
        var gym = new MovementGymSession();
        gym.Step(0.3, Still, true);
        gym.Step(0.01, Still, false, jumpReleasedThisStep: true);
        gym.Step(0.01, Still, true);  // second press while in flight is pending
        gym.Reset();
        Assert.Equal(0, gym.JumpsStarted);
        var still = gym.Step(MovementGymSession.FixedStepSeconds, Still, false);
        Assert.False(still.JumpStarted);
    }

    [Fact]
    public void ZeroTimeJumpPressIsIgnoredWithoutQueuingIt()
    {
        var gym = new MovementGymSession();
        gym.Step(0, Still, true);
        var state = gym.Step(MovementGymSession.FixedStepSeconds, Still, false);
        Assert.False(state.JumpStarted);
    }

    private static MovementGymSession NewGymThatHasWalkedOffTheRightLedge()
    {
        var gym = new MovementGymSession();
        for (int i = 0; i < 180 && gym.IsGrounded; i++)
            gym.Step(MovementGymSession.FixedStepSeconds, HorizontalDirection.Right, false);
        Assert.False(gym.IsGrounded);
        Assert.InRange(gym.PlayerBounds.Left, 6.0, 8.0);
        return gym;
    }
}
