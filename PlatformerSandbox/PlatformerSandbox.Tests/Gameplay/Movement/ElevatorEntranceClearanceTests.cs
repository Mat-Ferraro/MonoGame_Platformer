using PlatformerSandbox.Gameplay.Movement.Gyms;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class ElevatorEntranceClearanceTests
{
    [Fact]
    public void CombinedGymHasClearanceBetweenLiftAndRightBank()
    {
        var gym = new MovementGymSession();
        var lift = Assert.Single(gym.MovingPlatforms, p => p.PlatformId == 8);
        var bank = Assert.Single(gym.Colliders, c => c.Id == 9);
        var lowerFloor = Assert.Single(gym.Colliders, c => c.Id == 10);
        Assert.Equal(-5.2, bank.Bounds.Left);
        Assert.Equal(-5.2, lowerFloor.Bounds.Right);
        Assert.True(bank.Bounds.Left - lift.Bounds.Right > gym.PlayerBounds.Width + 0.1);
        Assert.Equal(-1.0, lowerFloor.Bounds.Top);
    }
}
