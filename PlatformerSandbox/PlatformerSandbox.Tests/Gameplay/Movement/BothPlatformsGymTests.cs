using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class BothPlatformsGymTests
{
    [Fact]
    public void DefaultSessionHasTwoDistinctLivePlatformsWithoutModeSwitch()
    {
        var gym = new MovementGymSession();
        Assert.Equal(MovingPlatformGymMode.Both, gym.PlatformMode);
        Assert.Equal(new[] {7, 8}, gym.MovingPlatforms.Select(p => p.PlatformId).ToArray());
        var first = gym.MovingPlatforms.ToArray();
        gym.Step(.25, HorizontalDirection.Neutral, false);
        var next = gym.MovingPlatforms.ToArray();
        Assert.NotEqual(first[0].Bounds, next[0].Bounds);
        Assert.NotEqual(first[1].Bounds, next[1].Bounds);
    }

    [Fact]
    public void DefaultDualPlatformGymReplaysDeterministically()
    {
        var a = new MovementGymSession();
        var b = new MovementGymSession();
        for (int i = 0; i < 600; ++i)
        {
            HorizontalDirection d = i % 180 < 60 ? HorizontalDirection.Left :
                i % 180 < 120 ? HorizontalDirection.Right : HorizontalDirection.Neutral;
            bool pressed = i % 90 == 0;
            bool released = i % 90 == 10;
            // Attach the deterministic input frame to any real simulation
            // failure so a replay fault can be reproduced without guessing
            // which of the 600 input transitions encountered the issue.
            try
            {
                a.Step(1.0 / 60.0, d, pressed, released);
                b.Step(1.0 / 60.0, d, pressed, released);
            }
            catch (Exception error)
            {
                throw new InvalidOperationException(
                    $"Dual-platform replay failed at frame {i}, " +
                    $"clock {a.ElapsedSeconds:R}, direction {d}, " +
                    $"press {pressed}, release {released}, " +
                    $"bounds {a.PlayerBounds}, velocity {a.PlayerVelocity}.", error);
            }
            Assert.Equal(a.PlayerBounds, b.PlayerBounds);
            Assert.Equal(a.PlayerVelocity, b.PlayerVelocity);
            Assert.Equal(a.MovingSupportId, b.MovingSupportId);
        }
    }
}
