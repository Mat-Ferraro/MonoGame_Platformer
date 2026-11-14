using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Collision.Contacts;

/// <summary>
/// Earliest contact during a constant-acceleration 2D translation. Time is
/// in seconds from query start, not normalized. The outward normal is cardinal.
/// Impact bounds are aligned to the contacted face within the caller's explicit
/// contact alignment tolerance; no gameplay collision assistance is applied.
/// </summary>
public readonly record struct ParabolicSweepHit(
    int ColliderId,
    double TimeSeconds,
    Vector2D Normal,
    Aabb MovingBoundsAtImpact,
    Vector2D VelocityAtImpact);
