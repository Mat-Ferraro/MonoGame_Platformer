using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class Milestone24StressAndFaultModeTests
{
    private static MovementReplayInput[] Schedule(int count)
    {
        var steps = new MovementReplayInput[count];
        // Pure integer pattern: no wall clock, random seed, controller device,
        // frame-time accumulation, or nondeterministic external dependency.
        for (int i = 0; i < steps.Length; i++)
        {
            int phase = i % 240;
            HorizontalDirection direction = phase < 80 ? HorizontalDirection.Right :
                phase < 160 ? HorizontalDirection.Left : HorizontalDirection.Neutral;
            steps[i] = new MovementReplayInput(direction,
                JumpPressed: phase == 7 || phase == 105,
                JumpReleased: phase == 12 || phase == 111);
        }
        return steps;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TenThousandFixedStepsAreReplayDeterministic(int presetIndex)
    {
        MovementGymTuning tuning = presetIndex switch
        {
            0 => MovementGymTuning.Baseline,
            1 => MovementGymTuning.Responsive,
            _ => MovementGymTuning.Snappy
        };
        MovementReplayInput[] inputs = Schedule(10_000);
        var first = MovementGymReplay.Run(new MovementGymSession(tuning,
            SimulationFaultSensitivity.Diagnostic), inputs);
        var second = MovementGymReplay.Run(new MovementGymSession(tuning,
            SimulationFaultSensitivity.Diagnostic), inputs);
        Assert.Equal(10_000, first.Steps);
        Assert.Equal(first.StateHash, second.StateHash);
        Assert.Equal(first.JumpFrames, second.JumpFrames);
        Assert.Equal(first.MovingPlatformRideFrames, second.MovingPlatformRideFrames);
        Assert.Equal(0, first.CheckpointFallbacks);
    }

    [Fact]
    public void StrictModeDoesNotSilentlyRecoverKnownAnomalies()
    {
        var session = SimulationFaultPolicy.Development.StartStep();
        Assert.Throws<SimulationFaultException>(() =>
            session.RecordRecovery(SimulationRecoveryKind.SubResolutionTimeRemainder,
                0.0, "Known tiny remainder"));
    }

    [Theory]
    [InlineData(SimulationFaultSensitivity.Diagnostic)]
    [InlineData(SimulationFaultSensitivity.Resilient)]
    public void NonStrictModesRecordAnExplicitRecovery(SimulationFaultSensitivity mode)
    {
        var session = new SimulationFaultPolicy(mode).StartStep();
        session.RecordRecovery(SimulationRecoveryKind.ContactAlignment,
            1e-12, "Bounded contact alignment");
        Assert.Single(session.Recoveries);
        Assert.Equal(1, session.RecoveryCount);
    }

    [Fact]
    public void CheckpointFallbackNeverSwallowsArbitraryExceptions()
    {
        Assert.True(MovementGymCheckpointFallback.IsEligible(
            new SimulationEventLimitException("budget")));
        Assert.True(MovementGymCheckpointFallback.IsEligible(
            new SimulationNoProgressException("nonprogress")));
        Assert.False(MovementGymCheckpointFallback.IsEligible(
            new InvalidOperationException("unknown programming error")));
        Assert.False(MovementGymCheckpointFallback.IsEligible(
            new NullReferenceException("unknown error")));
    }

    [Fact]
    public void ReleaseFallbackRunsForAClassifiedFaultOnly()
    {
        int attempts = 0;
        int restarts = 0;
        int result = MovementGymCheckpointFallback.Execute(
            () => { attempts++; throw new SimulationNoProgressException("stalled"); },
            _ => { restarts++; return 17; },
            SimulationFaultSensitivity.Resilient, 0);
        Assert.Equal(17, result);
        Assert.Equal(1, attempts);
        Assert.Equal(1, restarts);
    }

    [Fact]
    public void StrictFaultIsNotRecoveredAndDoesNotRunFallback()
    {
        int fallbackCalls = 0;
        Assert.Throws<SimulationNoProgressException>(() =>
            MovementGymCheckpointFallback.Execute(
                () => throw new SimulationNoProgressException("stalled"),
                _ => { fallbackCalls++; return 0; },
                SimulationFaultSensitivity.Strict, 0));
        Assert.Equal(0, fallbackCalls);
    }

    [Fact]
    public void UnknownErrorsAndExhaustedRetriesAlwaysPropagate()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MovementGymCheckpointFallback.Execute(
                () => throw new InvalidOperationException("unexpected"),
                _ => 42, SimulationFaultSensitivity.Resilient, 0));
        Assert.Throws<SimulationEventLimitException>(() =>
            MovementGymCheckpointFallback.Execute(
                () => throw new SimulationEventLimitException("budget"),
                _ => 42, SimulationFaultSensitivity.Resilient,
                MovementGymCheckpointFallback.MaximumConsecutiveFallbacks));
    }

    [Fact]
    public void ReplayFailureIncludesFrameAndPreUpdateState()
    {
        var bad = new[] { new MovementReplayInput((HorizontalDirection)123) };
        var error = Assert.Throws<MovementReplayFailureException>(() =>
            MovementGymReplay.Run(new MovementGymSession(), bad));
        Assert.Equal(0, error.Frame);
        Assert.Equal(0.0, error.SimulationTimeSeconds);
        Assert.Contains("frame 0", error.Message);
    }
}
