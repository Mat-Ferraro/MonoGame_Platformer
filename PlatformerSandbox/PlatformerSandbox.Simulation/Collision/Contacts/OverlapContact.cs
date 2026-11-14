using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Collision.Contacts;

/// <summary>
/// Positive-overlap report only, NOT an applied correction. Normal points
/// from obstacle toward moving box. Along that normal, PenetrationDepth is
/// the minimum translation needed to end overlap on the selected axis.
/// </summary>
public readonly record struct OverlapContact(
    int ColliderId,
    Vector2D Normal,
    double PenetrationDepth)
{
    public Vector2D MinimumSeparation => Normal * PenetrationDepth;
}
