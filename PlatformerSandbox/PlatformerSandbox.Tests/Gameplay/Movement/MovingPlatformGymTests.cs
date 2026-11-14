using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class MovingPlatformGymTests
{
    [Fact]
    public void PlatformIsExposedToRendererAsSimulationState()
    {
        var gym = new MovementGymSession();
        Assert.Equal(7, gym.MovingPlatform.PlatformId);
        Assert.False(gym.IsRidingMovingPlatform);
        Assert.DoesNotContain(gym.Colliders, c => c.Id == 7);
    }

    [Fact]
    public void MovingPlatformAdvancesWithSimulationClock()
    {
        var gym = new MovementGymSession();
        double start = gym.MovingPlatform.Bounds.Left;
        gym.Step(0.25, HorizontalDirection.Neutral, false);
        Assert.InRange(gym.MovingPlatform.Bounds.Left - start,
            0.249999999, 0.250000001);
    }

    [Fact]
    public void ResetAlsoResetsMovingPlatformClock()
    {
        var gym = new MovementGymSession();
        double start = gym.MovingPlatform.Bounds.Left;
        gym.Step(0.25, HorizontalDirection.Neutral, false);
        gym.Reset();
        Assert.Equal(start, gym.MovingPlatform.Bounds.Left);
    }

    [Fact]
    public void SameInputSequenceIsDeterministicWithMovingWorld()
    {
        var a = new MovementGymSession();
        var b = new MovementGymSession();
        for (int i = 0; i < 70; ++i)
        {
            HorizontalDirection d = i < 30 ? HorizontalDirection.Right : HorizontalDirection.Neutral;
            a.Step(MovementGymSession.FixedStepSeconds, d, i == 10);
            b.Step(MovementGymSession.FixedStepSeconds, d, i == 10);
        }
        Assert.Equal(a.PlayerBounds, b.PlayerBounds);
        Assert.Equal(a.PlayerVelocity, b.PlayerVelocity);
        Assert.Equal(a.MovingPlatform.Bounds, b.MovingPlatform.Bounds);
    }
}
