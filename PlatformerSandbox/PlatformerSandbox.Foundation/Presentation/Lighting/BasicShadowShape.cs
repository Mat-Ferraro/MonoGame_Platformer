using System;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Convex, explicitly authored lab shadow silhouette. The normalized rectangle
/// remains its editable bounds; Kind controls the actual silhouette. This is
/// cosmetic presentation data and never feeds collision or gameplay visibility.
/// </summary>
public enum BasicShadowShapeKind { Rectangle, Triangle, Trapezoid }

public readonly record struct BasicShadowShape(BasicShadowRectangle Bounds, BasicShadowShapeKind Kind)
{
    public void Validate()
    {
        Bounds.Validate();
        if (!Enum.IsDefined(Kind))
            throw new ArgumentOutOfRangeException(nameof(Kind));
    }

    /// <summary>
    /// Positive cross-product winding in screen coordinates (Y increases
    /// downward). Always convex, with 3 or 4 vertices.
    /// </summary>
    public LightPoint[] Vertices()
    {
        Validate();
        double x = Bounds.X, y = Bounds.Y, w = Bounds.Width, h = Bounds.Height;
        return Kind switch
        {
            BasicShadowShapeKind.Triangle =>
            [new(x + w / 2, y), new(x + w, y + h), new(x, y + h)],
            BasicShadowShapeKind.Trapezoid =>
            [new(x + w * .25, y), new(x + w * .75, y),
             new(x + w, y + h), new(x, y + h)],
            _ =>
            [new(x, y), new(x + w, y), new(x + w, y + h), new(x, y + h)]
        };
    }

    private static double Cross(LightPoint edgeStart, LightPoint edgeEnd, LightPoint point) =>
        (edgeEnd.X - edgeStart.X) * (point.Y - edgeStart.Y) -
        (edgeEnd.Y - edgeStart.Y) * (point.X - edgeStart.X);

    public bool Contains(LightPoint position)
    {
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y)) return false;
        LightPoint[] vertices = Vertices();
        for (int i = 0; i < vertices.Length; i++)
            if (Cross(vertices[i], vertices[(i + 1) % vertices.Length], position) < -1e-10)
                return false;
        return true;
    }

    /// <summary>
    /// Cyrus-Beck segment clipping against the convex polygon's inward half
    /// planes. Ignore light/receiver inside the same silhouette, consistent with
    /// the Stage-6 self-shadow convention. Open-segment contact is not a shadow.
    /// Mirrors the bounded four-edge GPU shader algorithm.
    /// </summary>
    public bool Blocks(LightPoint light, LightPoint receiver)
    {
        Validate();
        if (!double.IsFinite(light.X) || !double.IsFinite(light.Y) ||
            !double.IsFinite(receiver.X) || !double.IsFinite(receiver.Y))
            throw new ArgumentOutOfRangeException(nameof(light));
        // Retain exact prior rectangle semantics for all Stage-6/7 fixtures.
        if (Kind == BasicShadowShapeKind.Rectangle) return Bounds.Blocks(light, receiver);
        if (Contains(light) || Contains(receiver)) return false;
        LightPoint[] vertices = Vertices();
        double enter = 0, exit = 1;
        for (int i = 0; i < vertices.Length; i++)
        {
            LightPoint a = vertices[i], b = vertices[(i + 1) % vertices.Length];
            double signed = Cross(a, b, light);
            double gradient = (b.X - a.X) * (receiver.Y - light.Y) -
                              (b.Y - a.Y) * (receiver.X - light.X);
            if (Math.Abs(gradient) < 1e-12)
            {
                if (signed <= 1e-10) return false;
                continue;
            }
            double t = -signed / gradient;
            if (gradient > 0) enter = Math.Max(enter, t);
            else exit = Math.Min(exit, t);
            if (exit < enter) return false;
        }
        const double eps = 1e-7;
        return exit > enter + eps && enter < 1 - eps && exit > eps;
    }
}
