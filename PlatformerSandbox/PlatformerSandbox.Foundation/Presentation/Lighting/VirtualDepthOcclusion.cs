using System;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// A discrete authored depth axis independent of sprite draw order. Positive Z
/// is closer to the camera; negative Z is farther away. A rendering layer
/// (BG1, FG1, ...) is NOT a physical distance. This is a reference policy for
/// the later unified per-light shadow pass, not an automatic gameplay rule.
/// </summary>
public readonly record struct LightingDepthPoint(LightPoint XY, double Z)
{
    public void Validate()
    {
        XY.Validate();
        if (!double.IsFinite(Z) || Math.Abs(Z) > 10000)
            throw new ArgumentOutOfRangeException(nameof(Z));
    }
}

public static class VirtualDepthOcclusion
{
    /// <summary>
    /// Determines if a planar authored polygon blocks the segment connecting a
    /// 2.5D light and receiver. The same-plane case keeps the existing segment-
    /// vs-hull hard-shadow reference. A caster on the receiver plane never
    /// shadows its own outward-facing material surface.
    /// </summary>
    public static bool Blocks(LightingDepthPoint light, LightingDepthPoint receiver,
        LightingOccluder caster, double casterZ)
    {
        light.Validate(); receiver.Validate();
        ArgumentNullException.ThrowIfNull(caster);
        caster.Validate();
        if (!double.IsFinite(casterZ) || Math.Abs(casterZ) > 10000)
            throw new ArgumentOutOfRangeException(nameof(casterZ));
        const double epsilon = 1e-7;
        double interval = receiver.Z - light.Z;
        if (Math.Abs(interval) < epsilon)
        {
            if (Math.Abs(casterZ - light.Z) > epsilon) return false;
            return ShadowGeometry.IsOccluded(light.XY, receiver.XY, new[] { caster });
        }
        double t = (casterZ - light.Z) / interval;
        // Do not count a surface containing the receiver as its own shadow.
        if (t <= epsilon || t >= 1.0 - epsilon) return false;
        var hit = new LightPoint(light.XY.X + (receiver.XY.X - light.XY.X) * t,
            light.XY.Y + (receiver.XY.Y - light.XY.Y) * t);
        return ShadowGeometry.Contains(caster, hit);
    }
}
