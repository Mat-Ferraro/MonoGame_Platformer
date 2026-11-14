using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Motion.Integration;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Simulation.Collision.Detection;

/// <summary>
/// Continuous body-versus-moving-platform collision query for body motion
/// p(t)=p0+v0*t+0.5*a*t*t. Each platform follows the analytical, piecewise
/// constant-velocity path from Milestone 21. This is a QUERY, not a motor:
/// it does not apply collision response, resolve initial overlaps, or advance
/// authoritative character state.
/// </summary>
public static class MovingPlatformCollisionDetector
{
    /// <summary>
    /// Find the first contact, splitting each platform at its exact velocity
    /// reversals. Equal collision times choose the smaller stable platform ID.
    /// The body path uses WORLD velocity and acceleration. Platform velocity is
    /// subtracted exactly once within each uninterrupted platform interval.
    /// </summary>
    public static MovingPlatformSweepHit? SweepFirst(
        Aabb initialBody,
        Vector2D bodyVelocity,
        Vector2D bodyAcceleration,
        double startTimeSeconds,
        double durationSeconds,
        IReadOnlyList<MovingPlatformTrajectory> platforms,
        double contactAlignmentTolerance = 1e-8,
        int maxTurnEventsPerPlatform = 64)
    {
        if (!initialBody.IsValid)
            throw new ArgumentException("A valid body AABB is required.", nameof(initialBody));
        ArgumentNullException.ThrowIfNull(platforms);
        if (!double.IsFinite(startTimeSeconds) || startTimeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(startTimeSeconds));
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0 ||
            !double.IsFinite(startTimeSeconds + durationSeconds) ||
            (durationSeconds > 0 && startTimeSeconds + durationSeconds <= startTimeSeconds))
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (!double.IsFinite(contactAlignmentTolerance) || contactAlignmentTolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(contactAlignmentTolerance));
        if (maxTurnEventsPerPlatform < 1)
            throw new ArgumentOutOfRangeException(nameof(maxTurnEventsPerPlatform));

        var ids = new HashSet<int>();
        foreach (MovingPlatformTrajectory platform in platforms)
        {
            ArgumentNullException.ThrowIfNull(platform);
            if (!ids.Add(platform.Id))
                throw new ArgumentException("Moving platform IDs must be unique.", nameof(platforms));
            if (AabbCollisionDetector.Overlaps(initialBody,
                    platform.Evaluate(startTimeSeconds).Bounds))
                throw new InvalidOperationException(
                    $"The body begins penetrating moving platform {platform.Id}.");
        }

        if (durationSeconds == 0.0)
            return null;

        MovingPlatformSweepHit? best = null;
        foreach (MovingPlatformTrajectory platform in platforms)
        {
            MovingPlatformSweepHit? candidate = SweepOne(
                initialBody, bodyVelocity, bodyAcceleration,
                startTimeSeconds, durationSeconds, platform,
                contactAlignmentTolerance, maxTurnEventsPerPlatform);
            if (candidate is { } hit &&
                (best is null || hit.TimeSeconds < best.Value.TimeSeconds ||
                 (hit.TimeSeconds == best.Value.TimeSeconds &&
                  hit.PlatformId < best.Value.PlatformId)))
            {
                best = hit;
            }
        }
        return best;
    }

    private static MovingPlatformSweepHit? SweepOne(
        Aabb initialBody, Vector2D initialVelocity, Vector2D acceleration,
        double startTime, double duration, MovingPlatformTrajectory platform,
        double tolerance, int maxTurns)
    {
        double advanced = 0.0;
        int turns = 0;
        while (advanced < duration)
        {
            double now = startTime + advanced;
            double segment = Math.Min(duration - advanced,
                platform.TimeUntilNextTurn(now));
            if (!(segment > 0.0) || now + segment <= now ||
                !double.IsFinite(segment))
                throw new InvalidOperationException("Moving-platform event cannot advance time.");

            MotionSample2D bodyAtStart = ConstantAcceleration2D.Evaluate(
                Vector2D.Zero, initialVelocity, acceleration, advanced);
            Aabb currentBody = initialBody.Translated(bodyAtStart.Position);
            MovingPlatformSample platformAtStart = platform.Evaluate(now);
            Vector2D relativeVelocity = bodyAtStart.Velocity - platformAtStart.Velocity;

            // The obstacle is deliberately stationary in the platform-local
            // frame for this segment. The previous leg is never extrapolated
            // through a platform reversal.
            var stationaryInRelativeFrame = new StaticAabbCollider(
                platform.Id, platformAtStart.Bounds);
            ParabolicSweepHit? localHit = ParabolicAabbCollisionDetector.Sweep(
                currentBody, relativeVelocity, acceleration, segment,
                stationaryInRelativeFrame, tolerance);
            if (localHit is { } hit)
            {
                double hitTime = advanced + hit.TimeSeconds;
                double absoluteTime = startTime + hitTime;
                MovingPlatformSample atImpact = platform.Evaluate(absoluteTime);
                Vector2D platformDisplacement = atImpact.Bounds.Min -
                                                platformAtStart.Bounds.Min;
                Aabb bodyAtImpact = hit.MovingBoundsAtImpact.Translated(platformDisplacement);
                MotionSample2D bodySample = ConstantAcceleration2D.Evaluate(
                    Vector2D.Zero, initialVelocity, acceleration, hitTime);
                return new MovingPlatformSweepHit(platform.Id, hitTime,
                    hit.Normal, bodyAtImpact, atImpact.Bounds,
                    bodySample.Velocity, atImpact.Velocity);
            }

            advanced += segment;
            if (advanced < duration && ++turns > maxTurns)
                throw new InvalidOperationException(
                    "Moving-platform turnaround event limit exceeded.");
        }
        return null;
    }
}
