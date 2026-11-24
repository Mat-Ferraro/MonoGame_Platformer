using System;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

public enum ReceiverProjectionMode { Legacy, Flat, Clipped, Folded }

/// <summary>Opt-in 2.5D receiver. World Y is up; the seam is stationary in world space.
/// Folded is an artistic affine reprojection, not a volumetric shadow solver.</summary>
public sealed record SurfaceReceiverProjection
{
    public ReceiverProjectionMode Mode { get; init; } = ReceiverProjectionMode.Legacy;
    public double SeamY { get; init; }
    public double BandWorld { get; init; } = .75;
    public double Compression { get; init; } = .3;
    public double Shear { get; init; } = .15;
    public double PitchDegrees { get; init; } = 90;
    public double FacePitchDegrees { get; init; }
    public double OrientationStrength { get; init; } = .8;
    public bool Active => Mode != ReceiverProjectionMode.Legacy;
    public void Validate()
    {
        if (!Enum.IsDefined(Mode) || !double.IsFinite(SeamY) ||
            !double.IsFinite(BandWorld) || BandWorld is < .05 or > 20 ||
            !double.IsFinite(Compression) || Compression is < .1 or > 1 ||
            !double.IsFinite(Shear) || Shear is < -2 or > 2 ||
            !double.IsFinite(PitchDegrees) || PitchDegrees is < -180 or > 180 ||
            !double.IsFinite(FacePitchDegrees) || FacePitchDegrees is < -180 or > 180 ||
            !double.IsFinite(OrientationStrength) || OrientationStrength is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(Mode), "Invalid receiver projection settings.");
    }
    public LightPoint ProjectShadow(LightPoint point)
    {
        if (Mode != ReceiverProjectionMode.Folded) return point;
        double down = SeamY - point.Y;
        return new LightPoint(point.X + Shear * down, SeamY - Compression * down);
    }
    public LightPoint UnprojectShadow(LightPoint point)
    {
        if (Mode != ReceiverProjectionMode.Folded) return point;
        double down = (SeamY - point.Y) / Compression;
        return new LightPoint(point.X - Shear * down, SeamY - down);
    }
    public bool ReceivesProjectedShadow(LightPoint point) => Mode switch
    {
        ReceiverProjectionMode.Clipped => false,
        ReceiverProjectionMode.Folded => point.Y <= SeamY && point.Y >= SeamY - BandWorld,
        _ => true
    };
    public double Incidence(double towardX, double towardY, double towardZ, bool floorBand)
    {
        double length = Math.Sqrt(towardX*towardX + towardY*towardY + towardZ*towardZ);
        if (length < 1e-9) return 1;
        double angle = (floorBand ? PitchDegrees : FacePitchDegrees) * Math.PI / 180;
        double lambert = Math.Clamp((Math.Sin(angle)*towardY + Math.Cos(angle)*towardZ)/length, 0, 1);
        return 1 - OrientationStrength + OrientationStrength * lambert;
    }
}
