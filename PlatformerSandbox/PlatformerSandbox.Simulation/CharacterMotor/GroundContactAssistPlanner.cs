using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Simulation.CharacterMotor;

public readonly record struct GroundContactAssistPlan(
    Aabb Bounds, CollisionAssistEvent Event);

/// <summary>
/// Optional, explicit frame-boundary gameplay assistance. Unlike collision
/// precision repair, these are visibly-sized, designer-controlled shifts.
/// Safety is verified along BOTH legs of every correction, against the entire
/// static and moving world. No arbitrary overlap/depenetration is allowed.
/// </summary>
public static class GroundContactAssistPlanner
{
    public static GroundContactAssistPlan? TryStepUp(
        Aabb body, Vector2D velocity, HorizontalDirection direction,
        CollisionAssistancePolicy policy,
        IReadOnlyList<StaticAabbCollider> staticColliders,
        IReadOnlyList<MovingPlatformTrajectory> movingPlatforms,
        double worldTimeSeconds)
    {
        Validate(policy, staticColliders, movingPlatforms, worldTimeSeconds);
        if (!policy.GroundStepEnabled || velocity.Y > 0.0 ||
            direction == HorizontalDirection.Neutral)
            return null;
        if (direction is not (HorizontalDirection.Left or HorizontalDirection.Right))
            throw new ArgumentOutOfRangeException(nameof(direction));
        // Require actual, exact static support; climbing while unsupported is
        // a separate ability and cannot reuse a grounded micro-step policy.
        if (!staticColliders.Any(c => body.Bottom == c.Bounds.Top &&
            body.Right > c.Bounds.Left && body.Left < c.Bounds.Right))
            return null;
        GroundContactAssistPlan? selected = null;
        double minRise = double.PositiveInfinity;
        int selectedId = int.MaxValue;
        foreach (StaticAabbCollider obstacle in staticColliders)
        {
            Aabb wall = obstacle.Bounds;
            double rise = wall.Top - body.Bottom;
            if (!(rise > 0.0) || rise > policy.GroundStepHeightUnits ||
                wall.Bottom > body.Bottom || wall.Top >= body.Top)
                continue;
            // At, or immediately before, the wall face. A precision-sized
            // external gap may be assisted, but never an initial penetration.
            bool touches = direction == HorizontalDirection.Right
                ? body.Right <= wall.Left && wall.Left - body.Right <= 1e-8
                : body.Left >= wall.Right && body.Left - wall.Right <= 1e-8;
            if (!touches) continue;

            // The first leg is straight up the outside of the wall; the
            // second is a small forward placement ON its top, not through it.
            double toe = Math.Min(policy.GroundStepInwardUnits, body.Width / 8.0);
            if (!(toe > 0.0)) continue;
            double targetLeft = direction == HorizontalDirection.Right
                ? wall.Left + toe - body.Width : wall.Right - toe;
            Aabb raised = new(
                new Vector2D(body.Left, wall.Top),
                new Vector2D(body.Right, wall.Top + body.Height));
            Aabb target = new(
                new Vector2D(targetLeft, wall.Top),
                new Vector2D(targetLeft + body.Width, wall.Top + body.Height));
            if (!(target.Left < wall.Right && target.Right > wall.Left))
                continue;
            if (!ClearPath(body, raised, staticColliders,
                    movingPlatforms, worldTimeSeconds) ||
                !ClearPath(raised, target, staticColliders,
                    movingPlatforms, worldTimeSeconds))
                continue;
            if (rise < minRise || (rise == minRise && obstacle.Id < selectedId))
            {
                selected = new GroundContactAssistPlan(target,
                    new CollisionAssistEvent(obstacle.Id, worldTimeSeconds,
                        target.Left - body.Left, rise, CollisionAssistKind.GroundStep));
                minRise = rise;
                selectedId = obstacle.Id;
            }
        }
        return selected;
    }

    public static GroundContactAssistPlan? TryLedgeCatch(
        Aabb body, Vector2D velocity, HorizontalDirection direction,
        CollisionAssistancePolicy policy,
        IReadOnlyList<StaticAabbCollider> staticColliders,
        IReadOnlyList<MovingPlatformTrajectory> movingPlatforms,
        double worldTimeSeconds)
    {
        Validate(policy, staticColliders, movingPlatforms, worldTimeSeconds);
        // Must deliberately steer toward a narrowly missed ledge.
        // Neutral magnetism and automatic catch while rising are disallowed.
        if (!policy.LedgeCatchEnabled || velocity.Y >= 0.0 ||
            direction == HorizontalDirection.Neutral) return null;
        if (direction is not (HorizontalDirection.Left or HorizontalDirection.Right))
            throw new ArgumentOutOfRangeException(nameof(direction));
        GroundContactAssistPlan? best = null;
        double bestDistance = double.PositiveInfinity;
        int bestId = int.MaxValue;
        double toe = Math.Min(body.Width / 16.0, policy.LedgeCatchHorizontalUnits / 4.0);
        if (toe <= 0.0) return null;
        foreach (StaticAabbCollider floor in staticColliders)
            Consider(floor.Id, floor.Bounds, 0.0);
        foreach (MovingPlatformTrajectory platform in movingPlatforms)
        {
            MovingPlatformSample sample = platform.Evaluate(worldTimeSeconds);
            Consider(platform.Id, sample.Bounds, sample.Velocity.Y);
        }

        void Consider(int id, Aabb floor, double floorVelocityY)
        {
            // No catch when rising relative to the target surface, including
            // an elevator that is descending faster than the character.
            if (!(velocity.Y - floorVelocityY < 0.0)) return;
            double height = body.Bottom - floor.Top;
            if (height < 0.0 || height > policy.LedgeCatchVerticalUnits)
                return;
            // The body must genuinely have missed the edge horizontally.
            double left;
            if (direction == HorizontalDirection.Right &&
                body.Right <= floor.Left)
                left = floor.Left + toe - body.Width;
            else if (direction == HorizontalDirection.Left &&
                body.Left >= floor.Right)
                left = floor.Right - toe;
            else return;
            double delta = left - body.Left;
            if (!double.IsFinite(delta) ||
                Math.Abs(delta) > policy.LedgeCatchHorizontalUnits ||
                delta * (int)direction <= 0.0)
                return;
            Aabb target = new(
                new Vector2D(left, body.Bottom),
                new Vector2D(left + body.Width, body.Top));
            if (!ClearPath(body, target, staticColliders,
                    movingPlatforms, worldTimeSeconds)) return;
            double distance = Math.Abs(delta);
            if (distance < bestDistance ||
                (distance == bestDistance && id < bestId))
            {
                best = new GroundContactAssistPlan(target,
                    new CollisionAssistEvent(id, worldTimeSeconds,
                        delta, 0.0, CollisionAssistKind.LedgeCatch));
                bestDistance = distance;
                bestId = id;
            }
        }
        return best;
    }

    private static bool ClearPath(Aabb start, Aabb end,
        IReadOnlyList<StaticAabbCollider> statics,
        IReadOnlyList<MovingPlatformTrajectory> moving,
        double time)
    {
        Aabb span = new(new Vector2D(Math.Min(start.Left, end.Left),
            Math.Min(start.Bottom, end.Bottom)),
            new Vector2D(Math.Max(start.Right, end.Right),
            Math.Max(start.Top, end.Top)));
        foreach (StaticAabbCollider obstacle in statics)
            if (AabbCollisionDetector.Overlaps(span, obstacle.Bounds))
                return false;
        foreach (MovingPlatformTrajectory platform in moving)
            if (AabbCollisionDetector.Overlaps(span, platform.Evaluate(time).Bounds))
                return false;
        return true;
    }

    private static void Validate(CollisionAssistancePolicy policy,
        IReadOnlyList<StaticAabbCollider> statics,
        IReadOnlyList<MovingPlatformTrajectory> moving, double time)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(statics);
        ArgumentNullException.ThrowIfNull(moving);
        if (!double.IsFinite(time) || time < 0.0)
            throw new ArgumentOutOfRangeException(nameof(time));
    }
}
