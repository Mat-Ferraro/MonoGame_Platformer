using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Simulation.Motion.Solvers;

/// <summary>
/// Constructs an analytically solvable early-release ascent from a solved A06
/// held-jump reference profile. Release is modeled as a new, constant-deceleration
/// segment that starts with identical position/velocity, avoiding velocity snaps.
///
/// The parameter MinimumHeight is the exact reference peak if the player
/// releases immediately at launch. Later releases produce a higher (but never
/// higher than the held jump's) peak for supported power curves, exponent >= 2.
/// </summary>
public static class JumpReleaseSolver
{
    public static JumpReleaseTrajectory Solve(
        JumpTrajectory heldJump,
        JumpReleaseConstraints release,
        double releaseTimeSeconds)
    {
        ArgumentNullException.ThrowIfNull(heldJump);

        if (!double.IsFinite(release.MinimumHeight) ||
            release.MinimumHeight <= 0.0 ||
            release.MinimumHeight >= heldJump.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(release),
                "MinimumHeight must be finite, positive, and less than the full jump height.");
        }

        if (!double.IsFinite(releaseTimeSeconds) ||
            releaseTimeSeconds < 0.0 ||
            releaseTimeSeconds >= heldJump.RiseTimeSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(releaseTimeSeconds),
                "Release must occur during the rising phase, before the apex.");
        }

        JumpSample launch = heldJump.Evaluate(0.0);
        JumpSample atRelease = heldJump.Evaluate(releaseTimeSeconds);

        if (!(launch.VerticalVelocity > 0.0) || !(atRelease.VerticalVelocity > 0.0))
            throw new ArgumentException("The reference jump has no finite upward velocity at release.", nameof(heldJump));

        // Exactly reach MinimumHeight for releaseTime=0 under constant deceleration:
        //   MinimumHeight = launchVelocity^2 / (2 * cutDeceleration).
        double cutDeceleration = launch.VerticalVelocity * launch.VerticalVelocity
            / (2.0 * release.MinimumHeight);
        double remainingRiseTime = atRelease.VerticalVelocity / cutDeceleration;
        double peakHeight = atRelease.VerticalDisplacement
            + 0.5 * atRelease.VerticalVelocity * remainingRiseTime;

        if (!double.IsFinite(cutDeceleration) || cutDeceleration <= 0.0 ||
            !double.IsFinite(remainingRiseTime) || remainingRiseTime <= 0.0 ||
            !double.IsFinite(peakHeight) || peakHeight < atRelease.VerticalDisplacement ||
            peakHeight > heldJump.Height * (1.0 + 1e-12))
        {
            throw new ArgumentOutOfRangeException(nameof(release),
                "These constraints exceed the supported finite analytical motion range.");
        }

        return new JumpReleaseTrajectory(
            releaseTimeSeconds,
            atRelease.VerticalDisplacement,
            atRelease.VerticalVelocity,
            cutDeceleration,
            remainingRiseTime,
            peakHeight);
    }
}
