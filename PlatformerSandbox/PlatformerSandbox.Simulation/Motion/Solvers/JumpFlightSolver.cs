using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Simulation.Motion.Solvers;

/// <summary>
/// Composes the existing held-jump and early-release solvers into a complete
/// analytical vertical flight without changing either earlier implementation.
/// </summary>
public static class JumpFlightSolver
{
    /// <summary>
    /// Wraps a held reference jump without changing its trajectory or timing.
    /// </summary>
    public static JumpFlightTrajectory ForHeldJump(JumpTrajectory heldJump)
    {
        ArgumentNullException.ThrowIfNull(heldJump);
        return new JumpFlightTrajectory(heldJump);
    }

    /// <summary>
    /// Creates a reference flight containing the held ascent before release,
    /// the cut ascent after release, and the chosen analytical descent.
    /// Release time is measured since original takeoff, not since last update.
    /// </summary>
    public static JumpFlightTrajectory ForEarlyRelease(
        JumpTrajectory heldJump,
        JumpReleaseConstraints releaseConstraints,
        double releaseTimeSinceTakeoffSeconds,
        JumpDescentConstraints descentConstraints = default)
    {
        ArgumentNullException.ThrowIfNull(heldJump);

        // Validate the policy independently. Never silently ignore a supplied
        // duration or accept an unknown enum value.
        switch (descentConstraints.Policy)
        {
            case JumpDescentPolicy.PreserveReferenceFallLaw:
                if (descentConstraints.DescentDurationSeconds is not null)
                    throw new ArgumentException(
                        "PreserveReferenceFallLaw does not accept an explicit descent duration.",
                        nameof(descentConstraints));
                break;

            case JumpDescentPolicy.SpecifyDescentDuration:
                if (descentConstraints.DescentDurationSeconds is not double value ||
                    !double.IsFinite(value) || value <= 0.0)
                {
                    throw new ArgumentOutOfRangeException(nameof(descentConstraints),
                        "SpecifyDescentDuration requires a positive, finite duration.");
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(descentConstraints),
                    "Unknown analytical descent policy.");
        }

        JumpReleaseTrajectory cut = JumpReleaseSolver.Solve(
            heldJump, releaseConstraints, releaseTimeSinceTakeoffSeconds);

        double descentDuration = descentConstraints.Policy switch
        {
            JumpDescentPolicy.PreserveReferenceFallLaw =>
                heldJump.FallTimeSeconds * Math.Pow(cut.PeakHeight / heldJump.Height,
                    1.0 / heldJump.Exponent),
            JumpDescentPolicy.SpecifyDescentDuration =>
                descentConstraints.DescentDurationSeconds!.Value,
            _ => throw new InvalidOperationException("Descent policy must have been validated.")
        };

        double endTime = cut.ApexTimeSinceTakeoffSeconds + descentDuration;
        double apex = cut.ApexTimeSinceTakeoffSeconds;
        double initialAccelerationScale = cut.PeakHeight * heldJump.Exponent *
            (heldJump.Exponent - 1.0) / (descentDuration * descentDuration);
        double terminalVelocity = cut.PeakHeight * heldJump.Exponent / descentDuration;

        if (!double.IsFinite(descentDuration) || descentDuration <= 0.0 ||
            !double.IsFinite(apex) || apex <= cut.ReleaseTimeSeconds ||
            !double.IsFinite(endTime) || endTime <= apex ||
            !double.IsFinite(initialAccelerationScale) ||
            !double.IsFinite(terminalVelocity))
        {
            throw new ArgumentOutOfRangeException(nameof(descentConstraints),
                "The cut-jump descent cannot be represented within finite analytical precision.");
        }

        return new JumpFlightTrajectory(
            heldJump, cut, descentConstraints.Policy, descentDuration);
    }
}
