namespace PlatformerSandbox.Simulation.Diagnostics;

/// <summary>Raised when a strict-mode recovery is encountered or a recovery is unsafe.</summary>
public sealed class SimulationFaultException : InvalidOperationException
{
    public SimulationFaultException(SimulationRecoveryKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }

    public SimulationRecoveryKind Kind { get; }
}
