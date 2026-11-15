using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Collision.Resolution;
using PlatformerSandbox.Simulation.Diagnostics;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// Resolves the velocity half-space constraints imposed by contacts at the
/// SAME analytically determined impact instant. Contacts at merely nearby
/// timestamps must not be merged. This is a projection of a 2-D, axis-aligned
/// velocity onto the intersection of the allowed contact half-spaces; it is
/// not iterative impulse physics, positional depenetration, or a tolerance.
/// </summary>
public static class SimultaneousContactResolver
{
    public static (Vector2D Velocity, Vector2D Acceleration) Resolve(
        Vector2D incomingVelocity, Vector2D acceleration,
        IReadOnlyList<UnifiedCollisionHit> simultaneousHits,
        CollisionResponseMode mode, double worldTimeSeconds, Aabb bodyAtImpact)
    {
        ArgumentNullException.ThrowIfNull(simultaneousHits);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (!double.IsFinite(worldTimeSeconds) || worldTimeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(worldTimeSeconds));
        if (!bodyAtImpact.IsValid) throw new ArgumentException("Invalid impact bounds.", nameof(bodyAtImpact));
        if (simultaneousHits.Count == 0)
            throw new ArgumentException("At least one contact is required.", nameof(simultaneousHits));

        double minX = double.NegativeInfinity, maxX = double.PositiveInfinity;
        double minY = double.NegativeInfinity, maxY = double.PositiveInfinity;
        UnifiedCollisionHit? lowerX = null, upperX = null, lowerY = null, upperY = null;
        double time = simultaneousHits[0].TimeSeconds;
        var ids = new HashSet<int>();
        foreach (UnifiedCollisionHit hit in simultaneousHits)
        {
            if (hit.TimeSeconds != time)
                throw new ArgumentException("Contacts must have exactly equal impact timestamps.", nameof(simultaneousHits));
            if (!ids.Add(hit.ColliderId))
                throw new ArgumentException("Duplicate collider in contact set.", nameof(simultaneousHits));
            if (!double.IsFinite(hit.TimeSeconds) || hit.TimeSeconds < 0.0 ||
                !hit.BodyBoundsAtImpact.IsValid ||
                (!hit.IsMovingPlatform && hit.SurfaceVelocityAtImpact != Vector2D.Zero))
                throw new ArgumentException("Invalid contact time, bounds, or static-surface velocity.",
                    nameof(simultaneousHits));
            if (hit.Normal == new Vector2D(1, 0))
            {
                double v = hit.SurfaceVelocityAtImpact.X;
                if (v > minX || (v == minX && (lowerX is null || hit.ColliderId < lowerX.Value.ColliderId)))
                { minX = v; lowerX = hit; }
            }
            else if (hit.Normal == new Vector2D(-1, 0))
            {
                double v = hit.SurfaceVelocityAtImpact.X;
                if (v < maxX || (v == maxX && (upperX is null || hit.ColliderId < upperX.Value.ColliderId)))
                { maxX = v; upperX = hit; }
            }
            else if (hit.Normal == new Vector2D(0, 1))
            {
                double v = hit.SurfaceVelocityAtImpact.Y;
                if (v > minY || (v == minY && (lowerY is null || hit.ColliderId < lowerY.Value.ColliderId)))
                { minY = v; lowerY = hit; }
            }
            else if (hit.Normal == new Vector2D(0, -1))
            {
                double v = hit.SurfaceVelocityAtImpact.Y;
                if (v < maxY || (v == maxY && (upperY is null || hit.ColliderId < upperY.Value.ColliderId)))
                { maxY = v; upperY = hit; }
            }
            else throw new ArgumentException("Normals must be cardinal.", nameof(simultaneousHits));
        }

        // Opposing velocities that cannot simultaneously be satisfied are a
        // genuine geometric obstruction, not a reason to iterate contacts or
        // increase the contact budget. At least one face must be moving for a
        // gameplay crush. Impossible static-only geometry is a simulation fault.
        if (minX > maxX)
            ThrowOpposition(lowerX!.Value, upperX!.Value,
                CharacterCrushAxis.Horizontal, worldTimeSeconds, bodyAtImpact);
        if (minY > maxY)
            ThrowOpposition(lowerY!.Value, upperY!.Value,
                CharacterCrushAxis.Vertical, worldTimeSeconds, bodyAtImpact);

        if (mode == CollisionResponseMode.Stop)
            return (Vector2D.Zero, Vector2D.Zero);

        double x = Math.Clamp(incomingVelocity.X, minX, maxX);
        double y = Math.Clamp(incomingVelocity.Y, minY, maxY);
        // Platforms have constant velocity within a scheduler leg, so any
        // acceleration forcing the body INTO an active constraint is removed.
        double ax = acceleration.X;
        double ay = acceleration.Y;
        if ((x == minX && ax < 0) || (x == maxX && ax > 0)) ax = 0;
        if ((y == minY && ay < 0) || (y == maxY && ay > 0)) ay = 0;
        return (new Vector2D(x, y), new Vector2D(ax, ay));
    }

    private static void ThrowOpposition(UnifiedCollisionHit lower,
        UnifiedCollisionHit upper, CharacterCrushAxis axis,
        double worldTimeSeconds, Aabb body)
    {
        if (!lower.IsMovingPlatform && !upper.IsMovingPlatform)
            throw new SimulationNoProgressException(
                $"Impossible opposing static contact constraints on {axis} axis: " +
                $"colliders {lower.ColliderId} and {upper.ColliderId}.");
        UnifiedCollisionHit mover = lower.IsMovingPlatform && upper.IsMovingPlatform
            ? (lower.ColliderId < upper.ColliderId ? lower : upper)
            : (lower.IsMovingPlatform ? lower : upper);
        UnifiedCollisionHit opposite = mover.ColliderId == lower.ColliderId ? upper : lower;
        throw new CharacterCrushException(new CharacterCrushEvent(
            mover.ColliderId, opposite.ColliderId, axis, worldTimeSeconds, body));
    }
}
