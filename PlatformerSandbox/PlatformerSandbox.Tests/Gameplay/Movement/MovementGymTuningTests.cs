using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.Movement;

public sealed class MovementGymTuningTests
{
    [Fact]
    public void BaselinePreservesAllPreviouslyShippedMovementValues()
    {
        MovementGymTuning tuning = MovementGymTuning.Baseline;
        Assert.Equal(new HorizontalMotionConstraints(9, 0.18, 0.13, 0.38), tuning.Ground);
        Assert.Equal(new HorizontalMotionConstraints(9, 0.22, 0.16, 0.43), tuning.Air);
    }

    [Fact]
    public void DefaultSessionUsesBaseline()
    {
        Assert.Equal(MovementGymTuning.Baseline, new MovementGymSession().Tuning);
    }

    [Fact]
    public void PresetsHaveValidAnalyticalConstraints()
    {
        MovementGymTuning.Baseline.Validate();
        MovementGymTuning.Responsive.Validate();
        MovementGymTuning.Snappy.Validate();
    }

    [Fact]
    public void ResponsivePresetReducesStoppingAndReversalTimes()
    {
        Assert.True(MovementGymTuning.Responsive.Ground.TimeToStopSeconds <
            MovementGymTuning.Baseline.Ground.TimeToStopSeconds);
        Assert.True(MovementGymTuning.Responsive.Air.TimeToStopSeconds <
            MovementGymTuning.Baseline.Air.TimeToStopSeconds);
        Assert.True(MovementGymTuning.Responsive.Ground.TimeToReverseSeconds <
            MovementGymTuning.Baseline.Ground.TimeToReverseSeconds);
    }

    [Fact]
    public void SnappyPresetReducesTimesFurther()
    {
        Assert.True(MovementGymTuning.Snappy.Ground.TimeToStopSeconds <
            MovementGymTuning.Responsive.Ground.TimeToStopSeconds);
        Assert.True(MovementGymTuning.Snappy.Air.TimeToMaximumSpeedSeconds <
            MovementGymTuning.Responsive.Air.TimeToMaximumSpeedSeconds);
    }

    [Fact]
    public void CustomTuningCanBeProvidedAtConstruction()
    {
        var gym = new MovementGymSession(MovementGymTuning.Responsive);
        Assert.Equal(MovementGymTuning.Responsive, gym.Tuning);
        Assert.True(gym.IsGrounded);
    }

    [Fact]
    public void ApplyingTuningResetsStateButDoesNotReplaceTheLevel()
    {
        var gym = new MovementGymSession();
        int[] ids = gym.Colliders.Select(c => c.Id).ToArray();
        gym.Step(0.1, HorizontalDirection.Neutral, true);
        Assert.Equal(1, gym.JumpsStarted);
        gym.ApplyTuning(MovementGymTuning.Responsive);
        Assert.True(gym.IsGrounded);
        Assert.False(gym.IsJumping);
        Assert.Equal(0, gym.JumpsStarted);
        Assert.Equal(0, gym.PlayerBounds.Left);
        Assert.Equal(0, gym.PlayerBounds.Bottom);
        Assert.Equal(MovementGymTuning.Responsive, gym.Tuning);
        Assert.Equal(ids, gym.Colliders.Select(c => c.Id));
    }

    [Fact]
    public void FailedTuningLeavesLiveSimulationStateUntouched()
    {
        var gym = new MovementGymSession();
        gym.Step(0.05, HorizontalDirection.Right, false);
        var beforeBounds = gym.PlayerBounds;
        var beforeVelocity = gym.PlayerVelocity;
        var bad = new MovementGymTuning(
            new HorizontalMotionConstraints(9, 0.18, 0.13, 0.18),
            MovementGymTuning.Baseline.Air);
        Assert.Throws<ArgumentException>(() => gym.ApplyTuning(bad));
        Assert.Equal(beforeBounds, gym.PlayerBounds);
        Assert.Equal(beforeVelocity, gym.PlayerVelocity);
        Assert.Equal(MovementGymTuning.Baseline, gym.Tuning);
    }

    [Fact]
    public void OrdinaryResetRetainsCustomTuning()
    {
        var gym = new MovementGymSession();
        gym.ApplyTuning(MovementGymTuning.Snappy);
        gym.Step(0.07, HorizontalDirection.Right, false);
        gym.Reset();
        Assert.Equal(MovementGymTuning.Snappy, gym.Tuning);
        Assert.Equal(0, gym.PlayerVelocity.X);
    }

    [Fact]
    public void MoreResponsivePresetBrakesFasterAtSameVelocity()
    {
        var baseline = new MovementGymSession(MovementGymTuning.Baseline);
        var responsive = new MovementGymSession(MovementGymTuning.Responsive);
        baseline.Step(0.35, HorizontalDirection.Right, false);
        responsive.Step(0.35, HorizontalDirection.Right, false);
        Assert.Equal(9.0, baseline.PlayerVelocity.X, 8);
        Assert.Equal(9.0, responsive.PlayerVelocity.X, 8);
        baseline.Step(0.05, HorizontalDirection.Neutral, false);
        responsive.Step(0.05, HorizontalDirection.Neutral, false);
        Assert.True(responsive.PlayerVelocity.X < baseline.PlayerVelocity.X);
    }

    [Fact]
    public void SamePresetAndInputScheduleAreDeterministic()
    {
        var a = new MovementGymSession(MovementGymTuning.Snappy);
        var b = new MovementGymSession(MovementGymTuning.Snappy);
        for (int i = 0; i < 90; i++)
        {
            HorizontalDirection direction = i < 25 ? HorizontalDirection.Right :
                i < 55 ? HorizontalDirection.Left : HorizontalDirection.Neutral;
            bool jump = i == 5;
            a.Step(MovementGymSession.FixedStepSeconds, direction, jump);
            b.Step(MovementGymSession.FixedStepSeconds, direction, jump);
        }
        Assert.Equal(a.PlayerBounds, b.PlayerBounds);
        Assert.Equal(a.PlayerVelocity, b.PlayerVelocity);
        Assert.Equal(a.JumpsStarted, b.JumpsStarted);
    }

    [Fact]
    public void DiagnosticsMatchUnobstructedAnalyticalBraking()
    {
        var gym = new MovementGymSession();
        gym.Step(0.05, HorizontalDirection.Right, false);
        MovementGymDiagnostics d = gym.Diagnostics;
        double rate = 9.0 / 0.13;
        double speed = Math.Abs(gym.PlayerVelocity.X);
        Assert.True(d.GroundMovementProfile);
        Assert.Equal(speed, d.HorizontalSpeed, 8);
        Assert.Equal(9.0, d.MaximumHorizontalSpeed);
        Assert.Equal(rate, d.StoppingDeceleration, 8);
        Assert.Equal(speed / rate, d.FreeStoppingTimeSeconds, 8);
        Assert.Equal(speed * speed / (2 * rate), d.FreeStoppingDistanceUnits, 8);
    }

    [Fact]
    public void DiagnosticsAreObservationalOnly()
    {
        var gym = new MovementGymSession();
        gym.Step(0.05, HorizontalDirection.Right, false);
        var beforePosition = gym.PlayerBounds;
        var beforeVelocity = gym.PlayerVelocity;
        MovementGymDiagnostics a = gym.Diagnostics;
        MovementGymDiagnostics b = gym.Diagnostics;
        Assert.Equal(a, b);
        Assert.Equal(beforePosition, gym.PlayerBounds);
        Assert.Equal(beforeVelocity, gym.PlayerVelocity);
    }

    [Fact]
    public void AirDiagnosticsUseAirStoppingRate()
    {
        var gym = new MovementGymSession();
        gym.Step(0.05, HorizontalDirection.Right, true);
        Assert.False(gym.IsGrounded);
        var d = gym.Diagnostics;
        Assert.False(d.GroundMovementProfile);
        Assert.Equal(9.0 / 0.16, d.StoppingDeceleration, 8);
    }

    [Fact]
    public void EachEditorParameterCanIncreaseFromBaseline()
    {
        foreach (MovementGymParameter parameter in Enum.GetValues<MovementGymParameter>())
        {
            MovementGymTuning initial = MovementGymTuning.Baseline;
            Assert.True(initial.TryAdjust(parameter, 1, out MovementGymTuning next));
            Assert.True(next.Get(parameter) > initial.Get(parameter));
            next.Validate();
        }
    }

    [Fact]
    public void EachEditorParameterCanDecreaseFromBaseline()
    {
        foreach (MovementGymParameter parameter in Enum.GetValues<MovementGymParameter>())
        {
            MovementGymTuning initial = MovementGymTuning.Baseline;
            Assert.True(initial.TryAdjust(parameter, -1, out MovementGymTuning next));
            Assert.True(next.Get(parameter) < initial.Get(parameter));
            next.Validate();
        }
    }

    [Fact]
    public void EditorRejectsAnImpossibleReversalWithoutChangingTuning()
    {
        MovementGymTuning initial = MovementGymTuning.Baseline with
        {
            Ground = MovementGymTuning.Baseline.Ground with
            {
                TimeToReverseSeconds = 0.19
            }
        };
        initial.Validate();
        Assert.False(initial.TryAdjust(MovementGymParameter.GroundReversalTime,
            -1, out MovementGymTuning unchanged));
        Assert.Equal(initial, unchanged);
    }

    [Fact]
    public void EditorRejectsAnInvalidStep()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovementGymTuning.Baseline.TryAdjust(
                MovementGymParameter.GroundStoppingTime, 0, out _));
    }

    [Fact]
    public void ApplyingTuningClearsPendingBufferedJump()
    {
        var gym = new MovementGymSession();
        gym.Step(0.3, HorizontalDirection.Neutral, true);
        gym.Step(0.01, HorizontalDirection.Neutral, false,
            jumpReleasedThisStep: true);
        gym.Step(0.01, HorizontalDirection.Neutral, true);
        gym.ApplyTuning(MovementGymTuning.Responsive);
        var result = gym.Step(MovementGymSession.FixedStepSeconds,
            HorizontalDirection.Neutral, false);
        Assert.False(result.JumpStarted);
    }
}
