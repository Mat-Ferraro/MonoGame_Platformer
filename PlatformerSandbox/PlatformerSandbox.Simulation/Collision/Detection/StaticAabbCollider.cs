using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Collision.Detection;

/// <summary>
/// Immutable geometric obstacle with a stable nonnegative ID.
/// The caller owns collider registration and assigns IDs; this is not a
/// scene object or an authoritative physics body.
/// </summary>
public sealed class StaticAabbCollider
{
    public int Id { get; }
    public Aabb Bounds { get; }

    public StaticAabbCollider(int id, Aabb bounds)
    {
        if (id < 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        // Reject default(Aabb), which bypasses the geometry constructor.
        if (!bounds.IsValid)
            throw new ArgumentException("Collider requires a valid Aabb.", nameof(bounds));
        Id = id;
        Bounds = bounds;
    }
}
