using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Contacts;

namespace PlatformerSandbox.Simulation.Collision.Detection;

/// <summary>
/// Deterministic, framework-independent AABB queries against static obstacles.
/// Does NOT resolve motion, perform assistance, or integrate acceleration.
/// Sweep expects one LINEAR displacement segment; curved analytical motion
/// requires later event-aware continuous collision queries.
/// </summary>
public static class AabbCollisionDetector
{
    /// <summary>Strict positive-area overlap; mere boundary contact is false.</summary>
    public static bool Overlaps(Aabb moving, Aabb obstacle)
    {
        if (!moving.IsValid)
            throw new ArgumentException("Moving box must be valid.", nameof(moving));
        if (!obstacle.IsValid)
            throw new ArgumentException("Obstacle box must be valid.", nameof(obstacle));
        return moving.Left < obstacle.Right && moving.Right > obstacle.Left &&
            moving.Bottom < obstacle.Top && moving.Top > obstacle.Bottom;
    }

    /// <summary>
    /// Reports the minimum axis-aligned displacement to separate a *penetrating*
    /// moving box from one static obstacle. Equal depths prefer X, equal signs
    /// prefer the positive normal. This is a QUERY; it does not move the box.
    /// </summary>
    public static OverlapContact? FindOverlap(Aabb moving, StaticAabbCollider obstacle)
    {
        ArgumentNullException.ThrowIfNull(obstacle);
        Aabb target = obstacle.Bounds;
        if (!Overlaps(moving, target))
            return null;

        double moveLeft = moving.Right - target.Left;    // shift moving -X
        double moveRight = target.Right - moving.Left;   // shift moving +X
        double moveDown = moving.Top - target.Bottom;   // shift moving -Y
        double moveUp = target.Top - moving.Bottom;     // shift moving +Y

        double depthX = Math.Min(moveLeft, moveRight);
        double depthY = Math.Min(moveDown, moveUp);
        if (depthX <= depthY)
        {
            Vector2D normal = moveRight <= moveLeft ? new(1, 0) : new(-1, 0);
            return new OverlapContact(obstacle.Id, normal, depthX);
        }

        Vector2D yNormal = moveUp <= moveDown ? new(0, 1) : new(0, -1);
        return new OverlapContact(obstacle.Id, yNormal, depthY);
    }

    /// <summary>
    /// Sweeps a valid moving AABB across one linear displacement. Initial
    /// penetration is deliberately NOT handled here: call FindOverlap first.
    /// A boundary contact at t=0 counts only when moving inward. A face contact
    /// at t=1 counts. A corner-only grazing event with zero overlap-time does
    /// not count. Simultaneous X/Y entry prefers the X normal.
    /// </summary>
    public static SweepHit? Sweep(Aabb moving, Vector2D displacement, StaticAabbCollider obstacle)
    {
        ArgumentNullException.ThrowIfNull(obstacle);
        Aabb target = obstacle.Bounds;
        if (Overlaps(moving, target))
            return null;
        if (displacement == Vector2D.Zero)
            return null;

        if (!GetAxisTimes(moving.Left, moving.Right, target.Left, target.Right,
                displacement.X, out double xEntry, out double xExit))
            return null;
        if (!GetAxisTimes(moving.Bottom, moving.Top, target.Bottom, target.Top,
                displacement.Y, out double yEntry, out double yExit))
            return null;

        double entry = Math.Max(xEntry, yEntry);
        double exit = Math.Min(xExit, yExit);

        // exit == entry is an isolated corner graze, not an inward collision.
        if (!(entry < exit) || exit <= 0.0 || entry > 1.0 || entry < 0.0)
            return null;

        Vector2D normal = xEntry >= yEntry
            ? new Vector2D(displacement.X > 0.0 ? -1.0 : 1.0, 0.0)
            : new Vector2D(0.0, displacement.Y > 0.0 ? -1.0 : 1.0);

        return new SweepHit(obstacle.Id, entry, normal, moving.Translated(displacement * entry));
    }

    /// <summary>
    /// Returns earliest swept contact. EXACT tied times choose lowest stable
    /// collider ID regardless of enumeration order. Duplicate IDs are rejected.
    /// No hidden epsilon is applied; tolerance belongs to a later motor policy.
    /// </summary>
    public static SweepHit? SweepFirst(
        Aabb moving,
        Vector2D displacement,
        IReadOnlyList<StaticAabbCollider> obstacles)
    {
        ArgumentNullException.ThrowIfNull(obstacles);
        if (!moving.IsValid)
            throw new ArgumentException("Moving box must be valid.", nameof(moving));

        SweepHit? best = null;
        HashSet<int> ids = new();
        foreach (StaticAabbCollider obstacle in SpatialColliderSet.Select(obstacles, () => SpatialBounds.Swept(moving, displacement)))
        {
            ArgumentNullException.ThrowIfNull(obstacle);
            if (!ids.Add(obstacle.Id))
                throw new ArgumentException("Obstacle IDs must be unique.", nameof(obstacles));

            SweepHit? hit = Sweep(moving, displacement, obstacle);
            if (hit is null)
                continue;

            if (best is null || hit.Value.TimeOfImpact < best.Value.TimeOfImpact ||
                (hit.Value.TimeOfImpact == best.Value.TimeOfImpact &&
                 hit.Value.ColliderId < best.Value.ColliderId))
                best = hit;
        }
        return SpatialColliderSet.Verify(obstacles, best, () => SweepFirst(moving, displacement, obstacles));
    }

    private static bool GetAxisTimes(
        double movingMin, double movingMax,
        double targetMin, double targetMax,
        double distance,
        out double entry, out double exit)
    {
        if (distance == 0.0)
        {
            // Stationary axis must ALREADY have positive overlap. A face
            // touch alone is insufficient for a tangential collision.
            entry = double.NegativeInfinity;
            exit = double.PositiveInfinity;
            return movingMax > targetMin && movingMin < targetMax;
        }

        if (distance > 0.0)
        {
            entry = (targetMin - movingMax) / distance;
            exit = (targetMax - movingMin) / distance;
        }
        else
        {
            entry = (targetMax - movingMin) / distance;
            exit = (targetMin - movingMax) / distance;
        }
        return true;
    }
}
