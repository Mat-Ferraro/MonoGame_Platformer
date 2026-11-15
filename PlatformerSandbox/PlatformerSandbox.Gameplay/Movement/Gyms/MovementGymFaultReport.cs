using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Gameplay.Movement.Gyms;

/// <summary>
/// Reproducible context captured BEFORE a Resilient-mode checkpoint fallback.
/// Observational metadata; the presentation layer may show or export it.
/// </summary>
public sealed record MovementGymFaultReport(
    double WorldTimeSeconds,
    Aabb BodyBounds,
    Vector2D Velocity,
    HorizontalDirection Direction,
    bool JumpPressed,
    string FaultType,
    string Description,
    int ConsecutiveFallbacks);
