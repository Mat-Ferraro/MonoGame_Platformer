using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>A contact during one authoritative controller update (Y positive upward).</summary>
public readonly record struct CharacterControlContact(
    int ColliderId,
    double TimeWithinStepSeconds,
    Vector2D Normal,
    Vector2D IncomingVelocity,
    Vector2D OutgoingVelocity);
