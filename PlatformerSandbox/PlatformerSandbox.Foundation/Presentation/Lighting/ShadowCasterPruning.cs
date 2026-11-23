using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Conservative broad-phase for production shadow fans. Rejects only casters
/// whose axis-aligned bounds cannot reach the light's radial visibility disk.
/// The exact ShadowVisibility ray/segment algorithm remains authoritative.
/// Call every frame for moving lights and casters: no stale position cache.
/// </summary>
public static class ShadowCasterPruning
{
    // ShadowVisibility.BuildPolygon extends its falloff radius by 2% to avoid
    // clipping at the soft edge. Retain a small additional numerical margin;
    // never use the unextended light radius for this rejection test.
    private const double ConservativeRadiusScale = 1.021;

    public static IReadOnlyList<LightingOccluder> Filter(
        RadialLight light, IReadOnlyList<LightingOccluder> candidates)
    {
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(candidates);
        light.Validate();
        double radius = light.Radius * ConservativeRadiusScale;
        double radiusSquared = radius * radius;
        var result = new List<LightingOccluder>(candidates.Count);
        foreach (LightingOccluder caster in candidates)
        {
            if (caster is null)
                throw new ArgumentException("Null occluder.", nameof(candidates));
            // Preserve the original visibility builder's fail-fast validation,
            // including for casters discarded by this broad phase.
            caster.Validate();
            if (IntersectsBounds(caster, light.Position, radiusSquared))
                result.Add(caster);
        }
        return result;
    }

    private static bool IntersectsBounds(LightingOccluder caster,
        LightPoint center, double radiusSquared)
    {
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        foreach (LightPoint point in caster.Vertices)
        {
            minX = Math.Min(minX, point.X); maxX = Math.Max(maxX, point.X);
            minY = Math.Min(minY, point.Y); maxY = Math.Max(maxY, point.Y);
        }
        double x = Math.Clamp(center.X, minX, maxX);
        double y = Math.Clamp(center.Y, minY, maxY);
        double dx = x - center.X, dy = y - center.Y;
        // Inclusive, slightly expanded disk: borderline candidates survive and
        // are resolved by the exact polygon visibility function.
        return dx * dx + dy * dy <= radiusSquared;
    }
}
