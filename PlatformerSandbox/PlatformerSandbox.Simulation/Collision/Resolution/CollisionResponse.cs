using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Collision.Resolution;

/// <summary>
/// Pure, stateless and deterministic velocity response for cardinal AABB
/// contact normals. Contact detection owns time and geometry; this class
/// owns only the explicitly selected response policy.
/// </summary>
public static class CollisionResponse
{
    /// <summary>
    /// Applies a response to incoming velocity. For Slide, the inward normal
    /// component is set to zero; outward or tangential velocity is unchanged.
    /// Normals must be one of the four exact unit axis directions.
    /// </summary>
    public static Vector2D Apply(
        Vector2D incomingVelocity,
        Vector2D normal,
        CollisionResponseMode mode)
    {
        if (normal != new Vector2D(-1.0, 0.0) &&
            normal != new Vector2D(1.0, 0.0) &&
            normal != new Vector2D(0.0, -1.0) &&
            normal != new Vector2D(0.0, 1.0))
        {
            throw new ArgumentException("AABB collision normal must be an exact cardinal unit vector.", nameof(normal));
        }

        return mode switch
        {
            CollisionResponseMode.Stop => Vector2D.Zero,
            CollisionResponseMode.Slide => Slide(incomingVelocity, normal),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown collision response mode.")
        };
    }

    private static Vector2D Slide(Vector2D velocity, Vector2D normal)
    {
        double component = velocity.X * normal.X + velocity.Y * normal.Y;
        if (component >= 0.0)
            return velocity;
        // The normals are axis-aligned. Directly zeroing one component avoids
        // unnecessary arithmetic and preserves the tangent exactly.
        return normal.X != 0.0
            ? new Vector2D(0.0, velocity.Y)
            : new Vector2D(velocity.X, 0.0);
    }
}
