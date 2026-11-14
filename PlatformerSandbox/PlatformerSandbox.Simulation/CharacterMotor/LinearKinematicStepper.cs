using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// Advances a body with CONSTANT VELOCITY, finding the first continuous swept
/// AABB impact, resolving velocity, and advancing the remaining time using
/// the new straight-line segment. No discrete collision sampling or nudges.
///
/// IMPORTANT: Do not pass an entire curved jump's endpoint displacement here.
/// Variable acceleration and curved trajectories require a later analytical
/// event-aware collision query. This deliberately does NOT implement that.
/// </summary>
public static class LinearKinematicStepper
{
    public static LinearKinematicStepResult Step(
        Aabb initialBounds,
        Vector2D initialVelocity,
        double durationSeconds,
        IReadOnlyList<StaticAabbCollider> obstacles,
        CollisionResponseMode responseMode = CollisionResponseMode.Slide,
        int maxContactEvents = 8)
    {
        if (!initialBounds.IsValid)
            throw new ArgumentException("Moving AABB must be valid.", nameof(initialBounds));
        if (!double.IsFinite(durationSeconds) || durationSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Time must be finite and nonnegative.");
        ArgumentNullException.ThrowIfNull(obstacles);
        if (!Enum.IsDefined(responseMode))
            throw new ArgumentOutOfRangeException(nameof(responseMode));
        if (maxContactEvents <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxContactEvents));

        // Validate the world once, including when dt == 0 or velocity == 0.
        // Initial penetrations have no implicit repair policy; reject them.
        HashSet<int> uniqueIds = new();
        foreach (StaticAabbCollider obstacle in obstacles)
        {
            ArgumentNullException.ThrowIfNull(obstacle);
            if (!uniqueIds.Add(obstacle.Id))
                throw new ArgumentException("Obstacle IDs must be unique.", nameof(obstacles));
            if (AabbCollisionDetector.Overlaps(initialBounds, obstacle.Bounds))
            {
                throw new InvalidOperationException(
                    $"Moving body starts penetrating collider {obstacle.Id}. " +
                    "Resolve initial overlaps explicitly before stepping.");
            }
        }

        Aabb bounds = initialBounds;
        Vector2D velocity = initialVelocity;
        double elapsed = 0.0;
        var contacts = new List<LinearKinematicContact>();

        while (elapsed < durationSeconds && velocity != Vector2D.Zero)
        {
            double remaining = durationSeconds - elapsed;
            Vector2D displacement = velocity * remaining;
            if (displacement == Vector2D.Zero)
                break;

            var hit = AabbCollisionDetector.SweepFirst(bounds, displacement, obstacles);
            if (hit is null)
            {
                bounds = bounds.Translated(displacement);
                break;
            }

            if (contacts.Count == maxContactEvents)
            {
                throw new InvalidOperationException(
                    $"Collision event limit ({maxContactEvents}) exceeded; " +
                    "the step was not completed. No truncated result is returned.");
            }

            // Swept query expresses impact time as a fraction of this segment.
            // Contact is exact for constant-velocity translation.
            double timeToHit = remaining * hit.Value.TimeOfImpact;
            elapsed += timeToHit;
            bounds = hit.Value.MovingBoundsAtImpact;

            Vector2D outgoing = CollisionResponse.Apply(velocity, hit.Value.Normal, responseMode);
            contacts.Add(new LinearKinematicContact(
                hit.Value.ColliderId, elapsed, hit.Value.Normal,
                bounds, velocity, outgoing));
            velocity = outgoing;

            if (responseMode == CollisionResponseMode.Stop)
                break;

            // The next iteration uses the new velocity and remaining duration.
            // t=0 contacts are valid; each blocking contact removes an inward
            // axis so a finite number can occur in normal AABB geometry.
        }

        return new LinearKinematicStepResult(
            bounds, velocity, durationSeconds, contacts.ToArray());
    }
}
