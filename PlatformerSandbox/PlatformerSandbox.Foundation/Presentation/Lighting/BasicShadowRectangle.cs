using System;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// One deliberately simple rectangular shadow caster for the isolated lab.
/// Coordinates are normalized to the viewport (0..1). Does not own collision,
/// gameplay visibility, or any draw-order/depth policy.
/// </summary>
public readonly record struct BasicShadowRectangle(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public void Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y) ||
            !double.IsFinite(Width) || !double.IsFinite(Height) ||
            X < 0 || Y < 0 || Width <= 0 || Height <= 0 ||
            Right > 1 || Bottom > 1)
            throw new ArgumentOutOfRangeException(nameof(X),
                "Shadow rectangle must be a finite, positive rectangle inside normalized viewport bounds.");
    }

    public bool Contains(LightPoint point) =>
        point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    /// <summary>
    /// Reference visibility test for the GPU's slab intersection. A blocker
    /// casts only if the OPEN light-to-receiver segment passes through its
    /// interior. A light or receiver inside the same rectangle is not shadowed;
    /// self-occlusion and light-inside-volume require explicit 2.5D geometry
    /// in a later phase, rather than undefined hard-shadow behavior here.
    /// </summary>
    public bool Blocks(LightPoint light, LightPoint receiver)
    {
        Validate();
        if (!double.IsFinite(light.X) || !double.IsFinite(light.Y) ||
            !double.IsFinite(receiver.X) || !double.IsFinite(receiver.Y))
            throw new ArgumentOutOfRangeException(nameof(light));
        if (Contains(light) || Contains(receiver)) return false;
        double dx = receiver.X - light.X;
        double dy = receiver.Y - light.Y;
        double near = 0, far = 1;
        if (!Clip(light.X, dx, X, Right, ref near, ref far) ||
            !Clip(light.Y, dy, Y, Bottom, ref near, ref far)) return false;
        const double epsilon = 1e-7;
        return far > near + epsilon && near < 1 - epsilon && far > epsilon;
    }

    private static bool Clip(double origin, double delta, double low, double high,
        ref double near, ref double far)
    {
        if (Math.Abs(delta) < 1e-12)
            return origin > low && origin < high;
        double a = (low - origin) / delta;
        double b = (high - origin) / delta;
        near = Math.Max(near, Math.Min(a, b));
        far = Math.Min(far, Math.Max(a, b));
        return far >= near;
    }
}
