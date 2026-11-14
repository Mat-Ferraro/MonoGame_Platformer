namespace PlatformerSandbox.Foundation.Geometry;

/// <summary>
/// Axis-aligned box with strictly positive, finite width and height.
/// A box occupies [Min.X, Max.X] x [Min.Y, Max.Y] geometrically; contact
/// at a boundary is NOT positive-area overlap.
/// </summary>
public readonly record struct Aabb
{
    public Vector2D Min { get; }
    public Vector2D Max { get; }

    public double Left => Min.X;
    public double Right => Max.X;
    public double Bottom => Min.Y;
    public double Top => Max.Y;
    public double Width => Max.X - Min.X;
    public double Height => Max.Y - Min.Y;
    public bool IsValid => Max.X > Min.X && Max.Y > Min.Y &&
        double.IsFinite(Width) && double.IsFinite(Height);
    public Vector2D Center => new((Min.X * 0.5) + (Max.X * 0.5),
                                    (Min.Y * 0.5) + (Max.Y * 0.5));

    public Aabb(Vector2D min, Vector2D max)
    {
        if (max.X <= min.X || max.Y <= min.Y)
            throw new ArgumentException("Aabb must have strictly positive width and height.", nameof(max));
        if (!double.IsFinite(max.X - min.X) || !double.IsFinite(max.Y - min.Y))
            throw new ArgumentOutOfRangeException(nameof(max), "Box dimensions must be finite.");

        Min = min;
        Max = max;
    }

    public static Aabb FromCenterAndHalfExtents(Vector2D center, Vector2D halfExtents)
    {
        if (halfExtents.X <= 0.0 || halfExtents.Y <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(halfExtents), "Half extents must be positive.");
        return new Aabb(center - halfExtents, center + halfExtents);
    }

    public Aabb Translated(Vector2D displacement) =>
        new(Min + displacement, Max + displacement);
}
