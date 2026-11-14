namespace PlatformerSandbox.Simulation.Diagnostics;

/// <summary>
/// Immutable per-simulation fault policy. Do not change physical laws based on
/// this mode: only the reaction to a verified, bounded recovery may differ.
/// </summary>
public sealed class SimulationFaultPolicy
{
    public static SimulationFaultPolicy Development { get; } =
        new(SimulationFaultSensitivity.Strict);

    public static SimulationFaultPolicy QualityAssurance { get; } =
        new(SimulationFaultSensitivity.Diagnostic);

    public static SimulationFaultPolicy Release { get; } =
        new(SimulationFaultSensitivity.Resilient);

    public SimulationFaultPolicy(
        SimulationFaultSensitivity sensitivity,
        double maximumDiscardedDisplacement = 1e-10,
        int maximumRecoveriesPerStep = 64)
    {
        if (!Enum.IsDefined(sensitivity))
            throw new ArgumentOutOfRangeException(nameof(sensitivity));
        if (!double.IsFinite(maximumDiscardedDisplacement) ||
            maximumDiscardedDisplacement < 0.0)
            throw new ArgumentOutOfRangeException(nameof(maximumDiscardedDisplacement));
        if (maximumRecoveriesPerStep < 1)
            throw new ArgumentOutOfRangeException(nameof(maximumRecoveriesPerStep));

        Sensitivity = sensitivity;
        MaximumDiscardedDisplacement = maximumDiscardedDisplacement;
        MaximumRecoveriesPerStep = maximumRecoveriesPerStep;
    }

    public SimulationFaultSensitivity Sensitivity { get; }
    public double MaximumDiscardedDisplacement { get; }
    public int MaximumRecoveriesPerStep { get; }

    public SimulationFaultSession StartStep() => new(this);
}
