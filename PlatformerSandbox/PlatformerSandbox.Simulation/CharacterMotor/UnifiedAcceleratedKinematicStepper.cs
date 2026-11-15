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
/// Exact-time simultaneous cardinal contacts use a deterministic constraint
/// manifold. Broader body/body response and moving-platform obstruction policy
/// remain outside this stateless character stepper.
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
            // A collision at the last representable instant can leave a
            // local remainder smaller than one ULP of the absolute clock.
            // This is an ENDPOINT event, not an unknown interval to discard:
            // integrate that tiny remainder in local time, require its motion
            // to fit the explicit safety budget, then validate end geometry.
            // In particular Strict mode must not report this normal endpoint
            // canonicalization as a numerical RECOVERY. A step which starts
            // with an unrepresentable duration is intentionally still faulted.
            double worldEnd = worldStartSeconds + durationSeconds;
            double oneClockUlp = Math.BitIncrement(absoluteTime) - absoluteTime;
            if (contacts.Count > 0 && remaining > 0.0 &&
                absoluteTime + remaining <= absoluteTime &&
                worldEnd >= absoluteTime &&
                worldEnd - absoluteTime <= oneClockUlp)
            {
                MotionSample2D tail = ConstantAcceleration2D.Evaluate(
                    Vector2D.Zero, velocity, activeAcceleration, remaining);
                double displacement = Math.Max(
                    Math.Abs(tail.Position.X), Math.Abs(tail.Position.Y));
                if (!double.IsFinite(displacement) ||
                    displacement > diagnostics.Policy.MaximumDiscardedDisplacement)
                    throw new SimulationFaultException(
                        SimulationRecoveryKind.SubResolutionTimeRemainder,
                        "Post-contact endpoint interval exceeds the safe motion bound.");
                Aabb atEnd = bounds.Translated(tail.Position);
                atEnd = MovingPlatformOverlapResolver.AlignPrecisionSideOverlap(
                    atEnd, worldEnd, movingPlatforms, staticColliders);
                // A zero-duration query validates both static and moving
                // penetration at the authoritative END time without inventing
                // a physically unrepresentable sweep interval.
                _ = UnifiedCollisionEventScheduler.SweepParabolicFirst(
                    atEnd, tail.Velocity, Vector2D.Zero, worldEnd,
                    0.0, staticColliders, movingPlatforms);
                bounds = atEnd;
                velocity = tail.Velocity;
                elapsed = durationSeconds;
                break;
            }
            if (SimulationTimeProgress.TryConsumeKnownRemainder(
                    absoluteTime, remaining, velocity, activeAcceleration, diagnostics))
            {
                // An otherwise valid nonadvancing interval remains a
                // classified policy-controlled recovery, including in Strict.
                elapsed = durationSeconds;
                break;
            }

            // A resolved contact can leave a few ULPs of lateral overlap
            // against another platform at the same event time (notably the
            // descending elevator at the static-floor corner). Normalize the
            // known motion boundary before the next STRICT collision query.
            // The first query's starting-penetration validation above is never
            // bypassed by this correction.
            if (contacts.Count > 0)
                bounds = MovingPlatformOverlapResolver.AlignPrecisionSideOverlap(
                    bounds, absoluteTime, movingPlatforms, staticColliders);

            UnifiedCollisionHit? next = UnifiedCollisionEventScheduler.SweepParabolicFirst(
                bounds, velocity, activeAcceleration, absoluteTime, remaining,
                staticColliders, movingPlatforms);
            if (next is null)
            {
                MotionSample2D sample = ConstantAcceleration2D.Evaluate(
                    Vector2D.Zero, velocity, activeAcceleration, remaining);
                bounds = bounds.Translated(sample.Position);
                bounds = MovingPlatformOverlapResolver.AlignPrecisionSideOverlap(
                    bounds, worldStartSeconds + durationSeconds,
                    movingPlatforms, staticColliders);
                velocity = sample.Velocity;
                elapsed = durationSeconds;
                break;
            }

            if (contacts.Count >= maxContactEvents)
            {
                var trace = contacts.Select(c => new SimulationContactTrace(
                    c.ColliderId, c.TimeWithinStepSeconds, c.Normal)).ToArray();
                string orderedContacts = string.Join("; ", trace.Select(c =>
                    $"id={c.ColliderId} t={c.TimeWithinStepSeconds:R} " +
                    $"normal=({c.Normal.X:R},{c.Normal.Y:R})"));
                throw new SimulationEventLimitException(
                    "Unified collision event limit exceeded; no partial state was committed. " +
                    $"WorldStart={worldStartSeconds:R}, localTime={elapsed:R}, " +
                    $"bounds=[{bounds.Left:R},{bounds.Bottom:R}," +
                    $"{bounds.Right:R},{bounds.Top:R}], " +
                    $"velocity=({velocity.X:R},{velocity.Y:R}), " +
                    $"contacts=[{orderedContacts}]",
                    trace);
            }

            UnifiedCollisionHit hit = next.Value;

            // A vertical sweep can encounter a corner where the player's side
            // is a few binary64 ULPs inside a moving platform's side face.
            // At the instant of underside/top contact the boxes are only
            // TOUCHING in Y, so positive-area-only cleanup cannot see it.
            // Canonicalize that established X-side graze first and re-query;
            // do not apply a fictitious vertical impact or burn an event on
            // alternating ceiling/floor normals. This is bounded, rejects
            // any correction that intersects other geometry, and is NOT used
            // to normalize the strictly validated input state.
            if (hit.IsMovingPlatform && hit.Normal.Y != 0.0)
            {
                Aabb aligned = MovingPlatformOverlapResolver.AlignPrecisionSideOverlap(
                    hit.BodyBoundsAtImpact,
                    worldStartSeconds + elapsed + hit.TimeSeconds,
                    movingPlatforms, staticColliders);
                if (aligned != hit.BodyBoundsAtImpact)
                {
                    elapsed += hit.TimeSeconds;
                    bounds = aligned;
                    velocity = hit.BodyVelocityAtImpact;
                    continue;
                }
            }

            // Resolve truly SIMULTANEOUS contacts together. In particular,
            // two opposing moving/static faces must not alternate as separate
            // zero-time events. Distinct times are never coalesced.
            IReadOnlyList<UnifiedCollisionHit> coincident =
                UnifiedCollisionEventScheduler.SweepParabolicCoincident(
                    bounds, velocity, activeAcceleration, absoluteTime, remaining,
                    staticColliders, movingPlatforms);
            if (coincident.Count > 1)
            {
                if (contacts.Count + coincident.Count > maxContactEvents)
                {
                    var trace = contacts.Select(c => new SimulationContactTrace(
                        c.ColliderId, c.TimeWithinStepSeconds, c.Normal))
                        .Concat(coincident.Select(c => new SimulationContactTrace(
                            c.ColliderId, elapsed + c.TimeSeconds, c.Normal))).ToArray();
                    throw new SimulationEventLimitException(
                        "Simultaneous contact set exceeds the collision-event budget; " +
                        "no partial state was committed.", trace);
                }

                double eventTime = worldStartSeconds + elapsed + hit.TimeSeconds;
                Vector2D incomingGroup = hit.BodyVelocityAtImpact;
                var resolved = SimultaneousContactResolver.Resolve(
                    incomingGroup, activeAcceleration, coincident, responseMode,
                    eventTime, hit.BodyBoundsAtImpact);
                elapsed += hit.TimeSeconds;
                bounds = hit.BodyBoundsAtImpact;
                bounds = MovingPlatformOverlapResolver.AlignPrecisionSideOverlap(
                    bounds, eventTime, movingPlatforms, staticColliders);
                Vector2D beforeAcceleration = activeAcceleration;
                velocity = resolved.Velocity;
                activeAcceleration = resolved.Acceleration;
                foreach (UnifiedCollisionHit contact in coincident)
                    contacts.Add(new CharacterControlContact(contact.ColliderId,
                        elapsed, contact.Normal, incomingGroup, velocity));

                if (responseMode == CollisionResponseMode.Stop)
                    return new UnifiedAcceleratedStepResult(
                        bounds, velocity, activeAcceleration, elapsed, null,
                        contacts.ToArray());

                // Select a supported top contact in stable ID order, but
                // only when its own geometrical support test succeeds.
                foreach (UnifiedCollisionHit contact in coincident)
                {
                    if (!contact.IsMovingPlatform || contact.Normal.Y <= 0.0 ||
                        contact.RelativeVelocityAtImpact.Y > 0.0)
                        continue;
                    MovingPlatformTrajectory platform = movingPlatforms.Single(
                        candidate => candidate.Id == contact.ColliderId);
                    if (MovingPlatformRideSolver.IsSupported(bounds,
                        platform.Evaluate(eventTime).Bounds))
                        return new UnifiedAcceleratedStepResult(
                            bounds, velocity, activeAcceleration, elapsed,
                            contact.ColliderId, contacts.ToArray());
                }

                if (hit.TimeSeconds == 0.0 &&
                    incomingGroup == velocity && beforeAcceleration == activeAcceleration)
                    throw new SimulationNoProgressException(
                        "Simultaneous zero-time contacts cannot advance or constrain motion.");
                continue;
            }

            elapsed += hit.TimeSeconds;
            bounds = hit.BodyBoundsAtImpact;
            bounds = MovingPlatformOverlapResolver.AlignPrecisionSideOverlap(
                bounds, worldStartSeconds + elapsed,
                movingPlatforms, staticColliders);
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
                throw new SimulationNoProgressException(
                    $"Collision {hit.ColliderId} at t=0 cannot change velocity or " +
                    "acceleration and cannot advance time.");
        }

        return new UnifiedAcceleratedStepResult(
            bounds, velocity, activeAcceleration, elapsed, null,
            contacts.ToArray());
    }
}