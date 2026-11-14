namespace PlatformerSandbox.Simulation.Diagnostics;

/// <summary>
/// Per-step recovery accounting. One session should not be reused across
/// multiple authoritative updates. Every recovery is counted, including in
/// Release mode, so QA telemetry can distinguish an ordinary run from one
/// that repeatedly required numerical correction.
/// </summary>
public sealed class SimulationFaultSession
{
    private readonly List<SimulationRecoveryRecord> _records = new();

    internal SimulationFaultSession(SimulationFaultPolicy policy)
    {
        Policy = policy;
    }

    public SimulationFaultPolicy Policy { get; }
    public int RecoveryCount => _records.Count;
    public IReadOnlyList<SimulationRecoveryRecord> Recoveries =>
        Array.AsReadOnly(_records.ToArray());

    /// <summary>
    /// Report an actual, bounded numerical recovery. Strict mode faults to
    /// expose it; Diagnostic and Resilient modes preserve the same correction.
    /// A breached recovery budget faults in every mode.
    /// </summary>
    public void RecordRecovery(SimulationRecoveryKind kind, double amount, string description)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!double.IsFinite(amount) || amount < 0.0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (_records.Count >= Policy.MaximumRecoveriesPerStep)
            throw new SimulationFaultException(kind,
                "Simulation recovery budget exceeded; the update cannot safely continue.");

        _records.Add(new SimulationRecoveryRecord(kind, amount, description));
        if (Policy.Sensitivity == SimulationFaultSensitivity.Strict)
            throw new SimulationFaultException(kind, description);
    }
}
