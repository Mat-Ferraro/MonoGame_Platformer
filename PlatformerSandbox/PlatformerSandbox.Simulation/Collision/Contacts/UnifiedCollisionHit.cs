using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Collision.Contacts;

/// <summary>The first collision event from one authoritative static/moving query.</summary>
public readonly record struct UnifiedCollisionHit(
    int ColliderId,
    bool IsMovingPlatform,
    double TimeSeconds,
    Vector2D Normal,
    Aabb BodyBoundsAtImpact,
    Vector2D BodyVelocityAtImpact,
    Vector2D SurfaceVelocityAtImpact)
{
    public Vector2D RelativeVelocityAtImpact =>
        BodyVelocityAtImpact - SurfaceVelocityAtImpact;
}
