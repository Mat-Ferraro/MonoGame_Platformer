using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class Milestone26CrushPolicyTests
{
    private static CharacterCrushEvent Sample => new(
        7, 3, CharacterCrushAxis.Vertical, .2,
        new Aabb(new Vector2D(0, .25), new Vector2D(.8, 1.85)));

    [Fact]
    public void ClassifiedCrushCanTriggerDeterministicRespawn()
    {
        int resets = 0;
        int result = MovementGymCrushHandler.Execute<int>(
            () => throw new CharacterCrushException(Sample),
            crush => { resets++; Assert.Equal(7, crush.PushingColliderId); return 42; },
            MovementGymCrushPolicy.Respawn);
        Assert.Equal(42, result);
        Assert.Equal(1, resets);
    }

    [Fact]
    public void ThrowPolicyPreservesPhysicalCrushForDebugging()
    {
        bool handled = false;
        Assert.Throws<CharacterCrushException>(() => MovementGymCrushHandler.Execute<int>(
            () => throw new CharacterCrushException(Sample),
            _ => { handled = true; return 0; }, MovementGymCrushPolicy.Throw));
        Assert.False(handled);
    }

    [Fact]
    public void UnknownFailuresAreNeverReclassifiedAsCrush()
    {
        bool handled = false;
        Assert.Throws<InvalidOperationException>(() => MovementGymCrushHandler.Execute<int>(
            () => throw new InvalidOperationException("Bug"),
            _ => { handled = true; return 0; }, MovementGymCrushPolicy.Respawn));
        Assert.False(handled);
    }

    [Fact]
    public void GymCrushPolicyIsIndependentOfNumericalFaultModeAndTuning()
    {
        var gym = new MovementGymSession(
            faultSensitivity: SimulationFaultSensitivity.Strict);
        Assert.Equal(MovementGymCrushPolicy.Respawn, gym.CrushPolicy);
        gym.SetCrushPolicy(MovementGymCrushPolicy.Throw);
        gym.SetFaultSensitivity(SimulationFaultSensitivity.Resilient);
        gym.ApplyTuning(MovementGymTuning.Snappy);
        gym.Reset();
        Assert.Equal(MovementGymCrushPolicy.Throw, gym.CrushPolicy);
        Assert.Equal(SimulationFaultSensitivity.Resilient, gym.FaultSensitivity);
        Assert.Equal(0, gym.CrushRespawns);
    }

    [Fact]
    public void InvalidPolicyCannotChangeSession()
    {
        var gym = new MovementGymSession();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            gym.SetCrushPolicy((MovementGymCrushPolicy)777));
        Assert.Equal(MovementGymCrushPolicy.Respawn, gym.CrushPolicy);
    }
}
