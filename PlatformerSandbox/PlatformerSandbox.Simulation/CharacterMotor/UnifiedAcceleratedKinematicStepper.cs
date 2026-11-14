using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Integration;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// Event-aware, framework-independent accelerated motor against ONE shared
/// static/moving world. At every event the scheduler selects the earliest
/// static or moving contact, then the motor resolves and re-queries the rest.
/// This stateless stepper returns no partial result if an error occurs.
/// Moving-floor landing hands control to a higher-level rider coordinator.
/// Complex crushing, simultaneous multi-normal contacts, and live character
/// controller integration are intentionally outside this initial milestone.
/// </summary>
public static class UnifiedAcceleratedKinematicStepper
{
    public static UnifiedAcceleratedStepResult Step(
        Aabb initialBounds,
        Vector2D initialVelocity,
        Vector2D acceleration,
        double worldStartSeconds,
        double durationSeconds,
        IReadOnlyList<StaticAabbCollider> staticColliders,
        IReadOnlyList<MovingPlatformTrajectory> movingPlatforms,
        CollisionResponseMode responseMode = CollisionResponseMode.Slide,
        int maxContactEvents = 8,
        SimulationFaultSession? diagnostics = null)
    {
        if (!Enum.IsDefined(responseMode))
            throw new ArgumentOutOfRangeException(nameof(responseMode));
        if (maxContactEvents < 1)
            throw new ArgumentOutOfRangeException(nameof(maxContactEvents));
        diagnostics ??= SimulationFaultPolicy.Development.StartStep();

        // Validate the initial world even for zero-duration steps. A start
        // penetration is NOT a reason to invent a collision resolution.
        _ = UnifiedCollisionEventScheduler.SweepParabolicFirst(
            initialBounds, initialVelocity, acceleration, worldStartSeconds,
            0.0, staticColliders, movingPlatforms);

        Aabb bounds = initialBounds;
        Vector2D velocity = initialVelocity;
        Vector2D activeAcceleration = acceleration;
        double elapsed = 0.0;
        var contacts = new List<CharacterControlContact>();

        while (elapsed < durationSeconds)
        {
            double remaining = durationSeconds - elapsed;
            double absoluteTime = worldStartSeconds + elapsed;
            if (SimulationTimeProgress.TryConsumeKnownRemainder(
                    absoluteTime, remaining, velocity, activeAcceleration, diagnostics))
            {
                // Only a bounded, physically unresolvable final remainder
                // may be dropped. This is distinct from discarding a named
                // speed-cap, reversal or collision boundary event.
                elapsed = durationSeconds;
                break;
            }

            UnifiedCollisionHit? next = UnifiedCollisionEventScheduler.SweepParabolicFirst(
                bounds, velocity, activeAcceleration, absoluteTime, remaining,
                staticColliders, movingPlatforms);
            if (next is null)
            {
                MotionSample2D sample = ConstantAcceleration2D.Evaluate(
                    Vector2D.Zero, velocity, activeAcceleration, remaining);
                bounds = bounds.Translated(sample.Position);
                velocity = sample.Velocity;
                elapsed = durationSeconds;
                break;
            }

            if (contacts.Count >= maxContactEvents)
                throw new InvalidOperationException(
                    "Unified collision event limit exceeded; no partial state was committed.");

            UnifiedCollisionHit hit = next.Value;
            elapsed += hit.TimeSeconds;
            bounds = hit.BodyBoundsAtImpact;
            Vector2D incoming = hit.BodyVelocityAtImpact;
            Vector2D accelerationBeforeImpact = activeAcceleration;
            Vector2D outgoing;
            if (responseMode == CollisionResponseMode.Stop)
            {
                // Stop explicitly means halt in WORLD space, even if the
                // surface itself is moving. Do not inherit surface velocity.
                outgoing = Vector2D.Zero;
                activeAcceleration = Vector2D.Zero;
            }
            else
            {
                outgoing = CollisionResponse.Apply(
                    incoming - hit.SurfaceVelocityAtImpact,
                    hit.Normal, responseMode) + hit.SurfaceVelocityAtImpact;
                activeAcceleration = CollisionResponse.Apply(
                    activeAcceleration, hit.Normal, responseMode);
            }
            contacts.Add(new CharacterControlContact(
                hit.ColliderId, elapsed, hit.Normal, incoming, outgoing));
            velocity = outgoing;

            if (responseMode == CollisionResponseMode.Stop)
                return new UnifiedAcceleratedStepResult(
                    bounds, velocity, activeAcceleration, elapsed, null,
                    contacts.ToArray());

            if (hit.IsMovingPlatform && hit.Normal.Y > 0.0 &&
                hit.RelativeVelocityAtImpact.Y <= 0.0)
            {
                // Resting top contact at t=0 is support, not a fresh impact
                // to be reported indefinitely. A corner-only touch is NOT a
                // platform landing: horizontal support overlap is mandatory.
                MovingPlatformTrajectory platform = movingPlatforms.Single(
                    candidate => candidate.Id == hit.ColliderId);
                Aabb platformBounds = platform.Evaluate(
                    worldStartSeconds + elapsed).Bounds;
                if (MovingPlatformRideSolver.IsSupported(bounds, platformBounds))
                    return new UnifiedAcceleratedStepResult(
                        bounds, velocity, activeAcceleration, elapsed,
                        hit.ColliderId, contacts.ToArray());
            }

            if (hit.TimeSeconds == 0.0 && incoming == outgoing &&
                accelerationBeforeImpact == activeAcceleration)
                throw new InvalidOperationException(
                    $"Collision {hit.ColliderId} at t=0 cannot change velocity or " +
                    "acceleration and cannot advance time.");
        }

        return new UnifiedAcceleratedStepResult(
            bounds, velocity, activeAcceleration, elapsed, null,
            contacts.ToArray());
    }
}
