using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;
using PlatformerSandbox.Simulation.Diagnostics;

namespace PlatformerSandbox.Simulation.MovingPlatforms;

/// <summary>
/// Supported riding arbitration: support departure competes against all external
/// static and moving collisions. A contact manifold includes the current floor's
/// implicit upward normal, making a rising-floor/ceiling pinch an incompatible
/// velocity constraint instead of a repeated two-contact loop.
/// </summary>
public static class MovingPlatformRideEventScheduler
{
    public static MovingPlatformRideEventResult Step(
        MovingPlatformTrajectory platform, double worldStartSeconds,
        double durationSeconds, Aabb body, double relativeHorizontalVelocity,
        double relativeHorizontalAcceleration,
        IReadOnlyList<StaticAabbCollider> staticColliders,
        IReadOnlyList<MovingPlatformTrajectory>? otherMovingPlatforms = null,
        int maxContactEvents = 8)
    {
        ArgumentNullException.ThrowIfNull(platform);
        ArgumentNullException.ThrowIfNull(staticColliders);
        if (!body.IsValid) throw new ArgumentException("Invalid body bounds.", nameof(body));
        if (!double.IsFinite(worldStartSeconds) || worldStartSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(worldStartSeconds));
        if (!double.IsFinite(durationSeconds) || durationSeconds <= 0.0 ||
            !double.IsFinite(worldStartSeconds + durationSeconds) ||
            worldStartSeconds + durationSeconds <= worldStartSeconds)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (!double.IsFinite(relativeHorizontalVelocity))
            throw new ArgumentOutOfRangeException(nameof(relativeHorizontalVelocity));
        if (!double.IsFinite(relativeHorizontalAcceleration))
            throw new ArgumentOutOfRangeException(nameof(relativeHorizontalAcceleration));
        if (maxContactEvents < 1)
            throw new ArgumentOutOfRangeException(nameof(maxContactEvents));

        MovingPlatformTrajectory[] others = (otherMovingPlatforms ??
            Array.Empty<MovingPlatformTrajectory>()).ToArray();
        var ids = new HashSet<int> { platform.Id };
        foreach (StaticAabbCollider collider in staticColliders)
        {
            ArgumentNullException.ThrowIfNull(collider);
            if (!ids.Add(collider.Id))
                throw new ArgumentException("Duplicate static/support collider ID.", nameof(staticColliders));
        }
        foreach (MovingPlatformTrajectory candidate in others)
        {
            ArgumentNullException.ThrowIfNull(candidate);
            if (!ids.Add(candidate.Id))
                throw new ArgumentException("The support must not be queried as an external moving body, and all IDs must be unique.",
                    nameof(otherMovingPlatforms));
        }

        MovingPlatformSample support = platform.Evaluate(worldStartSeconds);
        if (!MovingPlatformRideSolver.IsSupported(body, support.Bounds))
            throw new InvalidOperationException("Ride query requires existing platform support.");

        double leg = Math.Min(durationSeconds, platform.TimeUntilNextTurn(worldStartSeconds));
        if (!(leg > 0.0) || worldStartSeconds + leg <= worldStartSeconds)
            throw new SimulationNoProgressException(
                "Moving-platform ride leg cannot advance representable time.");

        // Support loss bounds the admissible contact query. Beyond departure,
        // gravity and ordinary free motion belong to the controller.
        MovingPlatformRideResult proposed = MovingPlatformRideSolver.Step(
            platform, worldStartSeconds, leg, body,
            relativeHorizontalVelocity, relativeHorizontalAcceleration);
        Vector2D worldVelocity = support.Velocity +
            new Vector2D(relativeHorizontalVelocity, 0.0);
        Vector2D acceleration = new(relativeHorizontalAcceleration, 0.0);

        IReadOnlyList<UnifiedCollisionHit> manifold =
            UnifiedCollisionEventScheduler.SweepParabolicCoincident(
                body, worldVelocity, acceleration, worldStartSeconds,
                proposed.AdvancedSeconds, staticColliders, others);
        if (manifold.Count == 0)
            return new MovingPlatformRideEventResult(
                proposed.Bounds, proposed.WorldVelocity, proposed.AdvancedSeconds,
                proposed.IsSupported, null)
            { SupportPlatformId = proposed.IsSupported ? platform.Id : null };

        if (manifold.Count > maxContactEvents)
            throw new SimulationEventLimitException(
                "Supported ride simultaneous-contact set exceeds the collision budget.",
                manifold.Select(hit => new SimulationContactTrace(
                    hit.ColliderId, hit.TimeSeconds, hit.Normal)).ToArray());

        UnifiedCollisionHit first = manifold[0];
        double eventTime = worldStartSeconds + first.TimeSeconds;
        MovingPlatformSample floorAtHit = platform.Evaluate(eventTime);
        // Include the floor as a *constraint* but do not double-report it as
        // another impact/contact-budget event. Its velocity is authoritative.
        UnifiedCollisionHit floorConstraint = new(platform.Id, true,
            first.TimeSeconds, new Vector2D(0.0, 1.0), first.BodyBoundsAtImpact,
            first.BodyVelocityAtImpact, floorAtHit.Velocity);
        UnifiedCollisionHit[] constraints = manifold.Append(floorConstraint)
            .OrderBy(hit => hit.ColliderId).ToArray();
        var resolved = SimultaneousContactResolver.Resolve(
            first.BodyVelocityAtImpact, acceleration, constraints,
            CollisionResponseMode.Slide, eventTime, first.BodyBoundsAtImpact);

        // Continuous collision uses the same support trajectory but may round
        // the world-space Y coordinate one or two ULPs from the support plane.
        // Canonicalize only such representational error, never a real gap.
        Aabb atImpact = first.BodyBoundsAtImpact;
        double supportGap = Math.Abs(atImpact.Bottom - floorAtHit.Bounds.Top);
        if (supportGap > 1e-9)
            throw new SimulationNoProgressException(
                "Supported ride impact differs meaningfully from its floor plane.");
        if (supportGap != 0.0)
            atImpact = new Aabb(
                new Vector2D(atImpact.Left, floorAtHit.Bounds.Top),
                new Vector2D(atImpact.Right, floorAtHit.Bounds.Top + atImpact.Height));

        // Stable ID order is also the public contact reporting order.
        CharacterControlContact[] contacts = manifold.Select(hit =>
            new CharacterControlContact(hit.ColliderId, hit.TimeSeconds,
                hit.Normal, first.BodyVelocityAtImpact, resolved.Velocity)).ToArray();
        CharacterControlContact? staticContact = null;
        foreach (CharacterControlContact contact in contacts)
        {
            if (staticColliders.Any(c => c.Id == contact.ColliderId))
            {
                staticContact = contact;
                break;
            }
        }

        // The rider may transfer to another moving top surface at the event,
        // but has exactly ONE authoritative support. Retain the current one
        // whenever it still supports the post-impact velocity; otherwise use
        // the lowest-ID valid top contact.
        int? supportId = MovingPlatformRideSolver.IsSupported(
            atImpact, floorAtHit.Bounds) &&
            resolved.Velocity.Y == floorAtHit.Velocity.Y ? platform.Id : null;
        if (supportId is null)
        {
            foreach (UnifiedCollisionHit hit in manifold)
            {
                if (!hit.IsMovingPlatform || hit.Normal.Y <= 0.0) continue;
                MovingPlatformTrajectory candidate = others.Single(p => p.Id == hit.ColliderId);
                MovingPlatformSample sample = candidate.Evaluate(eventTime);
                if (MovingPlatformRideSolver.IsSupported(atImpact, sample.Bounds) &&
                    resolved.Velocity.Y == sample.Velocity.Y)
                {
                    supportId = candidate.Id;
                    break;
                }
            }
        }
        if (first.TimeSeconds == 0.0 &&
            resolved.Velocity == first.BodyVelocityAtImpact &&
            resolved.Acceleration == acceleration && supportId == platform.Id)
            throw new SimulationNoProgressException(
                "Supported ride repeated an unconstraining zero-time manifold.");

        return new MovingPlatformRideEventResult(
            atImpact, resolved.Velocity, first.TimeSeconds,
            supportId is not null, staticContact)
        {
            Contacts = Array.AsReadOnly(contacts), SupportPlatformId = supportId
        };
    }
}
