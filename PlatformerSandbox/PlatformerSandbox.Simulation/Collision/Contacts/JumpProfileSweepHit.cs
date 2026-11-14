using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Collision.Contacts;

/// <summary>
/// First blocking event along an unobstructed jump reference. AABB coordinates
/// are world-space (positive Y is up). Time is measured since takeoff.
/// </summary>
public readonly record struct JumpProfileSweepHit(
    int ColliderId,
    double TimeSeconds,
    Vector2D Normal,
    Aabb MovingBoundsAtImpact,
    Vector2D VelocityAtImpact,
    Vector2D AccelerationAtImpact);
