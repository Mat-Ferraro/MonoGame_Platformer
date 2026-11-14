using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Integration;

namespace PlatformerSandbox.Simulation.MovingPlatforms;

/// <summary>
/// A deliberately narrow composition of continuous moving-floor landing
/// with Milestone 21 relative riding. It proposes a state for one platform.
/// Static scenery, jump reference interruption and simultaneous multiple
/// moving obstacles remain the authoritative character motor's concern.
/// </summary>
public static class MovingPlatformInteractionSolver
{
    public static MovingPlatformInteractionResult Step(
        MovingPlatformTrajectory platform,
        double startTimeSeconds,
        double durationSeconds,
        Aabb initialBody,
        Vector2D worldVelocity,
        Vector2D worldAcceleration,
        double supportedRelativeHorizontalAcceleration = 0.0,
        int maxTurnEvents = 64)
    {
        ArgumentNullException.ThrowIfNull(platform);
        if (!initialBody.IsValid)
            throw new ArgumentException("Body AABB must be valid.", nameof(initialBody));
        if (!double.IsFinite(startTimeSeconds) || startTimeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(startTimeSeconds));
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0 ||
            !double.IsFinite(startTimeSeconds + durationSeconds) ||
            (durationSeconds > 0.0 && startTimeSeconds + durationSeconds <= startTimeSeconds))
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (!double.IsFinite(supportedRelativeHorizontalAcceleration))
            throw new ArgumentOutOfRangeException(nameof(supportedRelativeHorizontalAcceleration));
        if (maxTurnEvents < 1)
            throw new ArgumentOutOfRangeException(nameof(maxTurnEvents));

        MovingPlatformSample initialPlatform = platform.Evaluate(startTimeSeconds);
        // Already supported bodies do not need to collide a second time to
        // stay on their floor. Only carry if not intentionally moving upward.
        double relativeVerticalVelocity = worldVelocity.Y - initialPlatform.Velocity.Y;
        if (MovingPlatformRideSolver.IsSupported(initialBody, initialPlatform.Bounds) &&
            (relativeVerticalVelocity < 0.0 ||
             (relativeVerticalVelocity == 0.0 && worldAcceleration.Y <= 0.0)))
        {
            MovingPlatformRideResult ride = MovingPlatformRideSolver.Step(
                platform, startTimeSeconds, durationSeconds, initialBody,
                worldVelocity.X - initialPlatform.Velocity.X,
                supportedRelativeHorizontalAcceleration, maxTurnEvents);
            return AsRideResult(ride, false, null);
        }

        MovingPlatformSweepHit? contact = MovingPlatformCollisionDetector.SweepFirst(
            initialBody, worldVelocity, worldAcceleration,
            startTimeSeconds, durationSeconds, [platform],
            maxTurnEventsPerPlatform: maxTurnEvents);
        if (contact is null)
        {
            MotionSample2D sample = ConstantAcceleration2D.Evaluate(
                Vector2D.Zero, worldVelocity, worldAcceleration, durationSeconds);
            return new MovingPlatformInteractionResult(
                initialBody.Translated(sample.Position), sample.Velocity,
                durationSeconds, MovingPlatformInteractionPhase.FreeFlightCompleted,
                null, false);
        }

        MovingPlatformSweepHit hit = contact.Value;
        double relativeYAtContact = hit.RelativeVelocityAtImpact.Y;
        bool supportingArrival = hit.Normal == new Vector2D(0.0, 1.0) &&
            (relativeYAtContact < 0.0 ||
             (relativeYAtContact == 0.0 && worldAcceleration.Y < 0.0));
        if (!supportingArrival)
        {
            // Do not invent a wall/ceiling response or latch a body to a
            // floor that reversed away at the exact moment of contact.
            // The authoritative motor must resolve this event explicitly.
            return new MovingPlatformInteractionResult(
                hit.BodyBoundsAtImpact, hit.BodyVelocityAtImpact,
                hit.TimeSeconds,
                MovingPlatformInteractionPhase.InterruptedByContact,
                hit, false);
        }

        double remaining = durationSeconds - hit.TimeSeconds;
        MovingPlatformRideResult carried = MovingPlatformRideSolver.Step(
            platform, startTimeSeconds + hit.TimeSeconds,
            remaining, hit.BodyBoundsAtImpact,
            hit.BodyVelocityAtImpact.X - hit.PlatformVelocityAtImpact.X,
            supportedRelativeHorizontalAcceleration, maxTurnEvents);
        return AsRideResult(carried, true, hit, hit.TimeSeconds);
    }

    private static MovingPlatformInteractionResult AsRideResult(
        MovingPlatformRideResult ride, bool landed,
        MovingPlatformSweepHit? contact, double priorAdvancedSeconds = 0.0) =>
        new(ride.Bounds, ride.WorldVelocity,
            priorAdvancedSeconds + ride.AdvancedSeconds,
            ride.IsSupported ? MovingPlatformInteractionPhase.RidingCompleted :
                               MovingPlatformInteractionPhase.DepartedSupport,
            contact, landed);
}
