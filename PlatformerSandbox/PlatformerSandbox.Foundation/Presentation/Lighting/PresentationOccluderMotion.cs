using System;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Presentation-only animation: it does not modify simulation/collision objects.</summary>
public enum PresentationOccluderMotionAxis { Static, Horizontal, Vertical }
public enum PresentationOccluderRotationMode { Static, Clockwise, Counterclockwise, Swing }

/// <summary>Translation in Y-up world coordinates and rotation in clockwise screen degrees.</summary>
public readonly record struct PresentationOccluderPose(double DeltaX, double DeltaY,
    double RotationDegrees)
{
    public LightPoint Transform(LightPoint point, LightPoint pivot)
    {
        double radians = RotationDegrees * Math.PI / 180.0;
        double c = Math.Cos(radians), s = Math.Sin(radians);
        double x = point.X - pivot.X, y = point.Y - pivot.Y;
        return new LightPoint(pivot.X + x * c + y * s + DeltaX,
                              pivot.Y - x * s + y * c + DeltaY);
    }
}

/// <summary>
/// Authored anchor is the artwork pivot or LightBlocker center, not its AABB midpoint.
/// Geometry is evaluated from immutable authored vertices, never integrated each frame.
/// </summary>
public readonly record struct PresentationOccluderMotion(
    LightPoint Anchor, PresentationOccluderMotionAxis Axis,
    double TravelWorld, double CyclesPerSecond,
    PresentationOccluderRotationMode Rotation,
    double AngularSpeedDegreesPerSecond, double SwingDegrees)
{
    public bool IsAnimated => Axis != PresentationOccluderMotionAxis.Static ||
                              Rotation != PresentationOccluderRotationMode.Static;

    public void Validate()
    {
        Anchor.Validate();
        if (!Enum.IsDefined(Axis) || !Enum.IsDefined(Rotation) ||
            !double.IsFinite(TravelWorld) || TravelWorld < 0 || TravelWorld > 10 ||
            !double.IsFinite(CyclesPerSecond) || CyclesPerSecond < 0 || CyclesPerSecond > 2 ||
            !double.IsFinite(AngularSpeedDegreesPerSecond) ||
            AngularSpeedDegreesPerSecond < 0 || AngularSpeedDegreesPerSecond > 360 ||
            !double.IsFinite(SwingDegrees) || SwingDegrees < 0 || SwingDegrees > 180 ||
            (Axis != PresentationOccluderMotionAxis.Static &&
              (TravelWorld <= 0 || CyclesPerSecond <= 0)) ||
            (Rotation is PresentationOccluderRotationMode.Clockwise or
              PresentationOccluderRotationMode.Counterclockwise &&
              AngularSpeedDegreesPerSecond <= 0) ||
            (Rotation == PresentationOccluderRotationMode.Swing &&
             (SwingDegrees <= 0 || CyclesPerSecond <= 0)))
            throw new ArgumentOutOfRangeException(nameof(Axis),
                "Invalid presentation occluder translation/rotation.");
    }

    public PresentationOccluderPose At(double elapsedSeconds)
    {
        Validate();
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        double phase = (elapsedSeconds * CyclesPerSecond) % 1.0;
        double offset = TravelWorld * Math.Sin(2 * Math.PI * phase);
        double degrees = Rotation switch
        {
            PresentationOccluderRotationMode.Clockwise =>
                (elapsedSeconds * AngularSpeedDegreesPerSecond) % 360.0,
            PresentationOccluderRotationMode.Counterclockwise =>
                -(elapsedSeconds * AngularSpeedDegreesPerSecond) % 360.0,
            PresentationOccluderRotationMode.Swing =>
                SwingDegrees * Math.Sin(2 * Math.PI * phase),
            _ => 0
        };
        return new PresentationOccluderPose(
            Axis == PresentationOccluderMotionAxis.Horizontal ? offset : 0,
            Axis == PresentationOccluderMotionAxis.Vertical ? offset : 0,
            degrees);
    }

    public LightingOccluder Apply(LightingOccluder authored, double elapsedSeconds)
    {
        ArgumentNullException.ThrowIfNull(authored);
        var pose = At(elapsedSeconds);
        LightPoint[] vertices = new LightPoint[authored.Vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = pose.Transform(authored.Vertices[i], Anchor);
        return authored with { Vertices = vertices };
    }
}
