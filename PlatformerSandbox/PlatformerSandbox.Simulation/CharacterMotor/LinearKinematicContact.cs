using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// A blocking contact discovered while advancing one constant-velocity
/// segment. TimeSeconds is measured from the beginning of that step.
/// </summary>
public readonly record struct LinearKinematicContact(
    int ColliderId,
    double TimeSeconds,
    Vector2D Normal,
    Aabb BoundsAtImpact,
    Vector2D IncomingVelocity,
    Vector2D OutgoingVelocity);
