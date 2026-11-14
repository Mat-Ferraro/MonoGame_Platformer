using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class VariableJumpGymTests
{
    [Fact]
    public void GymForwardsSpaceReleaseToMotor()
    {
        var gym = new MovementGymSession();
        gym.Step(0.1, HorizontalDirection.Neutral, jumpPressedThisStep: true);
        Assert.False(gym.IsJumpEarlyReleased);
        gym.Step(0.02, HorizontalDirection.Neutral, false, jumpReleasedThisStep: true);
        Assert.True(gym.IsJumpEarlyReleased);
    }

    [Fact]
    public void AFullHoldGoesHigherThanAnEarlyRelease()
    {
        var held = new MovementGymSession();
        var cut = new MovementGymSession();
        held.Step(0.1, HorizontalDirection.Neutral, true);
        cut.Step(0.1, HorizontalDirection.Neutral, true);
        cut.Step(0.01, HorizontalDirection.Neutral, false, jumpReleasedThisStep: true);
        held.Step(0.01, HorizontalDirection.Neutral, false);

        double maxHeld = held.PlayerBounds.Bottom;
        double maxCut = cut.PlayerBounds.Bottom;
        for (int i = 0; i < 40; i++)
        {
            held.Step(0.01, HorizontalDirection.Neutral, false);
            cut.Step(0.01, HorizontalDirection.Neutral, false);
            maxHeld = Math.Max(maxHeld, held.PlayerBounds.Bottom);
            maxCut = Math.Max(maxCut, cut.PlayerBounds.Bottom);
        }
        Assert.True(maxHeld > maxCut + 0.5);
    }

    [Fact]
    public void GymResetClearsPreviousEarlyRelease()
    {
        var gym = new MovementGymSession();
        gym.Step(0.1, HorizontalDirection.Neutral, true);
        gym.Step(0.02, HorizontalDirection.Neutral, false, jumpReleasedThisStep: true);
        Assert.True(gym.IsJumpEarlyReleased);
        gym.Reset();
        Assert.False(gym.IsJumpEarlyReleased);
        Assert.True(gym.IsGrounded);
    }
}
