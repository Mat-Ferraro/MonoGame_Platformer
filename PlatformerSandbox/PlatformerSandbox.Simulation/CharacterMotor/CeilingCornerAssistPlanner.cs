using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// Pure, bounded, deterministic ceiling-corner planner. Runs ONLY after the
/// unified query has confirmed exactly one rising underside impact against
/// static or moving geometry. The contacted platform is sampled at the exact
/// event timestamp. This never bypasses simultaneous contacts or a crush.
/// </summary>
public static class CeilingCornerAssistPlanner
{
    public static Aabb? TryPlan(
        UnifiedCollisionHit hit, HorizontalDirection direction,
        CollisionAssistancePolicy policy,
        IReadOnlyList<StaticAabbCollider> staticColliders,
        IReadOnlyList<MovingPlatformTrajectory> movingPlatforms,
        double worldImpactSeconds)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(staticColliders);
        ArgumentNullException.ThrowIfNull(movingPlatforms);
        if (!policy.CeilingCornersEnabled ||
            hit.Normal.X != 0.0 || hit.Normal.Y >= 0.0 ||
            hit.BodyVelocityAtImpact.Y <= 0.0 ||
            hit.RelativeVelocityAtImpact.Y <= 0.0 ||
            !double.IsFinite(worldImpactSeconds) || worldImpactSeconds < 0.0)
            return null;
        if (direction is not (HorizontalDirection.Left or
            HorizontalDirection.Right or HorizontalDirection.Neutral))
            throw new ArgumentOutOfRangeException(nameof(direction));

        // A moving ceiling is located at the authoritative impact time, not
        // at the start or end of the frame. Require the matching source type.
        StaticAabbCollider? ceiling = hit.IsMovingPlatform ? null :
            staticColliders.FirstOrDefault(c => c.Id == hit.ColliderId);
        MovingPlatformTrajectory? movingCeiling = !hit.IsMovingPlatform ? null :
            movingPlatforms.FirstOrDefault(p => p.Id == hit.ColliderId);
        if (ceiling is null && movingCeiling is null) return null;
        Aabb obstacle = movingCeiling is null ? ceiling!.Bounds :
            movingCeiling.Evaluate(worldImpactSeconds).Bounds;
        Aabb reported = hit.BodyBoundsAtImpact;
        // The collision detector's existing analytical face alignment budget:
        // NOT the user-configured assistance distance.
        if (Math.Abs(reported.Top - obstacle.Bottom) > 1e-8)
            return null;
        Aabb start = new(
            new Vector2D(reported.Left, obstacle.Bottom - reported.Height),
            new Vector2D(reported.Right, obstacle.Bottom));
        if (!(start.Right > obstacle.Left && start.Left < obstacle.Right))
            return null;

        // The correction must point in the requested direction. Neutral input
        // selects the smaller escape (exact ties prefer left). No hidden
        // reversal of player intent occurs under directional input.
        double leftDelta = obstacle.Left - start.Right;
        double rightDelta = obstacle.Right - start.Left;
        Aabb? left = direction == HorizontalDirection.Right ? null :
            TryCandidate(leftDelta);
        Aabb? right = direction == HorizontalDirection.Left ? null :
            TryCandidate(rightDelta);
        if (left is null) return right;
        if (right is null) return left;
        return Math.Abs(leftDelta) <= Math.Abs(rightDelta) ? left : right;

        Aabb? TryCandidate(double delta)
        {
            if (!double.IsFinite(delta) || delta == 0.0 ||
                Math.Abs(delta) > policy.CeilingCornerCorrectionUnits)
                return null;
            // Construct the outgoing face from the exact obstacle edge,
            // rather than adding a floating-point delta to both corners.
            // This preserves exact nonpenetration at the next query.
            Aabb candidate = delta < 0.0
                ? new Aabb(
                    new Vector2D(obstacle.Left - start.Width, start.Bottom),
                    new Vector2D(obstacle.Left, start.Top))
                : new Aabb(
                    new Vector2D(obstacle.Right, start.Bottom),
                    new Vector2D(obstacle.Right + start.Width, start.Top));
            double actualCorrection = candidate.Left - start.Left;
            if (candidate == start ||
                Math.Abs(actualCorrection) > policy.CeilingCornerCorrectionUnits)
                return null;

            // Horizontal path envelope is exact for a constant-Y shift. The
            // target ceiling has no positive Y overlap at its underside.
            Aabb traversed = new(
                new Vector2D(Math.Min(start.Left, candidate.Left), start.Bottom),
                new Vector2D(Math.Max(start.Right, candidate.Right), start.Top));
            foreach (StaticAabbCollider collider in staticColliders)
                if (collider.Id != hit.ColliderId &&
                    AabbCollisionDetector.Overlaps(traversed, collider.Bounds))
                    return null;
            foreach (MovingPlatformTrajectory platform in movingPlatforms)
                if (platform.Id != hit.ColliderId && AabbCollisionDetector.Overlaps(
                    traversed, platform.Evaluate(worldImpactSeconds).Bounds))
                    return null;
            return candidate;
        }
    }
}
