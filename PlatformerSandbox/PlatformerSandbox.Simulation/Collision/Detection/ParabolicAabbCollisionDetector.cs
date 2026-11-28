using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Motion.Integration;

namespace PlatformerSandbox.Simulation.Collision.Detection;

/// <summary>
/// Continuous AABB contact search for p(t) = p(0) + v*t + a*t*t/2.
/// Builds a finite partition from the analytic roots of all four contact
/// planes; only the midpoint of each root-delimited interval is examined to
/// classify the entire interval. This is NOT collision time-step sampling.
/// It detects curved paths even when the end displacement is zero.
/// </summary>
public static class ParabolicAabbCollisionDetector
{
    private readonly record struct BoundaryEvent(double Time, Vector2D Normal);

    /// <summary>
    /// Find a first contact with one obstacle. Initial positive penetration
    /// is intentionally outside this API (returns null); use FindOverlap or
    /// the Stepper's explicit initial-overlap validation.
    /// The alignment tolerance is in world units, not seconds. It bounds the
    /// face-coordinate adjustment needed due to floating-point root error.
    /// </summary>
    public static ParabolicSweepHit? Sweep(
        Aabb moving,
        Vector2D velocity,
        Vector2D acceleration,
        double durationSeconds,
        StaticAabbCollider obstacle,
        double contactAlignmentTolerance = 1e-9)
    {
        ValidateMotion(moving, durationSeconds, contactAlignmentTolerance);
        ArgumentNullException.ThrowIfNull(obstacle);
        if (durationSeconds == 0.0 || AabbCollisionDetector.Overlaps(moving, obstacle.Bounds))
            return null;

        Aabb target = obstacle.Bounds;
        var events = new List<BoundaryEvent>();

        // For positive-area overlap, the translation on each axis must lie
        // STRICTLY between these two thresholds. Their roots partition time
        // into intervals on which the overlap predicate cannot change.
        AddFaceRoots(events, velocity.X, acceleration.X,
            target.Left - moving.Right, new Vector2D(-1, 0), durationSeconds);
        AddFaceRoots(events, velocity.X, acceleration.X,
            target.Right - moving.Left, new Vector2D(1, 0), durationSeconds);
        AddFaceRoots(events, velocity.Y, acceleration.Y,
            target.Bottom - moving.Top, new Vector2D(0, -1), durationSeconds);
        AddFaceRoots(events, velocity.Y, acceleration.Y,
            target.Top - moving.Bottom, new Vector2D(0, 1), durationSeconds);

        var times = new List<double> { 0.0, durationSeconds };
        foreach (BoundaryEvent evt in events)
            times.Add(evt.Time);
        times.Sort();

        double? prior = null;
        var unique = new List<double>(times.Count);
        foreach (double value in times)
        {
            if (prior.HasValue && prior.Value == value)
                continue;
            unique.Add(value);
            prior = value;
        }

        // If any open interval overlaps, its LEFT boundary is the first
        // inward collision. This includes a tangential turning point whose
        // initial velocity is zero but acceleration points into the obstacle.
        for (int i = 0; i + 1 < unique.Count; i++)
        {
            double begin = unique[i];
            double end = unique[i + 1];
            if (begin >= end)
                continue;
            double midpoint = begin + (end - begin) * 0.5;
            Aabb middle = BoundsAt(moving, velocity, acceleration, midpoint);
            bool hasInteriorOverlap = AabbCollisionDetector.Overlaps(middle, target);
            if (!hasInteriorOverlap && end == durationSeconds)
            {
                // Near the update endpoint, the open contact interval can be
                // narrower than the world-coordinate ULP. Its midpoint may
                // round right back onto the entering face, despite the END
                // position being genuinely inside the obstacle. The analytic
                // face root still marks the entry: test the representable
                // endpoint before discarding that interval. Require positive
                // overlap on BOTH axes so endpoint touches and corner grazes
                // remain non-collisions.
                hasInteriorOverlap = AabbCollisionDetector.Overlaps(
                    BoundsAt(moving, velocity, acceleration, end), target);
            }
            if (!hasInteriorOverlap)
                continue;

            Vector2D? normal = SelectEnteringFace(events, begin, velocity, acceleration);
            if (!normal.HasValue)
            {
                throw new InvalidOperationException(
                    "Analytical collision interval entered without a valid inward contact face. " +
                    "Motion values may exceed reliable floating-point root precision.");
            }
            return CreateHit(moving, velocity, acceleration, obstacle, begin,
                normal.Value, contactAlignmentTolerance);
        }

        // Endpoint contact counts when it would immediately move inward in
        // the following time interval. A mere endpoint corner-graze does not.
        // This preserves the convention of the Milestone 10 linear sweep.
        Vector2D? atEnd = SelectEnteringFace(events, durationSeconds, velocity, acceleration);
        if (atEnd is { } endNormal)
        {
            Aabb endpoint = BoundsAt(moving, velocity, acceleration, durationSeconds);
            if (OtherAxisOverlaps(endpoint, target, endNormal))
            {
                return CreateHit(moving, velocity, acceleration, obstacle,
                    durationSeconds, endNormal, contactAlignmentTolerance);
            }
        }
        return null;
    }

    /// <summary>
    /// Earliest event across a static world. Exact equal times prefer the
    /// smallest stable obstacle ID independent of collection order. All IDs
    /// are validated even when the earliest collision was already found.
    /// </summary>
    public static ParabolicSweepHit? SweepFirst(
        Aabb moving,
        Vector2D velocity,
        Vector2D acceleration,
        double durationSeconds,
        IReadOnlyList<StaticAabbCollider> obstacles,
        double contactAlignmentTolerance = 1e-9)
    {
        ValidateMotion(moving, durationSeconds, contactAlignmentTolerance);
        ArgumentNullException.ThrowIfNull(obstacles);
        var ids = new HashSet<int>();
        ParabolicSweepHit? best = null;
        foreach (StaticAabbCollider obstacle in SpatialColliderSet.Select(obstacles, () => SpatialColliderSet.Parabolic(moving, velocity, acceleration, durationSeconds), contactAlignmentTolerance))
        {
            ArgumentNullException.ThrowIfNull(obstacle);
            if (!ids.Add(obstacle.Id))
                throw new ArgumentException("Obstacle IDs must be unique.", nameof(obstacles));
            ParabolicSweepHit? hit = Sweep(moving, velocity, acceleration, durationSeconds,
                obstacle, contactAlignmentTolerance);
            if (hit is { } candidate &&
                (best is null || candidate.TimeSeconds < best.Value.TimeSeconds ||
                 (candidate.TimeSeconds == best.Value.TimeSeconds &&
                  candidate.ColliderId < best.Value.ColliderId)))
                best = candidate;
        }
        return SpatialColliderSet.Verify(obstacles, best, () => SweepFirst(moving, velocity, acceleration, durationSeconds, obstacles, contactAlignmentTolerance));
    }

    private static void ValidateMotion(Aabb moving, double duration, double tolerance)
    {
        if (!moving.IsValid)
            throw new ArgumentException("Moving AABB must be valid.", nameof(moving));
        if (!double.IsFinite(duration) || duration < 0.0)
            throw new ArgumentOutOfRangeException(nameof(duration), "Time must be finite and nonnegative.");
        if (!double.IsFinite(tolerance) || tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), "Alignment tolerance must be finite and nonnegative.");
    }

    private static Aabb BoundsAt(Aabb moving, Vector2D velocity,
        Vector2D acceleration, double time)
    {
        MotionSample2D sample = ConstantAcceleration2D.Evaluate(
            Vector2D.Zero, velocity, acceleration, time);
        return moving.Translated(sample.Position);
    }

    private static void AddFaceRoots(List<BoundaryEvent> events,
        double velocity, double acceleration, double faceDisplacement,
        Vector2D normal, double duration)
    {
        // Solve acceleration*t*t/2 + velocity*t - faceDisplacement = 0.
        // Stable quadratic formula avoids catastrophic cancellation of a
        // small root when |velocity| dwarfs the acceleration term.
        double coefficientA = acceleration * 0.5;
        double coefficientB = velocity;
        double coefficientC = -faceDisplacement;
        if (!double.IsFinite(coefficientA) ||
            !double.IsFinite(coefficientB) ||
            !double.IsFinite(coefficientC))
        {
            throw new OverflowException("A face-intersection coefficient is not representable.");
        }
        if (coefficientA == 0.0)
        {
            if (coefficientB != 0.0)
                AddIfInRange(events, -coefficientC / coefficientB, normal, duration);
            else if (coefficientC == 0.0)
                AddIfInRange(events, 0.0, normal, duration);
            return;
        }

        double discriminant = coefficientB * coefficientB -
                              4.0 * coefficientA * coefficientC;
        if (!double.IsFinite(discriminant))
            throw new OverflowException("The face-intersection discriminant is not representable.");
        if (discriminant < 0.0)
            return;
        if (discriminant == 0.0)
        {
            AddIfInRange(events, -coefficientB / (2.0 * coefficientA), normal, duration);
            return;
        }

        double root = Math.Sqrt(discriminant);
        double q = -0.5 * (coefficientB + Math.CopySign(root, coefficientB));
        if (q == 0.0)
        {
            AddIfInRange(events, -coefficientB / (2.0 * coefficientA), normal, duration);
            return;
        }
        AddIfInRange(events, q / coefficientA, normal, duration);
        AddIfInRange(events, coefficientC / q, normal, duration);
    }

    private static void AddIfInRange(List<BoundaryEvent> events,
        double time, Vector2D normal, double duration)
    {
        if (double.IsFinite(time) && time >= 0.0 && time <= duration)
            events.Add(new BoundaryEvent(time, normal));
    }

    private static Vector2D? SelectEnteringFace(
        List<BoundaryEvent> events, double time, Vector2D velocity,
        Vector2D acceleration)
    {
        MotionSample2D state = ConstantAcceleration2D.Evaluate(
            Vector2D.Zero, velocity, acceleration, time);
        Vector2D? chosen = null;
        foreach (BoundaryEvent evt in events)
        {
            if (evt.Time != time)
                continue;
            double inwardVelocity = state.Velocity.X * evt.Normal.X +
                                    state.Velocity.Y * evt.Normal.Y;
            double inwardAcceleration = acceleration.X * evt.Normal.X +
                                        acceleration.Y * evt.Normal.Y;
            if (inwardVelocity > 0.0 ||
                (inwardVelocity == 0.0 && inwardAcceleration >= 0.0))
                continue;

            // Axis-first, X before Y for a true corner-entry tie.
            if (chosen is null || (evt.Normal.X != 0 && chosen.Value.X == 0))
                chosen = evt.Normal;
        }
        return chosen;
    }

    private static bool OtherAxisOverlaps(Aabb moving, Aabb target, Vector2D normal)
    {
        return normal.X != 0.0
            ? moving.Bottom < target.Top && moving.Top > target.Bottom
            : moving.Left < target.Right && moving.Right > target.Left;
    }

    private static ParabolicSweepHit CreateHit(Aabb moving,
        Vector2D velocity, Vector2D acceleration,
        StaticAabbCollider obstacle, double time, Vector2D normal,
        double contactAlignmentTolerance)
    {
        MotionSample2D sample = ConstantAcceleration2D.Evaluate(
            Vector2D.Zero, velocity, acceleration, time);
        Aabb atImpact = moving.Translated(sample.Position);
        Aabb target = obstacle.Bounds;
        Vector2D correction = normal switch
        {
            { X: -1.0 } => new Vector2D(target.Left - atImpact.Right, 0),
            { X: 1.0 } => new Vector2D(target.Right - atImpact.Left, 0),
            { Y: -1.0 } => new Vector2D(0, target.Bottom - atImpact.Top),
            { Y: 1.0 } => new Vector2D(0, target.Top - atImpact.Bottom),
            _ => throw new InvalidOperationException("Only cardinal contact normals are supported.")
        };
        double error = Math.Abs(correction.X) + Math.Abs(correction.Y);
        if (error > contactAlignmentTolerance)
        {
            throw new InvalidOperationException(
                $"Computed contact face differs from the collider by {error:R} world units, " +
                $"greater than the explicit alignment tolerance {contactAlignmentTolerance:R}.");
        }
        return new ParabolicSweepHit(obstacle.Id, time, normal,
            atImpact.Translated(correction), sample.Velocity);
    }
}
