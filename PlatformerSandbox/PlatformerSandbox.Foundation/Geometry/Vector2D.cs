namespace PlatformerSandbox.Foundation.Geometry;

/// <summary>
/// Small, framework-independent world-space vector. Y is positive upward.
/// Each constructed component must be finite. The default value (0, 0) is valid.
/// </summary>
public readonly record struct Vector2D
{
    public double X { get; }
    public double Y { get; }

    public Vector2D(double x, double y)
    {
        if (!double.IsFinite(x))
            throw new ArgumentOutOfRangeException(nameof(x), "X must be finite.");
        if (!double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(y), "Y must be finite.");

        X = x;
        Y = y;
    }

    public static Vector2D Zero => new(0.0, 0.0);

    public static Vector2D operator +(Vector2D a, Vector2D b) =>
        new(a.X + b.X, a.Y + b.Y);

    public static Vector2D operator -(Vector2D a, Vector2D b) =>
        new(a.X - b.X, a.Y - b.Y);

    public static Vector2D operator -(Vector2D vector) =>
        new(-vector.X, -vector.Y);

    public static Vector2D operator *(Vector2D vector, double scale) =>
        new(vector.X * scale, vector.Y * scale);

    public static Vector2D operator *(double scale, Vector2D vector) => vector * scale;
}
