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
    /// Exact arbitrary analytical motion, including nonparabolic jump curves.
    /// The supplied interval MUST be monotone in X and Y, including in the
    /// relative frame of each moving platform. The calling coordinator splits
    /// at jump apices, release events, horizontal and relative reversals;
    /// platform turnarounds are split internally. Relative endpoint velocity
    /// sign changes are rejected rather than risking a missed collision.
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

                MotionSample2D bodyStart = evaluate(advanced);
                MotionSample2D bodyEnd = evaluate(advanced + segment);
                Aabb intervalBody = body.Translated(bodyStart.Position);
                MovingPlatformSample platformStart = platform.Evaluate(now);
                Vector2D platformVelocity = platformStart.Velocity;
                if (ChangesRelativeDirection(bodyStart.Velocity, bodyEnd.Velocity,
                        platformVelocity))
                {
                    throw new ArgumentException(
                        "Relative analytical motion turns within a moving-platform " +
                        "interval. Split the input at the relative velocity-zero event.",
                        nameof(evaluate));
                }

                // The platform is fixed in the relative frame during this
                // uninterrupted velocity leg. The body remains analytical.
                MotionSample2D EvaluateRelative(double localTime)
                {
                    MotionSample2D bodySample = evaluate(advanced + localTime);
                    return new MotionSample2D(
                        bodySample.Position - bodyStart.Position -
                            platformVelocity * localTime,
                        bodySample.Velocity - platformVelocity);
                }

                var relativeObstacle = new StaticAabbCollider(
                    platform.Id, platformStart.Bounds);
                AnalyticalSweepHit? relativeHit =
                    AnalyticalAabbCollisionDetector.SweepFirst(
                        intervalBody, EvaluateRelative, segment,
                        [relativeObstacle], contactAlignmentTolerance);
                if (relativeHit is { } hit)
                {
                    double time = advanced + hit.TimeSeconds;
                    MotionSample2D bodyAtImpact = evaluate(time);
                    MovingPlatformSample platformAtImpact =
                        platform.Evaluate(worldStartSeconds + time);
                    // Recompute WORLD impact bounds from authoritative body
                    // displacement. A face-aligned relative hit is corrected
                    // by the platform's exact displacement on this leg.
                    Vector2D authoritativePlatformDisplacement =
                        platformAtImpact.Bounds.Min - platformStart.Bounds.Min;
                    Aabb contactBounds = hit.MovingBoundsAtImpact.Translated(
                        authoritativePlatformDisplacement);
                    UnifiedCollisionHit candidate = new(
                        platform.Id, true, time, hit.Normal,
                        contactBounds, bodyAtImpact.Velocity,
                        platformAtImpact.Velocity);
                    best = Earlier(best, candidate);
                    break;
                }

                advanced += segment;
                if (advanced < durationSeconds && ++turnCount > maxPlatformTurnEvents)
                    throw new InvalidOperationException(
                        "Moving-platform turnaround event budget exceeded.");
            }
        }
        return best;
    }

    private static bool ChangesRelativeDirection(
        Vector2D initialBodyVelocity, Vector2D finalBodyVelocity,
        Vector2D platformVelocity)
    {
        // This is a conservative check of the caller's relative-monotonicity
        // contract. A single segment with multiple hidden turns is likewise
        // outside the API contract; callers must split at all motion events.
        return (initialBodyVelocity.X < platformVelocity.X &&
                finalBodyVelocity.X > platformVelocity.X) ||
               (initialBodyVelocity.X > platformVelocity.X &&
                finalBodyVelocity.X < platformVelocity.X) ||
               (initialBodyVelocity.Y < platformVelocity.Y &&
                finalBodyVelocity.Y > platformVelocity.Y) ||
               (initialBodyVelocity.Y > platformVelocity.Y &&
                finalBodyVelocity.Y < platformVelocity.Y);
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
