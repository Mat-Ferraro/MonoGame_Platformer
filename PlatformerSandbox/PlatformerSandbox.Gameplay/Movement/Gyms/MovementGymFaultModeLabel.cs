using PlatformerSandbox.Simulation.Diagnostics;

namespace PlatformerSandbox.Gameplay.Movement.Gyms;

/// <summary>Presentation vocabulary only; the selected mode is authoritative in the motor.</summary>
public static class MovementGymFaultModeLabel
{
    public static string For(SimulationFaultSensitivity sensitivity) => sensitivity switch
    {
        SimulationFaultSensitivity.Strict => "FAULT: STRICT",
        SimulationFaultSensitivity.Diagnostic => "FAULT: DIAGNOSTIC",
        SimulationFaultSensitivity.Resilient => "FAULT: RESILIENT",
        _ => throw new ArgumentOutOfRangeException(nameof(sensitivity))
    };
}
