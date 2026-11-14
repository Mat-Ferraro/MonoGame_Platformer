namespace PlatformerSandbox.Gameplay.Movement.Gyms;

/// <summary>
/// Presentation-safe snapshot. Stop time and distance assume unobstructed
/// neutral-input braking from the current horizontal velocity; they are not
/// predictions of collision or platform landing.
/// </summary>
public readonly record struct MovementGymDiagnostics(
    double HorizontalSpeed,
    double MaximumHorizontalSpeed,
    double StoppingDeceleration,
    double FreeStoppingTimeSeconds,
    double FreeStoppingDistanceUnits,
    bool GroundMovementProfile);
