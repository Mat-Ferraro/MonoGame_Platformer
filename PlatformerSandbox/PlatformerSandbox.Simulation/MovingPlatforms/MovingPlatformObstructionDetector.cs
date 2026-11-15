using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Diagnostics;

namespace PlatformerSandbox.Simulation.MovingPlatforms;

/// <summary>
/// Continuous, analytical world-obstruction check for immutable linear
/// platform legs. Each platform's turn is a named boundary. On each leg,
/// relative motion is linear and the strict AABB slab inequalities describe
/// the complete positive-area overlap interval. No timestep sampling, contact
/// epsilon, physics impulse or platform clock mutation is involved.
/// </summary>
public static class MovingPlatformObstructionDetector
{
    public static PlatformObstruction? FindFirst(double worldStartSeconds,
        double durationSeconds, IReadOnlyList<StaticAabbCollider> staticColliders,
        IReadOnlyList<MovingPlatformTrajectory> movingPlatforms,
        int maxPlatformLegs = 64)
    {
        ArgumentNullException.ThrowIfNull(staticColliders);
        ArgumentNullException.ThrowIfNull(movingPlatforms);
        if (!double.IsFinite(worldStartSeconds) || worldStartSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(worldStartSeconds));
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0 ||
            !double.IsFinite(worldStartSeconds + durationSeconds) ||
            (durationSeconds > 0.0 && worldStartSeconds + durationSeconds <= worldStartSeconds))
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (maxPlatformLegs < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPlatformLegs));

        var ids = new HashSet<int>();
        foreach (StaticAabbCollider collider in staticColliders)
        {
            ArgumentNullException.ThrowIfNull(collider);
            if (!ids.Add(collider.Id))
                throw new ArgumentException("Duplicate collider IDs.", nameof(staticColliders));
        }
        foreach (MovingPlatformTrajectory platform in movingPlatforms)
        {
            ArgumentNullException.ThrowIfNull(platform);
            if (!ids.Add(platform.Id))
                throw new ArgumentException("Duplicate world collider IDs.", nameof(movingPlatforms));
        }

        PlatformObstruction? first = null;
        for (int i = 0; i < movingPlatforms.Count; ++i)
        {
            MovingPlatformTrajectory moving = movingPlatforms[i];
            foreach (StaticAabbCollider obstacle in staticColliders)
            {
                double? time = FirstOverlapTime(moving, null, obstacle.Bounds,
                    worldStartSeconds, durationSeconds, maxPlatformLegs);
                if (time is not null)
                    first = Prefer(first, new PlatformObstruction(
                        moving.Id, obstacle.Id, false, time.Value));
            }
            for (int j = i + 1; j < movingPlatforms.Count; ++j)
            {
                MovingPlatformTrajectory other = movingPlatforms[j];
                double? time = FirstOverlapTime(moving, other, default,
                    worldStartSeconds, durationSeconds, maxPlatformLegs);
                if (time is not null)
                {
                    int low = Math.Min(moving.Id, other.Id);
                    int high = Math.Max(moving.Id, other.Id);
                    first = Prefer(first, new PlatformObstruction(
                        low, high, true, time.Value));
                }
            }
        }
        return first;
    }

    private static PlatformObstruction Prefer(
        PlatformObstruction? current, PlatformObstruction candidate)
    {
        if (current is null ||
            candidate.WorldTimeSeconds < current.Value.WorldTimeSeconds ||
            (candidate.WorldTimeSeconds == current.Value.WorldTimeSeconds &&
             (candidate.MovingPlatformId < current.Value.MovingPlatformId ||
              (candidate.MovingPlatformId == current.Value.MovingPlatformId &&
               candidate.OtherColliderId < current.Value.OtherColliderId))))
            return candidate;
        return current.Value;
    }

    private static double? FirstOverlapTime(
        MovingPlatformTrajectory moving, MovingPlatformTrajectory? other,
        Aabb staticBounds, double worldStart, double duration, int maxLegs)
    {
        double progressed = 0.0;
        int legs = 0;
        while (true)
        {
            double now = worldStart + progressed;
            MovingPlatformSample a = moving.Evaluate(now);
            Aabb bBounds = other?.Evaluate(now).Bounds ?? staticBounds;
            if (AabbCollisionDetector.Overlaps(a.Bounds, bBounds))
                return now;
            if (progressed >= duration) return null;
            if (++legs > maxLegs)
                throw new SimulationEventLimitException(
                    "Platform world-obstruction analytical leg budget exceeded.",
                    Array.Empty<SimulationContactTrace>());
            double remaining = duration - progressed;
            double leg = Math.Min(remaining, moving.TimeUntilNextTurn(now));
            if (other is not null)
                leg = Math.Min(leg, other.TimeUntilNextTurn(now));
            if (!(leg > 0.0) || now + leg <= now || progressed + leg <= progressed)
                throw new SimulationNoProgressException(
                    "Platform world-obstruction interval cannot advance.");
            Vector2D relative = a.Velocity -
                (other?.Evaluate(now).Velocity ?? Vector2D.Zero);
            double? hit = FirstStrictOverlapWithinLeg(a.Bounds, bBounds, relative, leg);
            if (hit is double t)
                return now + t;
            progressed = leg == remaining ? duration : progressed + leg;
        }
    }

    /// <summary>
    /// Slab intersection of two boxes undergoing constant relative velocity.
    /// Mere endpoint touching and isolated corner grazing are not an overlap.
    /// </summary>
    private static double? FirstStrictOverlapWithinLeg(
        Aabb a, Aabb b, Vector2D relativeVelocity, double duration)
    {
        if (!Slab(a.Left, a.Right, b.Left, b.Right, relativeVelocity.X,
                out double xEnter, out double xLeave) ||
            !Slab(a.Bottom, a.Top, b.Bottom, b.Top, relativeVelocity.Y,
                out double yEnter, out double yLeave))
            return null;
        double enter = Math.Max(0.0, Math.Max(xEnter, yEnter));
        double leave = Math.Min(duration, Math.Min(xLeave, yLeave));
        return enter < leave ? enter : null;
    }

    private static bool Slab(double aMin, double aMax, double bMin, double bMax,
        double relativeVelocity, out double enter, out double leave)
    {
        enter = double.NegativeInfinity;
        leave = double.PositiveInfinity;
        if (relativeVelocity == 0.0)
            return aMax > bMin && aMin < bMax;
        double t1 = (bMin - aMax) / relativeVelocity;
        double t2 = (bMax - aMin) / relativeVelocity;
        enter = Math.Min(t1, t2);
        leave = Math.Max(t1, t2);
        return true;
    }
}
