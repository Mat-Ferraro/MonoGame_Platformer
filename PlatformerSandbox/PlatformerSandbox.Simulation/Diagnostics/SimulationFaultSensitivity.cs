namespace PlatformerSandbox.Simulation.Diagnostics;

/// <summary>
/// Selects how the simulation treats an EXPLAINED, BOUNDED numerical recovery.
/// This setting is independent of Debug/Release compilation configuration.
/// Invalid inputs and unbounded errors always fail in every mode.
/// </summary>
public enum SimulationFaultSensitivity
{
    Strict = 0,
    Diagnostic = 1,
    Resilient = 2
}
