using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>Contact event in world coordinates with times relative to this motor's takeoff and step.</summary>
public readonly record struct JumpMotorContact(
    int ColliderId,
    double TimeWithinStepSeconds,
    double TimeSinceTakeoffSeconds,
    Vector2D Normal,
    Aabb BoundsAtImpact,
    Vector2D IncomingVelocity,
    Vector2D OutgoingVelocity);
