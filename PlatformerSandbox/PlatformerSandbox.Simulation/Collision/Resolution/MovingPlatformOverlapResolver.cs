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
