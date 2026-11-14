using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;
using PlatformerSandbox.Simulation.Motion.Integration;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// Advances an AABB through a single time interval under constant 2D
/// acceleration, locating collision events along the TRUE parabolic path.
/// At each event, position/velocity are evaluated analytically, followed by
/// an explicit stop/slide policy and a fresh analytical segment.
/// Does not handle arbitrary power-curve jump profiles or moving obstacles.
/// </summary>
public static class AcceleratedKinematicStepper
{
    public static AcceleratedKinematicStepResult Step(
        Aabb initialBounds,
        Vector2D initialVelocity,
        Vector2D acceleration,
        double durationSeconds,
        IReadOnlyList<StaticAabbCollider> obstacles,
        CollisionResponseMode responseMode = CollisionResponseMode.Slide,
        int maxContactEvents = 8,
        double contactAlignmentTolerance = 1e-9)
    {
        if (!initialBounds.IsValid)
            throw new ArgumentException("Moving AABB must be valid.", nameof(initialBounds));
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds));
        if (!double.IsFinite(contactAlignmentTolerance) || contactAlignmentTolerance < 0.0)
            throw new ArgumentOutOfRangeException(nameof(contactAlignmentTolerance));
        ArgumentNullException.ThrowIfNull(obstacles);
        if (!Enum.IsDefined(responseMode))
            throw new ArgumentOutOfRangeException(nameof(responseMode));
        if (maxContactEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxContactEvents));

        // No implicit depenetration: the caller must repair invalid initial
        // world states through a separate, explicit resolution policy.
        var ids = new HashSet<int>();
        foreach (StaticAabbCollider obstacle in obstacles)
        {
            ArgumentNullException.ThrowIfNull(obstacle);
            if (!ids.Add(obstacle.Id))
                throw new ArgumentException("Obstacle IDs must be unique.", nameof(obstacles));
            if (AabbCollisionDetector.Overlaps(initialBounds, obstacle.Bounds))
            {
                throw new InvalidOperationException(
                    $"Moving body starts penetrating collider {obstacle.Id}. " +
                    "Resolve the initial overlap explicitly before stepping.");
            }
        }

        Aabb bounds = initialBounds;
        Vector2D velocity = initialVelocity;
        Vector2D activeAcceleration = acceleration;
        double elapsed = 0.0;
        var contacts = new List<AcceleratedKinematicContact>();

        while (elapsed < durationSeconds)
        {
            double remaining = durationSeconds - elapsed;
            var hit = ParabolicAabbCollisionDetector.SweepFirst(
                bounds, velocity, activeAcceleration, remaining,
                obstacles, contactAlignmentTolerance);
            if (hit is null)
            {
                MotionSample2D end = ConstantAcceleration2D.Evaluate(
                    Vector2D.Zero, velocity, activeAcceleration, remaining);
                bounds = bounds.Translated(end.Position);
                velocity = end.Velocity;
                break;
            }

            if (contacts.Count >= maxContactEvents)
            {
                throw new InvalidOperationException(
                    $"Collision event limit ({maxContactEvents}) exceeded; " +
                    "no incomplete movement result is returned.");
            }

            double hitTime = hit.Value.TimeSeconds;
            elapsed += hitTime;
            bounds = hit.Value.MovingBoundsAtImpact;
            Vector2D incomingVelocity = hit.Value.VelocityAtImpact;

            Vector2D outgoingVelocity = CollisionResponse.Apply(
                incomingVelocity, hit.Value.Normal, responseMode);
            // Critical: under sustained contact, the inward acceleration must
            // also be constrained. Otherwise gravity would recreate the same
            // t=0 contact forever. Tangential/outward acceleration survives.
            Vector2D outgoingAcceleration = CollisionResponse.Apply(
                activeAcceleration, hit.Value.Normal, responseMode);

            contacts.Add(new AcceleratedKinematicContact(
                hit.Value.ColliderId, elapsed, hit.Value.Normal, bounds,
                incomingVelocity, outgoingVelocity,
                activeAcceleration, outgoingAcceleration));

            velocity = outgoingVelocity;
            activeAcceleration = outgoingAcceleration;
            if (responseMode == CollisionResponseMode.Stop)
                break;
        }

        return new AcceleratedKinematicStepResult(bounds, velocity,
            activeAcceleration, durationSeconds, contacts.ToArray());
    }
}
