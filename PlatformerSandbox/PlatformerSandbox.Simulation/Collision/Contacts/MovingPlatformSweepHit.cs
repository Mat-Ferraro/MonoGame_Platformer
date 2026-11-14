using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Collision.Contacts;

/// <summary>
/// First impact against a translating platform. TimeSeconds is relative to the
/// collision query's start. Velocities are WORLD-space; RelativeVelocity is
/// computed from them, and Normal points outward from the platform.
/// </summary>
public readonly record struct MovingPlatformSweepHit(
    int PlatformId,
    double TimeSeconds,
    Vector2D Normal,
    Aabb BodyBoundsAtImpact,
    Aabb PlatformBoundsAtImpact,
    Vector2D BodyVelocityAtImpact,
    Vector2D PlatformVelocityAtImpact)
{
    public Vector2D RelativeVelocityAtImpact =>
        BodyVelocityAtImpact - PlatformVelocityAtImpact;
}
