using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Collision.Contacts;

/// <summary>
/// First impact of a box moving by one LINEAR displacement vector, normalized
/// to [0,1]. Normal points from obstacle toward moving box. No response is
/// applied. A TimeOfImpact of 0 means already touching and moving inward.
/// </summary>
public readonly record struct SweepHit(
    int ColliderId,
    double TimeOfImpact,
    Vector2D Normal,
    Aabb MovingBoundsAtImpact);
