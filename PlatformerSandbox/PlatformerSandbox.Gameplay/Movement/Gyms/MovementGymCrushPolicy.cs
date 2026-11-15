namespace PlatformerSandbox.Gameplay.Movement.Gyms;

/// <summary>
/// Game design chooses how to react to a classified crush independently of
/// Strict/Diagnostic/Resilient numerical-fault sensitivity.
/// </summary>
public enum MovementGymCrushPolicy
{
    Respawn,
    Throw
}
