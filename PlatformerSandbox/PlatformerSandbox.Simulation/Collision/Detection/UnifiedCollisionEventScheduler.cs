using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Motion.Integration;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Simulation.Collision.Detection;

/// <summary>
/// Selects the earliest contact from ONE shared static-and-moving world.
/// Does not own body state, resolve penetrations, or apply collision responses.
/// Stable collider IDs break exact-time ties independently of enumeration order.
/// A caller must choose the collision response and requery after interruption.
/// </summary>
public static class UnifiedCollisionEventScheduler
{
    /// <summary>Exact parabolic body motion with piecewise-linear platforms.</summary>
    public static UnifiedCollisionHit? SweepParabolicFirst(
        Aabb body,
        Vector2D velocity,
        Vector2D acceleration,
        double worldStartSeconds,
        double durationSeconds,
        IReadOnlyList<StaticAabbCollider> staticColliders,
        IReadOnlyList<MovingPlatformTrajectory> movingPlatforms,
        double contactAlignmentTolerance = 1e-8)
    {
        ValidateWorld(body, worldStartSeconds, durationSeconds,
            staticColliders, movingPlatforms, contactAlignmentTolerance);
        if (durationSeconds == 0.0) return null;

        ParabolicSweepHit? staticHit = ParabolicAabbCollisionDetector.SweepFirst(
            body, velocity, acceleration, durationSeconds,
            staticColliders, contactAlignmentTolerance);
        MovingPlatformSweepHit? movingHit = MovingPlatformCollisionDetector.SweepFirst(
            body, velocity, acceleration, worldStartSeconds,
            durationSeconds, movingPlatforms, contactAlignmentTolerance);

        UnifiedCollisionHit? a = staticHit is { } s
            ? new UnifiedCollisionHit(s.ColliderId, false, s.TimeSeconds,
                s.Normal, s.MovingBoundsAtImpact, s.VelocityAtImpact, Vector2D.Zero)
            : null;
        UnifiedCollisionHit? b = movingHit is { } m
            ? new UnifiedCollisionHit(m.PlatformId, true, m.TimeSeconds,
                m.Normal, m.BodyBoundsAtImpact, m.BodyVelocityAtImpact,
                m.PlatformVelocityAtImpact)
            : null;
        return Earlier(a, b);
    }

    /// <summary>
    /// Returns ALL genuinely coincident first impacts in ascending collider-ID
    /// order. Uses exact time equality; it does not merge merely close impact
    /// times or introduce a floating-point time tolerance. The ordinary
    /// first-hit query remains unchanged for callers that need only one hit.
    /// </summary>
    public static IReadOnlyList<UnifiedCollisionHit> SweepParabolicCoincident(
        Aabb body, Vector2D velocity, Vector2D acceleration,
        double worldStartSeconds, double durationSeconds,
        IReadOnlyList<StaticAabbCollider> staticColliders,
        IReadOnlyList<MovingPlatformTrajectory> movingPlatforms,
        double contactAlignmentTolerance = 1e-8)
    {
        UnifiedCollisionHit? first = SweepParabolicFirst(body, velocity,
            acceleration, worldStartSeconds, durationSeconds, staticColliders,
            movingPlatforms, contactAlignmentTolerance);
        if (first is null) return Array.Empty<UnifiedCollisionHit>();

        var hits = new List<UnifiedCollisionHit> { first.Value };
        foreach (StaticAabbCollider obstacle in staticColliders)
        {
            if (obstacle.Id == first.Value.ColliderId) continue;
            ParabolicSweepHit? candidate = ParabolicAabbCollisionDetector.Sweep(
                body, velocity, acceleration, durationSeconds, obstacle,
                contactAlignmentTolerance);
            if (candidate is { } hit && hit.TimeSeconds == first.Value.TimeSeconds)
                hits.Add(new UnifiedCollisionHit(hit.ColliderId, false,
                    hit.TimeSeconds, hit.Normal, hit.MovingBoundsAtImpact,
                    hit.VelocityAtImpact, Vector2D.Zero));
        }
        foreach (MovingPlatformTrajectory platform in movingPlatforms)
        {
            if (platform.Id == first.Value.ColliderId) continue;
            MovingPlatformSweepHit? candidate = MovingPlatformCollisionDetector.SweepFirst(
                body, velocity, acceleration, worldStartSeconds,
                durationSeconds, [platform], contactAlignmentTolerance);
            if (candidate is { } hit && hit.TimeSeconds == first.Value.TimeSeconds)
                hits.Add(new UnifiedCollisionHit(hit.PlatformId, true,
                    hit.TimeSeconds, hit.Normal, hit.BodyBoundsAtImpact,
                    hit.BodyVelocityAtImpact, hit.PlatformVelocityAtImpact));
        }
        hits.Sort((a, b) => a.ColliderId.CompareTo(b.ColliderId));
        return Array.AsReadOnly(hits.ToArray());
    }

    /// <summary>
    /// Exact arbitrary analytical motion, including nonparabolic jump curves.
    /// The supplied interval must be monotone in WORLD X and Y; the calling
    /// coordinator splits at its own motion-law discontinuities and reversals.
    /// Each moving-platform leg is split internally at RELATIVE X/Y velocity
    /// zero events as well as platform turnarounds, so a world-monotone jump
    /// may turn relative to a rising elevator without invalidating the query.
    /// The velocity on each supplied world-motion phase must be continuous
    /// and have at most one zero crossing per component on a platform leg.
    /// evaluate(0).Position must be zero.
    /// </summary>
    public static UnifiedCollisionHit? SweepAnalyticalFirst(
        Aabb body,
        Func<double, MotionSample2D> evaluate,
        double worldStartSeconds,
        double durationSeconds,
        IReadOnlyList<StaticAabbCollider> staticColliders,
        IReadOnlyList<MovingPlatformTrajectory> movingPlatforms,
        double contactAlignmentTolerance = 1e-8,
        int maxPlatformTurnEvents = 64)
    {
        ArgumentNullException.ThrowIfNull(evaluate);
        ValidateWorld(body, worldStartSeconds, durationSeconds,
            staticColliders, movingPlatforms, contactAlignmentTolerance);
        if (maxPlatformTurnEvents < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPlatformTurnEvents));
        MotionSample2D atStart = evaluate(0.0);
        if (atStart.Position != Vector2D.Zero)
            throw new ArgumentException(
                "Analytical displacement must begin at zero.", nameof(evaluate));
        if (durationSeconds == 0.0) return null;

        AnalyticalSweepHit? staticHit = AnalyticalAabbCollisionDetector.SweepFirst(
            body, evaluate, durationSeconds, staticColliders,
            contactAlignmentTolerance);
        UnifiedCollisionHit? best = staticHit is { } s
            ? new UnifiedCollisionHit(s.ColliderId, false, s.TimeSeconds,
                s.Normal, s.MovingBoundsAtImpact, s.VelocityAtImpact, Vector2D.Zero)
            : null;

        foreach (MovingPlatformTrajectory platform in movingPlatforms)
        {
            double advanced = 0.0;
            int turnCount = 0;
            while (advanced < durationSeconds)
            {
                double now = worldStartSeconds + advanced;
                double remaining = durationSeconds - advanced;
                double segment = Math.Min(remaining, platform.TimeUntilNextTurn(now));
                if (!(segment > 0.0) || now + segment <= now ||
                    !(advanced + segment > advanced))
                    throw new InvalidOperationException(
                        "An analytical platform event cannot advance representable time. " +
                        "The authoritative motion coordinator must normalize the event boundary.");

                // A world-monotone jump need not be monotone RELATIVE to a
                // moving elevator. For example, vy may pass through the
                // elevator speed before the character reaches its world apex.
                // Locate relative velocity-zero events deterministically,
                // and sweep each monotone interval with the existing exact
                // analytical face-crossing detector. Never skip the whole
                // interval or treat a valid reversal as a simulation fault.
                Vector2D platformVelocity = platform.Evaluate(now).Velocity;
                var boundaries = new List<double> { 0.0, segment };
                AddRelativeTurn(boundaries, evaluate, advanced, segment,
                    platformVelocity.X, horizontal: true);
                AddRelativeTurn(boundaries, evaluate, advanced, segment,
                    platformVelocity.Y, horizontal: false);
                boundaries.Sort();

                bool foundOnThisLeg = false;
                for (int part = 0; part + 1 < boundaries.Count; ++part)
                {
                    double start = boundaries[part];
                    double end = boundaries[part + 1];
                    if (!(end > start)) continue;
                    double absoluteStart = advanced + start;
                    double intervalDuration = end - start;
                    MotionSample2D bodyStart = evaluate(absoluteStart);
                    Aabb intervalBody = body.Translated(bodyStart.Position);
                    MovingPlatformSample platformStart = platform.Evaluate(now + start);

                    // Platform velocity is constant throughout this leg;
                    // the frame origin changes for each monotone subinterval.
                    MotionSample2D EvaluateRelative(double localTime)
                    {
                        MotionSample2D bodySample = evaluate(absoluteStart + localTime);
                        return new MotionSample2D(
                            bodySample.Position - bodyStart.Position -
                                platformVelocity * localTime,
                            bodySample.Velocity - platformVelocity);
                    }

                    var relativeObstacle = new StaticAabbCollider(
                        platform.Id, platformStart.Bounds);
                    AnalyticalSweepHit? relativeHit =
                        AnalyticalAabbCollisionDetector.SweepFirst(
                            intervalBody, EvaluateRelative, intervalDuration,
                            [relativeObstacle], contactAlignmentTolerance);
                    if (relativeHit is not { } hit) continue;

                    double time = absoluteStart + hit.TimeSeconds;
                    MotionSample2D bodyAtImpact = evaluate(time);
                    MovingPlatformSample platformAtImpact =
                        platform.Evaluate(worldStartSeconds + time);
                    Vector2D platformDisplacement =
                        platformAtImpact.Bounds.Min - platformStart.Bounds.Min;
                    Aabb contactBounds = hit.MovingBoundsAtImpact.Translated(
                        platformDisplacement);
                    UnifiedCollisionHit candidate = new(
                        platform.Id, true, time, hit.Normal,
                        contactBounds, bodyAtImpact.Velocity,
                        platformAtImpact.Velocity);
                    best = Earlier(best, candidate);
                    foundOnThisLeg = true;
                    break;
                }
                if (foundOnThisLeg) break;

                advanced += segment;
                if (advanced < durationSeconds && ++turnCount > maxPlatformTurnEvents)
                    throw new InvalidOperationException(
                        "Moving-platform turnaround event budget exceeded.");
            }
        }
        return best;
    }

    /// <summary>
    /// All contacts at the exact first impact instant of a piecewise analytical
    /// trajectory, ordered by stable collider ID. The single-hit query performs
    /// the authoritative global-world validation and selects the impact time;
    /// each candidate is then queried with the SAME trajectory and platform
    /// turnaround/relative-turn logic. Near-but-unequal timestamps are not
    /// coalesced. This service reports geometry only, never gameplay response.
    /// </summary>
    public static IReadOnlyList<UnifiedCollisionHit> SweepAnalyticalCoincident(
        Aabb body,
        Func<double, MotionSample2D> evaluate,
        double worldStartSeconds,
        double durationSeconds,
        IReadOnlyList<StaticAabbCollider> staticColliders,
        IReadOnlyList<MovingPlatformTrajectory> movingPlatforms,
        double contactAlignmentTolerance = 1e-8,
        int maxPlatformTurnEvents = 64)
    {
        UnifiedCollisionHit? first = SweepAnalyticalFirst(
            body, evaluate, worldStartSeconds, durationSeconds,
            staticColliders, movingPlatforms, contactAlignmentTolerance,
            maxPlatformTurnEvents);
        if (first is null) return Array.Empty<UnifiedCollisionHit>();

        var hits = new List<UnifiedCollisionHit> { first.Value };
        foreach (StaticAabbCollider collider in staticColliders)
        {
            if (collider.Id == first.Value.ColliderId) continue;
            UnifiedCollisionHit? candidate = SweepAnalyticalFirst(
                body, evaluate, worldStartSeconds, durationSeconds,
                [collider], Array.Empty<MovingPlatformTrajectory>(),
                contactAlignmentTolerance, maxPlatformTurnEvents);
            if (candidate is { } hit && hit.TimeSeconds == first.Value.TimeSeconds)
                hits.Add(hit);
        }
        foreach (MovingPlatformTrajectory platform in movingPlatforms)
        {
            if (platform.Id == first.Value.ColliderId) continue;
            UnifiedCollisionHit? candidate = SweepAnalyticalFirst(
                body, evaluate, worldStartSeconds, durationSeconds,
                Array.Empty<StaticAabbCollider>(), [platform],
                contactAlignmentTolerance, maxPlatformTurnEvents);
            if (candidate is { } hit && hit.TimeSeconds == first.Value.TimeSeconds)
                hits.Add(hit);
        }
        hits.Sort((a, b) => a.ColliderId.CompareTo(b.ColliderId));
        return Array.AsReadOnly(hits.ToArray());
    }

    /// <summary>
    /// Find an interior relative-velocity zero using a bracketed binary64
    /// search. This is event isolation, not sampled collision detection:
    /// contact times are still solved by AnalyticalAabbCollisionDetector.
    /// On a single continuous world-motion phase a velocity component has
    /// at most one crossing; any more complex curve must be split by its
    /// motion coordinator at its physical law-change events.
    /// </summary>
    private static void AddRelativeTurn(
        List<double> boundaries,
        Func<double, MotionSample2D> evaluate,
        double advanced,
        double duration,
        double platformVelocity,
        bool horizontal)
    {
        double InitialRelativeVelocity(double localTime)
        {
            Vector2D v = evaluate(advanced + localTime).Velocity;
            return (horizontal ? v.X : v.Y) - platformVelocity;
        }

        double first = InitialRelativeVelocity(0.0);
        double last = InitialRelativeVelocity(duration);
        if (!((first < 0.0 && last > 0.0) ||
              (first > 0.0 && last < 0.0)))
            return;

        double low = 0.0;
        double high = duration;
        bool negativeAtStart = first < 0.0;
        for (int i = 0; i < 128; ++i)
        {
            double mid = low + (high - low) * 0.5;
            if (mid == low || mid == high) break;
            double velocity = InitialRelativeVelocity(mid);
            if (velocity == 0.0)
            {
                low = mid;
                high = mid;
                break;
            }
            if ((velocity < 0.0) == negativeAtStart) low = mid;
            else high = mid;
        }

        // Pick the representable time nearest the root. A root that rounds
        // onto the segment boundary requires no additional subinterval;
        // the physical reversal cannot be temporally resolved in this leg.
        double root = Math.Abs(InitialRelativeVelocity(low)) <=
                      Math.Abs(InitialRelativeVelocity(high)) ? low : high;
        if (root > 0.0 && root < duration)
            boundaries.Add(root);
    }

    private static UnifiedCollisionHit? Earlier(
        UnifiedCollisionHit? a, UnifiedCollisionHit? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return b.Value.TimeSeconds < a.Value.TimeSeconds ||
            (b.Value.TimeSeconds == a.Value.TimeSeconds &&
                b.Value.ColliderId < a.Value.ColliderId) ? b : a;
    }

    private static void ValidateWorld(
        Aabb body, double worldStart, double duration,
        IReadOnlyList<StaticAabbCollider> staticColliders,
        IReadOnlyList<MovingPlatformTrajectory> movingPlatforms,
        double tolerance)
    {
        if (!body.IsValid)
            throw new ArgumentException("A valid body AABB is required.", nameof(body));
        if (!double.IsFinite(worldStart) || worldStart < 0.0)
            throw new ArgumentOutOfRangeException(nameof(worldStart));
        if (!double.IsFinite(duration) || duration < 0.0 ||
            !double.IsFinite(worldStart + duration) ||
            (duration > 0.0 && worldStart + duration <= worldStart))
            throw new ArgumentOutOfRangeException(nameof(duration));
        if (!double.IsFinite(tolerance) || tolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(tolerance));
        ArgumentNullException.ThrowIfNull(staticColliders);
        ArgumentNullException.ThrowIfNull(movingPlatforms);

        // Require global collider-ID uniqueness for deterministic equal-time
        // ordering. Also reject invalid initial overlaps even for dt == 0.
        var ids = new HashSet<int>();
        foreach (StaticAabbCollider collider in staticColliders)
        {
            ArgumentNullException.ThrowIfNull(collider);
            if (!ids.Add(collider.Id))
                throw new ArgumentException("All collider IDs must be unique.",
                    nameof(staticColliders));
            if (AabbCollisionDetector.Overlaps(body, collider.Bounds))
                throw new InvalidOperationException(
                    $"The body begins penetrating static collider {collider.Id}.");
        }
        foreach (MovingPlatformTrajectory platform in movingPlatforms)
        {
            ArgumentNullException.ThrowIfNull(platform);
            if (!ids.Add(platform.Id))
                throw new ArgumentException("All collider IDs must be unique.",
                    nameof(movingPlatforms));
            if (AabbCollisionDetector.Overlaps(body,
                    platform.Evaluate(worldStart).Bounds))
                throw new InvalidOperationException(
                    $"The body begins penetrating moving platform {platform.Id}.");
        }
    }
}