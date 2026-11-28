using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Motion.Integration;

namespace PlatformerSandbox.Simulation.Collision.Detection;

/// <summary>
/// Continuous AABB collision search for ONE interval on which X and Y
/// displacements are each monotone. The caller supplies an analytical
/// function of time; this class isolates face crossings with deterministic
/// bisection, never a straight-line sweep or time-sample approximation.
/// Split the path at every velocity turning point / motion-law event BEFORE
/// calling. A touching face at t=0 only collides if the path enters it.
/// </summary>
public static class AnalyticalAabbCollisionDetector
{
    private readonly record struct FaceEvent(double TimeSeconds, Vector2D Normal);

    public static AnalyticalSweepHit? SweepFirst(
        Aabb initialBounds,
        Func<double, MotionSample2D> evaluate,
        double durationSeconds,
        IReadOnlyList<StaticAabbCollider> obstacles,
        double contactAlignmentTolerance = 1e-8)
    {
        if (!initialBounds.IsValid)
            throw new ArgumentException("Invalid moving bounds.", nameof(initialBounds));
        ArgumentNullException.ThrowIfNull(evaluate);
        ArgumentNullException.ThrowIfNull(obstacles);
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (!double.IsFinite(contactAlignmentTolerance) || contactAlignmentTolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(contactAlignmentTolerance));

        MotionSample2D begin = evaluate(0.0);
        MotionSample2D end = evaluate(durationSeconds);
        if (begin.Position != Vector2D.Zero)
            throw new ArgumentException("The analytical displacement must start at zero.", nameof(evaluate));

        var ids = new HashSet<int>();
        AnalyticalSweepHit? best = null;
        foreach (StaticAabbCollider obstacle in SpatialColliderSet.Select(obstacles, () => SpatialBounds.Swept(initialBounds, end.Position), contactAlignmentTolerance))
        {
            ArgumentNullException.ThrowIfNull(obstacle);
            if (!ids.Add(obstacle.Id))
                throw new ArgumentException("Collider IDs must be unique.", nameof(obstacles));
            if (AabbCollisionDetector.Overlaps(initialBounds, obstacle.Bounds))
                throw new InvalidOperationException(
                    $"Moving body begins penetrating collider {obstacle.Id}.");

            AnalyticalSweepHit? found = SweepOne(initialBounds, obstacle,
                evaluate, begin, end, durationSeconds, contactAlignmentTolerance);
            if (found is { } hit &&
                (best is null || hit.TimeSeconds < best.Value.TimeSeconds ||
                 (hit.TimeSeconds == best.Value.TimeSeconds && hit.ColliderId < best.Value.ColliderId)))
            {
                best = hit;
            }
        }
        return SpatialColliderSet.Verify(obstacles, best, () => SweepFirst(initialBounds, evaluate, durationSeconds, obstacles, contactAlignmentTolerance));
    }

    private static AnalyticalSweepHit? SweepOne(
        Aabb moving, StaticAabbCollider obstacle,
        Func<double, MotionSample2D> evaluate,
        MotionSample2D begin, MotionSample2D end,
        double duration, double tolerance)
    {
        Aabb target = obstacle.Bounds;
        var faces = new List<FaceEvent>();
        AddCrossing(faces, evaluate, false, target.Bottom - moving.Top,
            new Vector2D(0, -1), begin, end, duration);
        AddCrossing(faces, evaluate, false, target.Top - moving.Bottom,
            new Vector2D(0, 1), begin, end, duration);
        AddCrossing(faces, evaluate, true, target.Left - moving.Right,
            new Vector2D(-1, 0), begin, end, duration);
        AddCrossing(faces, evaluate, true, target.Right - moving.Left,
            new Vector2D(1, 0), begin, end, duration);

        var times = new List<double> { 0.0, duration };
        foreach (FaceEvent face in faces) times.Add(face.TimeSeconds);
        times.Sort();
        for (int i = 0; i < times.Count - 1; i++)
        {
            double left = times[i];
            double right = times[i + 1];
            if (!(left < right)) continue;
            double middle = left + (right - left) * 0.5;
            Aabb middleBounds = moving.Translated(evaluate(middle).Position);
            if (!AabbCollisionDetector.Overlaps(middleBounds, target)) continue;

            Vector2D delta = evaluate(middle).Position - evaluate(left).Position;
            Vector2D? normal = null;
            foreach (FaceEvent face in faces)
            {
                if (face.TimeSeconds != left) continue;
                double inward = delta.X * face.Normal.X + delta.Y * face.Normal.Y;
                if (!(inward < 0.0)) continue;
                // X before Y for an exact simultaneous corner entry.
                if (normal is null || (face.Normal.X != 0.0 && normal.Value.X == 0.0))
                    normal = face.Normal;
            }
            if (normal is null)
            {
                // Bisection can isolate a crossing to adjacent binary64 time
                // values, yet the rounded translated AABB can become strictly
                // overlapping one representable instant *before* that crossing
                // timestamp. In that case an exact face.TimeSeconds == left
                // comparison loses the entering face, even though it is still
                // aligned at left to within our explicit contact tolerance.
                // Recover only a geometrically aligned, inward-moving face.
                // Never turn a grazing or separating touch into a collision.
                normal = RecoverEnteringNormal(
                    moving.Translated(evaluate(left).Position), middleBounds,
                    target, evaluate(left).Velocity, evaluate(middle).Velocity,
                    delta, tolerance);
            }
            if (normal is null)
                throw new InvalidOperationException(
                    "An overlapping interval has no inward, face-aligned " +
                    "contact within the configured precision tolerance.");
            return CreateHit(moving, obstacle, evaluate(left), left, normal.Value, tolerance);
        }

        // Report an inward-facing terminal contact, as on the existing sweep
        // detectors. A corner-only graze is never a positive-area collision.
        foreach (FaceEvent face in faces)
        {
            if (face.TimeSeconds != duration) continue;
            double normalVelocity = end.Velocity.X * face.Normal.X +
                                    end.Velocity.Y * face.Normal.Y;
            if (!(normalVelocity < 0.0)) continue;
            Aabb atEnd = moving.Translated(end.Position);
            bool otherAxisOverlaps = face.Normal.X != 0.0
                ? atEnd.Bottom < target.Top && atEnd.Top > target.Bottom
                : atEnd.Left < target.Right && atEnd.Right > target.Left;
            if (otherAxisOverlaps)
                return CreateHit(moving, obstacle, end, duration, face.Normal, tolerance);
        }
        return null;
    }

    /// <summary>
    /// Recover an entry when boundary-time root isolation and rounded AABB
    /// translation disagree by one or more final binary64 bits. This fallback
    /// is constrained by the SAME world-space alignment tolerance as CreateHit;
    /// it does not relax positive-area overlap or invent a collision normal.
    /// X wins exact corner ties, consistent with the primary sweep algorithm.
    /// </summary>
    private static Vector2D? RecoverEnteringNormal(
        Aabb atEntry, Aabb inside, Aabb obstacle,
        Vector2D startVelocity, Vector2D insideVelocity,
        Vector2D displacement, double tolerance)
    {
        // Candidate face, signed position correction needed to touch it.
        // At least one inward-facing candidate must actually be aligned.
        (Vector2D normal, double correction)[] candidates =
        [
            (new Vector2D(-1, 0), obstacle.Left - atEntry.Right),
            (new Vector2D(1, 0), obstacle.Right - atEntry.Left),
            (new Vector2D(0, -1), obstacle.Bottom - atEntry.Top),
            (new Vector2D(0, 1), obstacle.Top - atEntry.Bottom)
        ];

        Vector2D? best = null;
        double bestError = double.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            double error = Math.Abs(candidate.correction);
            if (!(error <= tolerance)) continue;

            // The inner sample must truly occupy positive area; a shared
            // corner or a purely tangential sweep is not an impact.
            bool otherAxisOverlaps = candidate.normal.X != 0.0
                ? inside.Bottom < obstacle.Top && inside.Top > obstacle.Bottom
                : inside.Left < obstacle.Right && inside.Right > obstacle.Left;
            if (!otherAxisOverlaps) continue;

            double inwardDisplacement = displacement.X * candidate.normal.X +
                                        displacement.Y * candidate.normal.Y;
            double inwardStartVelocity = startVelocity.X * candidate.normal.X +
                                         startVelocity.Y * candidate.normal.Y;
            double inwardInsideVelocity = insideVelocity.X * candidate.normal.X +
                                          insideVelocity.Y * candidate.normal.Y;
            if (!(inwardDisplacement < 0.0 || inwardStartVelocity < 0.0 ||
                  inwardInsideVelocity < 0.0))
                continue;

            // Prefer the smallest position correction. Exact ties prefer X;
            // the candidates above are already ordered X then Y.
            if (error < bestError)
            {
                best = candidate.normal;
                bestError = error;
            }
        }
        return best;
    }

    private static void AddCrossing(
        List<FaceEvent> faces, Func<double, MotionSample2D> evaluate,
        bool horizontal, double target, Vector2D normal,
        MotionSample2D begin, MotionSample2D end, double duration)
    {
        double a = horizontal ? begin.Position.X : begin.Position.Y;
        double b = horizontal ? end.Position.X : end.Position.Y;
        if (a == b || target < Math.Min(a, b) || target > Math.Max(a, b))
            return;
        double time = a == target ? 0.0 : b == target ? duration :
            CrossingTime(evaluate, horizontal, target, a, b, duration);
        faces.Add(new FaceEvent(time, normal));
    }

    private static double CrossingTime(
        Func<double, MotionSample2D> evaluate,
        bool horizontal, double target, double begin, double end, double duration)
    {
        double low = 0.0;
        double high = duration;
        bool increasing = end > begin;
        for (int i = 0; i < 128; i++)
        {
            double mid = low + (high - low) * 0.5;
            if (mid == low || mid == high) break;
            Vector2D position = evaluate(mid).Position;
            double value = horizontal ? position.X : position.Y;
            if (value == target) return mid;
            if ((value < target) == increasing) low = mid;
            else high = mid;
        }
        Vector2D lowPosition = evaluate(low).Position;
        Vector2D highPosition = evaluate(high).Position;
        double lowError = Math.Abs((horizontal ? lowPosition.X : lowPosition.Y) - target);
        double highError = Math.Abs((horizontal ? highPosition.X : highPosition.Y) - target);
        return lowError <= highError ? low : high;
    }

    private static AnalyticalSweepHit CreateHit(
        Aabb moving, StaticAabbCollider obstacle, MotionSample2D state,
        double time, Vector2D normal, double tolerance)
    {
        Aabb raw = moving.Translated(state.Position);
        Aabb target = obstacle.Bounds;
        Vector2D correction = normal switch
        {
            { X: -1.0 } => new Vector2D(target.Left - raw.Right, 0),
            { X: 1.0 } => new Vector2D(target.Right - raw.Left, 0),
            { Y: -1.0 } => new Vector2D(0, target.Bottom - raw.Top),
            { Y: 1.0 } => new Vector2D(0, target.Top - raw.Bottom),
            _ => throw new InvalidOperationException("Unsupported contact normal.")
        };
        if (Math.Abs(correction.X) + Math.Abs(correction.Y) > tolerance)
            throw new InvalidOperationException(
                "Analytical face alignment exceeded the configured tolerance.");
        return new AnalyticalSweepHit(obstacle.Id, time, normal,
            raw.Translated(correction), state.Velocity);
    }
}
