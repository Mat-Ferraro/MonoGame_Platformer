namespace PlatformerSandbox.Simulation.Diagnostics;

/// <summary>Stable categories for deterministic simulation recovery reporting.</summary>
public enum SimulationRecoveryKind
{
    SubResolutionTimeRemainder = 0,
    BoundedPenetration = 1,
    ContactAlignment = 2
}
