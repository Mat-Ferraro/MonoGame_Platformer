using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// Blocking contact at an analytically calculated time within one accelerated
/// step. All timestamps are measured from the START of the requested step.
/// </summary>
public readonly record struct AcceleratedKinematicContact(
    int ColliderId,
    double TimeSeconds,
    Vector2D Normal,
    Aabb BoundsAtImpact,
    Vector2D IncomingVelocity,
    Vector2D OutgoingVelocity,
    Vector2D IncomingAcceleration,
    Vector2D OutgoingAcceleration);
