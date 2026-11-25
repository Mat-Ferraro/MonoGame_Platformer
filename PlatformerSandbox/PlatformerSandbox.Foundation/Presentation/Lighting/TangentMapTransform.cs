using System;
namespace PlatformerSandbox.Foundation.Presentation.Lighting;
/// <summary>Screen-space tangent vector, positive X right and Y down; reflect before clockwise rotation.</summary>
public readonly record struct TangentMapTransform(double Cos, double Sin, double SignX, double SignY)
{
    public static TangentMapTransform From(double degrees, bool flipX, bool flipY)
    {
        if (!double.IsFinite(degrees)) throw new ArgumentOutOfRangeException(nameof(degrees));
        double a=degrees*Math.PI/180;
        return new(Math.Cos(a),Math.Sin(a),flipX?-1:1,flipY?-1:1);
    }
    public LightPoint Apply(LightPoint xy) => new(Cos*xy.X*SignX-Sin*xy.Y*SignY,
        Sin*xy.X*SignX+Cos*xy.Y*SignY);
}
