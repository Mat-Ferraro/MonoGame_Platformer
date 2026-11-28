using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Simulation.Collision.Detection;

/// <summary>
/// Continuous AABB search along a fully piecewise analytical jump reference.
/// All face-crossing times are isolated by deterministic bisection on known
/// monotone intervals (not by movement time-step sampling). Handles power-curve
/// apex hang, release, fall, horizontal reversals, and scheduled input events.
/// Only the FIRST event is returned; post-impact movement requires a fresh
/// motor motion segment and is explicitly outside this detector's responsibility.
/// </summary>
public static class JumpProfileCollisionDetector
{
    private readonly record struct BoundaryEvent(double Time, Vector2D Normal);

    public static JumpProfileSweepHit? SweepFirst(
        Aabb initialBounds,
        JumpReferenceCollisionPlan plan,
        IReadOnlyList<StaticAabbCollider> obstacles,
        double contactAlignmentTolerance = 1e-8)
    {
        if (!initialBounds.IsValid)
            throw new ArgumentException("Moving AABB must be valid.", nameof(initialBounds));
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(obstacles);
        if (!double.IsFinite(contactAlignmentTolerance) || contactAlignmentTolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(contactAlignmentTolerance));

        var ids = new HashSet<int>();
        JumpProfileSweepHit? first = null;
        foreach (StaticAabbCollider collider in SpatialColliderSet.Select(obstacles, () =>
        {
            var bounds=SpatialBounds.From(initialBounds);
            foreach(double time in plan.MonotonicBreakpoints)
            {
                var sample=plan.Reference.Evaluate(time);
                bounds=bounds.Union(SpatialBounds.From(initialBounds.Translated(new Vector2D(sample.HorizontalPosition,sample.VerticalDisplacement))));
            }
            return bounds;
        }, contactAlignmentTolerance))
        {
            ArgumentNullException.ThrowIfNull(collider);
            if (!ids.Add(collider.Id))
                throw new ArgumentException("Collider IDs must be unique.", nameof(obstacles));
            if (AabbCollisionDetector.Overlaps(initialBounds, collider.Bounds))
                throw new InvalidOperationException(
                    $"Moving AABB starts penetrating collider {collider.Id}. Resolve overlap explicitly.");

            JumpProfileSweepHit? hit = SweepOne(initialBounds, plan, collider, contactAlignmentTolerance);
            if (hit is { } next &&
                (first is null || next.TimeSeconds < first.Value.TimeSeconds ||
                 (next.TimeSeconds == first.Value.TimeSeconds && next.ColliderId < first.Value.ColliderId)))
                first = next;
        }
        return SpatialColliderSet.Verify(obstacles, first, () => SweepFirst(initialBounds, plan, obstacles, contactAlignmentTolerance));
    }

    private static JumpProfileSweepHit? SweepOne(
        Aabb moving,
        JumpReferenceCollisionPlan plan,
        StaticAabbCollider collider,
        double tolerance)
    {
        Aabb target = collider.Bounds;
        var events = new List<BoundaryEvent>();
        // Overlap requires translation to be STRICTLY between two thresholds
        // on each axis. Every crossing partitions its truth value in time.
        AddFaceCrossings(events, plan, true,
            target.Left - moving.Right, new Vector2D(-1, 0));
        AddFaceCrossings(events, plan, true,
            target.Right - moving.Left, new Vector2D(1, 0));
        AddFaceCrossings(events, plan, false,
            target.Bottom - moving.Top, new Vector2D(0, -1));
        AddFaceCrossings(events, plan, false,
            target.Top - moving.Bottom, new Vector2D(0, 1));

        var times = new List<double> { 0.0, plan.TotalDurationSeconds };
        times.AddRange(plan.MonotonicBreakpoints);
        foreach (BoundaryEvent evt in events)
            times.Add(evt.Time);
        times.Sort();
        var unique = new List<double>(times.Count);
        foreach (double time in times)
            if (unique.Count == 0 || unique[^1] != time)
                unique.Add(time);

        for (int i = 0; i + 1 < unique.Count; i++)
        {
            double begin = unique[i];
            double end = unique[i + 1];
            if (begin >= end)
                continue;
            double middle = begin + (end - begin) * 0.5;
            Aabb midpointBounds = BoundsAt(moving, plan.Reference.Evaluate(middle));
            if (!AabbCollisionDetector.Overlaps(midpointBounds, target))
                continue;

            Vector2D? normal = SelectEnteringNormal(events, plan, begin, middle);
            if (normal is null)
                throw new InvalidOperationException(
                    "An overlapping interval has no representable entering face. " +
                    "The trajectory or geometry exceeds reliable collision-search precision.");
            return MakeHit(moving, collider, plan.Reference.Evaluate(begin), begin, normal.Value, tolerance);
        }

        // Consistent with previous sweep APIs: an end-face impact counts if
        // motion still points inward, but a corner-only endpoint graze does not.
        double finish = plan.TotalDurationSeconds;
        JumpTravelSample terminal = plan.Reference.Evaluate(finish);
        foreach (BoundaryEvent evt in events)
        {
            if (evt.Time != finish)
                continue;
            Vector2D velocity = new(terminal.HorizontalVelocity, terminal.VerticalVelocity);
            if (velocity.X * evt.Normal.X + velocity.Y * evt.Normal.Y >= 0.0)
                continue;
            Aabb finalBounds = BoundsAt(moving, terminal);
            bool otherOverlaps = evt.Normal.X != 0.0
                ? finalBounds.Bottom < target.Top && finalBounds.Top > target.Bottom
                : finalBounds.Left < target.Right && finalBounds.Right > target.Left;
            if (otherOverlaps)
                return MakeHit(moving, collider, terminal, finish, evt.Normal, tolerance);
        }
        return null;
    }

    private static Aabb BoundsAt(Aabb start, JumpTravelSample sample) =>
        start.Translated(new Vector2D(sample.HorizontalPosition, sample.VerticalDisplacement));

    private static void AddFaceCrossings(
        List<BoundaryEvent> events,
        JumpReferenceCollisionPlan plan,
        bool horizontal,
        double targetDisplacement,
        Vector2D normal)
    {
        IReadOnlyList<double> times = plan.MonotonicBreakpoints;
        for (int i = 0; i + 1 < times.Count; i++)
        {
            double start = times[i];
            double end = times[i + 1];
            double p0 = AxisPosition(plan.Reference.Evaluate(start), horizontal);
            double p1 = AxisPosition(plan.Reference.Evaluate(end), horizontal);
            if (p0 == p1)
            {
                // Constant position on this interval is NOT a crossing.
                // A root at an adjacent interval boundary is still found there.
                continue;
            }
            if (targetDisplacement < Math.Min(p0, p1) ||
                targetDisplacement > Math.Max(p0, p1))
                continue;

            double root = p0 == targetDisplacement ? start
                : p1 == targetDisplacement ? end
                : FindCrossing(plan.Reference, horizontal, targetDisplacement, start, end, p0, p1);
            events.Add(new BoundaryEvent(root, normal));
        }
    }

    private static double AxisPosition(JumpTravelSample sample, bool horizontal) =>
        horizontal ? sample.HorizontalPosition : sample.VerticalDisplacement;

    private static double FindCrossing(
        JumpTravelTrajectory reference,
        bool horizontal,
        double threshold,
        double start, double end, double startValue, double endValue)
    {
        double low = start;
        double high = end;
        bool increasing = startValue < endValue;
        // 128 subdivisions suffice for the binary64 exponent range. Stop only
        // when the interval cannot be subdivided further in double precision;
        // there is no arbitrary time-sampling step size.
        for (int iteration = 0; iteration < 128; iteration++)
        {
            double mid = low + (high - low) * 0.5;
            if (mid == low || mid == high)
                break;
            double position = AxisPosition(reference.Evaluate(mid), horizontal);
            if (position == threshold)
                return mid;
            if ((position < threshold) == increasing)
                low = mid;
            else
                high = mid;
        }
        double lowError = Math.Abs(AxisPosition(reference.Evaluate(low), horizontal) - threshold);
        double highError = Math.Abs(AxisPosition(reference.Evaluate(high), horizontal) - threshold);
        return lowError <= highError ? low : high;
    }

    private static Vector2D? SelectEnteringNormal(
        List<BoundaryEvent> events,
        JumpReferenceCollisionPlan plan,
        double entryTime,
        double interiorTime)
    {
        JumpTravelSample start = plan.Reference.Evaluate(entryTime);
        JumpTravelSample interior = plan.Reference.Evaluate(interiorTime);
        double dx = interior.HorizontalPosition - start.HorizontalPosition;
        double dy = interior.VerticalDisplacement - start.VerticalDisplacement;
        Vector2D? best = null;
        foreach (BoundaryEvent evt in events)
        {
            if (evt.Time != entryTime)
                continue;
            double inward = dx * evt.Normal.X + dy * evt.Normal.Y;
            if (!(inward < 0.0))
                continue;
            // X first for exact simultaneous corner entry, consistent with
            // the prior detector's documented tie rule.
            if (best is null || (evt.Normal.X != 0.0 && best.Value.X == 0.0))
                best = evt.Normal;
        }
        return best;
    }

    private static JumpProfileSweepHit MakeHit(
        Aabb moving,
        StaticAabbCollider collider,
        JumpTravelSample sample,
        double time,
        Vector2D normal,
        double tolerance)
    {
        Aabb raw = BoundsAt(moving, sample);
        Aabb target = collider.Bounds;
        Vector2D adjustment = normal switch
        {
            { X: -1.0 } => new Vector2D(target.Left - raw.Right, 0),
            { X: 1.0 } => new Vector2D(target.Right - raw.Left, 0),
            { Y: -1.0 } => new Vector2D(0, target.Bottom - raw.Top),
            { Y: 1.0 } => new Vector2D(0, target.Top - raw.Bottom),
            _ => throw new InvalidOperationException("Unsupported collision normal.")
        };
        if (Math.Abs(adjustment.X) + Math.Abs(adjustment.Y) > tolerance)
            throw new InvalidOperationException(
                "Contact position exceeds explicit face-alignment tolerance. " +
                "Reduce coordinate magnitude or choose a representable tolerance.");

        return new JumpProfileSweepHit(
            collider.Id, time, normal, raw.Translated(adjustment),
            new Vector2D(sample.HorizontalVelocity, sample.VerticalVelocity),
            new Vector2D(sample.HorizontalAcceleration, sample.VerticalAcceleration));
    }
}
