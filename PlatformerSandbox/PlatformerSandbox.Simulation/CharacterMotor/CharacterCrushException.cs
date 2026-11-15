namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// A deterministic opposing-surface outcome for gameplay to handle. It is
/// deliberately NOT a SimulationFaultException: Strict mode remains strict
/// about numerical faults, but a legitimate crush is a gameplay consequence.
/// </summary>
public sealed class CharacterCrushException : InvalidOperationException
{
    public CharacterCrushException(CharacterCrushEvent crush)
        : base($"Character crushed on {crush?.Axis} axis between moving collider " +
               $"{crush?.PushingColliderId} and collider {crush?.OpposingColliderId} " +
               $"at world time {crush?.WorldTimeSeconds:R}.")
    {
        Crush = crush ?? throw new ArgumentNullException(nameof(crush));
    }

    public CharacterCrushEvent Crush { get; }
}
