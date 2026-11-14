namespace PlatformerSandbox.Simulation.Diagnostics;

/// <summary>
/// Deterministic diagnostic; contains no wall-clock time or non-repeatable IDs.
/// Callers may attach simulation tick and entity identity in their own log.
/// </summary>
public readonly record struct SimulationRecoveryRecord(
    SimulationRecoveryKind Kind,
    double Amount,
    string Description);
