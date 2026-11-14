using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Diagnostics;

/// <summary>
/// Handles an analytically known, physically negligible remainder whose
/// duration cannot advance the current absolute binary64 simulation clock.
/// Only use for a remainder AFTER the caller has applied any event state
/// transition (speed cap, reversal, support departure, etc.). This helper
/// MUST NOT be used to discard an unknown collision search interval.
/// </summary>
public static class SimulationTimeProgress
{
    /// <summary>
    /// Returns true if an interval is nonadvancing and may be consumed under
    /// the configured policy. Returns false if time can advance normally.
    /// Throws on invalid arguments, unsafe displacement, or Strict recovery.
    /// </summary>
    public static bool TryConsumeKnownRemainder(
        double absoluteTimeSeconds,
        double durationSeconds,
        Vector2D velocity,
        Vector2D acceleration,
        SimulationFaultSession diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (!double.IsFinite(absoluteTimeSeconds) || absoluteTimeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(absoluteTimeSeconds));
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0 ||
            !double.IsFinite(absoluteTimeSeconds + durationSeconds))
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (durationSeconds == 0.0 ||
            absoluteTimeSeconds + durationSeconds > absoluteTimeSeconds)
            return false;

        // Bound the ACTUAL maximum proposed displacement, not merely the
        // smallness of the elapsed time. A huge velocity can still move a
        // long distance during a tiny duration.
        double halfTimeSquared = (0.5 * durationSeconds) * durationSeconds;
        double dx = velocity.X * durationSeconds + acceleration.X * halfTimeSquared;
        double dy = velocity.Y * durationSeconds + acceleration.Y * halfTimeSquared;
        double displacement = Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (!double.IsFinite(displacement) ||
            displacement > diagnostics.Policy.MaximumDiscardedDisplacement)
            throw new SimulationFaultException(
                SimulationRecoveryKind.SubResolutionTimeRemainder,
                "An unrepresentable time interval would cause an unsafe movement displacement.");

        diagnostics.RecordRecovery(SimulationRecoveryKind.SubResolutionTimeRemainder,
            displacement, "Consumed a known-event time remainder below absolute-clock resolution.");
        return true;
    }
}
