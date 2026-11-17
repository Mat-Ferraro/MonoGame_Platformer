using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;

namespace PlatformerSandbox.Simulation.Collision.Resolution;

/// <summary>
/// Canonicalizes a representationally tiny overlap at the TOP of a static
/// floor. This is not collision penetration recovery: it cannot fix a real
/// intersection, a side/ceiling overlap, or an ambiguous corner. The strict
/// sweep's initial-overlap validation remains authoritative.
/// </summary>
public static class StaticFloorPrecisionAlignment
{
    public static Aabb Align(Aabb body, IReadOnlyList<StaticAabbCollider> colliders)
    {
        if (!body.IsValid)
            throw new ArgumentException("Body must be a valid AABB.", nameof(body));
        ArgumentNullException.ThrowIfNull(colliders);

        double requiredBottom = body.Bottom;
        bool found = false;
        foreach (StaticAabbCollider collider in colliders)
        {
            ArgumentNullException.ThrowIfNull(collider);
            if (!AabbCollisionDetector.Overlaps(body, collider.Bounds))
                continue;

            // A legitimate top support means a tiny positive Y intrusion,
            // with meaningful horizontal overlap and the body above the floor.
            // Side/ceiling contacts and genuine penetrations are intentionally
            // NOT normalized here.
            double intrusion = collider.Bounds.Top - body.Bottom;
            double precisionBudget = Math.Min(1e-10,
                32.0 * Math.Max(Ulp(body.Bottom), Ulp(collider.Bounds.Top)));
            if (!(intrusion > 0.0 && intrusion <= precisionBudget) ||
                body.Top <= collider.Bounds.Top)
                return body;

            requiredBottom = Math.Max(requiredBottom, collider.Bounds.Top);
            found = true;
        }
        if (!found) return body;

        // Use a literal surface coordinate, not an incrementally accumulated
        // displacement, so the resulting contact is exactly face-aligned.
        Aabb aligned = new(
            new Vector2D(body.Left, requiredBottom),
            new Vector2D(body.Right, requiredBottom + body.Height));

        // A nearby wall or second floor may make the proposed alignment
        // ambiguous; never canonicalize if ANY static overlap would remain.
        foreach (StaticAabbCollider collider in colliders)
            if (AabbCollisionDetector.Overlaps(aligned, collider.Bounds))
                return body;

        return aligned;
    }

    private static double Ulp(double value)
    {
        if (!double.IsFinite(value)) return 0.0;
        return Math.Max(Math.BitIncrement(value) - value,
            value - Math.BitDecrement(value));
    }
}
