using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Simulation.Motion.Solvers;

/// <summary>
/// Converts designer-facing durations into exact constant-acceleration rates.
/// No time-stepped integration or iteration is required.
/// </summary>
public static class HorizontalMotionSolver
{
    public static HorizontalMotionProfile Solve(HorizontalMotionConstraints constraints)
    {
        RequirePositiveFinite(constraints.MaximumSpeed, nameof(constraints.MaximumSpeed));
        RequirePositiveFinite(constraints.TimeToMaximumSpeedSeconds,
            nameof(constraints.TimeToMaximumSpeedSeconds));
        RequirePositiveFinite(constraints.TimeToStopSeconds, nameof(constraints.TimeToStopSeconds));
        RequirePositiveFinite(constraints.TimeToReverseSeconds, nameof(constraints.TimeToReverseSeconds));

        double brakingTime = constraints.TimeToReverseSeconds - constraints.TimeToMaximumSpeedSeconds;
        if (!double.IsFinite(brakingTime) || brakingTime <= 0.0)
        {
            throw new ArgumentException(
                "TimeToReverseSeconds must be greater than TimeToMaximumSpeedSeconds: " +
                "reversal includes a full acceleration from rest after braking.",
                nameof(constraints));
        }

        double accelerationRate = constraints.MaximumSpeed / constraints.TimeToMaximumSpeedSeconds;
        double stoppingRate = constraints.MaximumSpeed / constraints.TimeToStopSeconds;
        double reversalRate = constraints.MaximumSpeed / brakingTime;
        if (!double.IsFinite(accelerationRate) || accelerationRate <= 0.0 ||
            !double.IsFinite(stoppingRate) || stoppingRate <= 0.0 ||
            !double.IsFinite(reversalRate) || reversalRate <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(constraints),
                "These constraints produce unsupported non-finite or zero acceleration rates.");
        }

        return new HorizontalMotionProfile(
            constraints.MaximumSpeed, accelerationRate, stoppingRate, reversalRate);
    }

    private static void RequirePositiveFinite(double value, string parameterName)
    {
        if (!double.IsFinite(value) || value <= 0.0)
            throw new ArgumentOutOfRangeException(parameterName,
                "Value must be finite and greater than zero.");
    }
}
