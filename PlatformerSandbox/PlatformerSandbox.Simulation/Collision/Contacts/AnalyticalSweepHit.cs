using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Collision.Contacts;

/// <summary>
/// First contact on a caller-supplied, piecewise-monotone analytical path.
/// The detector does not choose a response or mutate the moving body.
/// </summary>
public readonly record struct AnalyticalSweepHit(
    int ColliderId,
    double TimeSeconds,
    Vector2D Normal,
    Aabb MovingBoundsAtImpact,
    Vector2D VelocityAtImpact);
