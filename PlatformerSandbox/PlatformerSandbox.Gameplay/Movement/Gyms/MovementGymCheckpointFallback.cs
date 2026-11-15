using PlatformerSandbox.Simulation.Diagnostics;

namespace PlatformerSandbox.Gameplay.Movement.Gyms;

/// <summary>
/// The gym's checkpoint fallback is deliberately narrower than a catch-all.
/// Unexpected programming exceptions are never swallowed, including in
/// Resilient mode. A checkpoint restart may be used only for classified
/// simulation faults after the motor has rolled back its transactional step.
/// </summary>
public static class MovementGymCheckpointFallback
{
    public const int MaximumConsecutiveFallbacks = 3;

    public static bool IsEligible(Exception failure) => failure is
        SimulationFaultException or
        SimulationEventLimitException or
        SimulationNoProgressException;

    /// <summary>
    /// Only a classified failure in Resilient mode may invoke the supplied
    /// checkpoint recovery. Strict and Diagnostic modes rethrow unchanged.
    /// A fixed cap prevents endlessly replaying an unsafe state.
    /// </summary>
    public static TResult Execute<TResult>(
        Func<TResult> attempt,
        Func<Exception, TResult> fallback,
        SimulationFaultSensitivity sensitivity,
        int consecutiveFallbacks)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(fallback);
        if (!Enum.IsDefined(sensitivity))
            throw new ArgumentOutOfRangeException(nameof(sensitivity));
        if (consecutiveFallbacks < 0)
            throw new ArgumentOutOfRangeException(nameof(consecutiveFallbacks));
        try
        {
            return attempt();
        }
        catch (Exception error) when (
            sensitivity == SimulationFaultSensitivity.Resilient &&
            consecutiveFallbacks < MaximumConsecutiveFallbacks &&
            IsEligible(error))
        {
            return fallback(error);
        }
    }
}
