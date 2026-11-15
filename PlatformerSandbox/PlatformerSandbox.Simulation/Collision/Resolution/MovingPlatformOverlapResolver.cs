using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Simulation.Collision.Resolution;

/// <summary>
/// Explicit, bounded correction for a body that begins a motor interval already
/// overlapping a moving platform. This is NOT part of the collision detector:
/// continuous sweeps remain strict and still report invalid starting states.
/// Candidates are minimum translations onto one of the platform's four faces,
/// ordered by correction distance, then cardinal direction for exact ties.
/// A correction is rejected if it exceeds the budget or intersects static world.
/// </summary>
public static class MovingPlatformOverlapResolver
{
    public const double DefaultMaximumCorrection = 0.125;

    /// <summary>
    /// Align only a binary64-sized horizontal SIDE overhang with a moving
    /// platform at a completed motion boundary, including when the vertical
    /// intervals are within the small correction budget but only touching. A corner graze against a
    /// vertical lift can otherwise become a strictly positive AABB overlap
    /// when independently evaluated world coordinates differ by a few ULPs.
    ///
    /// This is NOT a starting-penetration recovery or a general depenetrator:
    /// the caller must still validate its starting world before moving. We
    /// only adjust X (never height, velocity, or the simulation clock), only
    /// when the horizontal separation is at most the explicitly bounded
    /// precision budget, and only if the aligned box is valid against all
    /// other geometry. Larger overlaps are intentionally left for the strict
    /// validator to reject.
    /// </summary>
    public static Aabb AlignPrecisionSideOverlap(
        Aabb body, double worldTime,
        IReadOnlyList<MovingPlatformTrajectory> movingPlatforms,
        IReadOnlyList<StaticAabbCollider> staticObstacles,
        double maximumCorrection = 1e-9)
    {
        if (!body.IsValid || !double.IsFinite(worldTime) || worldTime < 0.0)
            throw new ArgumentOutOfRangeException(nameof(body));
        ArgumentNullException.ThrowIfNull(movingPlatforms);
        ArgumentNullException.ThrowIfNull(staticObstacles);
        if (!double.IsFinite(maximumCorrection) || maximumCorrection <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(maximumCorrection));

        // Stable ID ordering makes the result independent of collider-list order.
        foreach (MovingPlatformTrajectory candidate in movingPlatforms.OrderBy(p => p.Id))
        {
            Aabb moving = candidate.Evaluate(worldTime).Bounds;
            // At a side/underside CORNER, a vertical face contact is not yet
            // a positive-area Y overlap. The elevator may be only a few ULPs
            // above the head, while the character's X side differs from the
            // elevator face by equally tiny rounding error. Consider that
            // precisely bounded Y proximity *before* a false underside event
            // is scheduled. Far-away geometry is not eligible for alignment.
            if (!(body.Left < moving.Right && body.Right > moving.Left) ||
                body.Top < moving.Bottom - maximumCorrection ||
                body.Bottom > moving.Top + maximumCorrection)
                continue;

            // Choose an actual *outward* horizontal face, not the width of
            // the overlapping interval (which can be misleading in containment).
            double moveLeft = body.Right - moving.Left;
            double moveRight = moving.Right - body.Left;
            bool canAlignLeft = moveLeft > 0.0 && moveLeft <= maximumCorrection;
            bool canAlignRight = moveRight > 0.0 && moveRight <= maximumCorrection;
            if (!canAlignLeft && !canAlignRight)
                continue;

            // Prefer the smaller face correction; exact ties prefer left.
            bool alignLeft = canAlignLeft &&
                (!canAlignRight || moveLeft <= moveRight);
            double left = alignLeft ? moving.Left - body.Width : moving.Right;
            double right = alignLeft ? moving.Left : moving.Right + body.Width;
            Aabb aligned = new(
                new Vector2D(left, body.Bottom),
                new Vector2D(right, body.Top));

            foreach (StaticAabbCollider obstacle in staticObstacles)
                if (AabbCollisionDetector.Overlaps(aligned, obstacle.Bounds))
                    throw new InvalidOperationException(
                        $"Precision side alignment against moving platform {candidate.Id} " +
                        $"would penetrate static collider {obstacle.Id}.");
            foreach (MovingPlatformTrajectory other in movingPlatforms)
                if (other.Id != candidate.Id && AabbCollisionDetector.Overlaps(
                        aligned, other.Evaluate(worldTime).Bounds))
                    throw new InvalidOperationException(
                        $"Precision side alignment against moving platform {candidate.Id} " +
                        $"would penetrate moving collider {other.Id}.");
            body = aligned;
        }
        return body;
    }

    public static MovingPlatformOverlapCorrection? Resolve(
        Aabb body, MovingPlatformSample platform,
        IReadOnlyList<StaticAabbCollider> staticObstacles,
        double maximumCorrection = DefaultMaximumCorrection)
    {
        if (!body.IsValid || !platform.Bounds.IsValid)
            throw new ArgumentException("Valid body and platform bounds are required.");
        ArgumentNullException.ThrowIfNull(staticObstacles);
        if (!double.IsFinite(maximumCorrection) || maximumCorrection <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(maximumCorrection));
        if (!AabbCollisionDetector.Overlaps(body, platform.Bounds))
            return null;

        Aabb floor = platform.Bounds;
        // When depths tie, the fixed order is stable: horizontal before vertical.
        (Vector2D displacement, Vector2D normal)[] candidates =
        [
            (new Vector2D(floor.Left - body.Right, 0), new Vector2D(-1, 0)),
            (new Vector2D(floor.Right - body.Left, 0), new Vector2D(1, 0)),
            (new Vector2D(0, floor.Bottom - body.Top), new Vector2D(0, -1)),
            (new Vector2D(0, floor.Top - body.Bottom), new Vector2D(0, 1))
        ];
        // Sort by magnitude, using the original index as a deterministic tie break.
        foreach (var candidate in candidates.OrderBy(c =>
                     Math.Abs(c.displacement.X) + Math.Abs(c.displacement.Y)))
        {
            double depth = Math.Abs(candidate.displacement.X) +
                           Math.Abs(candidate.displacement.Y);
            if (depth > maximumCorrection)
                break;
            Aabb corrected = body.Translated(candidate.displacement);
            if (staticObstacles.Any(obstacle =>
                AabbCollisionDetector.Overlaps(corrected, obstacle.Bounds)))
                continue;
            return new MovingPlatformOverlapCorrection(
                platform.PlatformId, corrected, candidate.normal, depth);
        }
        throw new InvalidOperationException(
            $"Moving platform {platform.PlatformId} overlaps the character. " +
            $"No nonpenetrating correction within {maximumCorrection:R} world units exists. " +
            $"Body=[{body.Left:R},{body.Bottom:R},{body.Right:R},{body.Top:R}], " +
            $"platform=[{floor.Left:R},{floor.Bottom:R},{floor.Right:R},{floor.Top:R}].");
    }
}

public readonly record struct MovingPlatformOverlapCorrection(
    int PlatformId, Aabb Bounds, Vector2D Normal, double CorrectionDistance);