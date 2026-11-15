using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class VerticalElevatorGymTests
{
    [Fact]
    public void DefaultGymContainsHorizontalPlatformAndVerticalElevator()
    {
        var gym = new MovementGymSession();
        Assert.Equal(MovingPlatformGymMode.Both, gym.PlatformMode);
        Assert.Equal(2, gym.MovingPlatforms.Count);
        Assert.Contains(gym.MovingPlatforms, p => p.PlatformId == 7 && p.Velocity.Y == 0);
        Assert.Contains(gym.MovingPlatforms, p => p.PlatformId == 8 && p.Velocity.Y > 0);
        Assert.False(gym.IsRidingMovingPlatform);
        Assert.Equal(0.0, gym.PlayerBounds.Bottom);
    }

    [Fact]
    public void CombinedGymHasSolidFloorBeneathLeftElevator()
    {
        var gym = new MovementGymSession();
        var floor = Assert.Single(gym.Colliders, c => c.Id == 10);
        var lift = Assert.Single(gym.MovingPlatforms, p => p.PlatformId == 8);

        Assert.Equal(-8.4, floor.Bounds.Left);
        Assert.Equal(-5.2, floor.Bounds.Right);
        Assert.Equal(-1.0, floor.Bounds.Top);
        // The floor must not initially intersect the elevator.
        Assert.True(floor.Bounds.Top < lift.Bounds.Bottom);
    }

    [Fact]
    public void ElevatorModeStartsSupportedAndRisesImmediately()
    {
        var gym = new MovementGymSession(platformMode: MovingPlatformGymMode.Elevator);
        Assert.True(gym.IsRidingMovingPlatform);
        Assert.Equal(7, gym.MovingPlatform.PlatformId);
        Assert.Equal(gym.MovingPlatform.Bounds.Top, gym.PlayerBounds.Bottom);
        gym.Step(.25, HorizontalDirection.Neutral, false);
        Assert.True(gym.IsRidingMovingPlatform);
        Assert.Equal(gym.MovingPlatform.Bounds.Top, gym.PlayerBounds.Bottom);
        Assert.InRange(gym.PlayerBounds.Bottom, .374999999, .375000001);
    }

    [Fact]
    public void ChangingGymModeIsAtomicAndPreservesTuningAndFaultPolicy()
    {
        var gym = new MovementGymSession(
            tuning: MovementGymTuning.Snappy,
            faultSensitivity: SimulationFaultSensitivity.Resilient);
        gym.Step(.2, HorizontalDirection.Right, false);
        gym.SetPlatformMode(MovingPlatformGymMode.Elevator);
        Assert.Equal(MovingPlatformGymMode.Elevator, gym.PlatformMode);
        Assert.Equal(MovementGymTuning.Snappy, gym.Tuning);
        Assert.Equal(SimulationFaultSensitivity.Resilient, gym.FaultSensitivity);
        Assert.Equal(0, gym.ElapsedSeconds);
        Assert.True(gym.IsRidingMovingPlatform);
        gym.SetPlatformMode(MovingPlatformGymMode.Horizontal);
        Assert.Equal(MovingPlatformGymMode.Horizontal, gym.PlatformMode);
        Assert.Equal(0, gym.ElapsedSeconds);
        Assert.False(gym.IsRidingMovingPlatform);
    }

    [Fact]
    public void InvalidModeDoesNotModifyTheActiveGym()
    {
        var gym = new MovementGymSession();
        var previous = gym.MovingPlatform;
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            gym.SetPlatformMode((MovingPlatformGymMode)999));
        // The normal gym now launches both independent fixtures.
        Assert.Equal(MovingPlatformGymMode.Both, gym.PlatformMode);
        Assert.Equal(previous.Bounds, gym.MovingPlatform.Bounds);
        Assert.Equal(2, gym.MovingPlatforms.Count);
    }

    [Fact]
    public void ReplayingSameElevatorInputSequenceIsDeterministic()
    {
        var a = new MovementGymSession(platformMode: MovingPlatformGymMode.Elevator);
        var b = new MovementGymSession(platformMode: MovingPlatformGymMode.Elevator);
        for (int i = 0; i < 600; ++i)
        {
            a.Step(MovementGymSession.FixedStepSeconds, HorizontalDirection.Neutral, false);
            b.Step(MovementGymSession.FixedStepSeconds, HorizontalDirection.Neutral, false);
            Assert.Equal(a.PlayerBounds, b.PlayerBounds);
            Assert.Equal(a.PlayerVelocity, b.PlayerVelocity);
            Assert.Equal(a.MovingPlatform.Bounds, b.MovingPlatform.Bounds);
        }
    }
}
