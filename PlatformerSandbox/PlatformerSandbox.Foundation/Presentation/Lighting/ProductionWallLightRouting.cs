using System;
using System.Collections.Generic;
using System.Linq;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Stage 15B2 presentation-only wall-light policy. A wall ignores ONLY its own
/// caster ID while other LDtk occluders retain normal per-light routing.
/// Layer render order is never used as a substitute for authored virtual Z.
/// </summary>
public static class ProductionWallLightRouting
{
    public const int MaxOwnerEdges = 128;

    public static IReadOnlyList<LightingOccluder> OwnWalls(ProductionSurfaceSubmission surface,
        IReadOnlyList<LightingOccluder> blockers)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(blockers);
        if (surface.CasterId is null) return Array.Empty<LightingOccluder>();
        var matches = blockers.Where(b => b.Owner == surface.CasterId).ToArray();
        if (matches.Length == 0 || matches.Any(b => b.LayerId != surface.LayerId || b.Vertices is null ||
                b.Vertices.Length is < 3 or > LightingOccluder.MaxVertices) ||
            matches.Select(b => b.Id).Distinct(StringComparer.Ordinal).Count() != matches.Length ||
            matches.Sum(b => b.Vertices.Length) > MaxOwnerEdges)
            throw new InvalidOperationException($"Artwork '{surface.InstanceId}' has missing, duplicate, wrong-layer or over-budget shadow contours.");
        return matches;
    }

    // Compatibility for callers that specifically expect a single contour.
    public static LightingOccluder? OwnWall(ProductionSurfaceSubmission surface,
        IReadOnlyList<LightingOccluder> blockers) => OwnWalls(surface, blockers).SingleOrDefault();

    public static bool BehindAny(ProductionSurfaceSubmission surface, RadialLight light,
        IReadOnlyList<LightingOccluder> walls) => walls.Any(w => Behind(surface, light, w));

    public static bool Behind(ProductionSurfaceSubmission surface,
        RadialLight light, LightingOccluder? ownWall)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(light);
        if (ownWall is null) return false; // Ordinary material panels are not walls.
        if (light.VirtualZ < surface.MaterialDepthZ - 1e-7) return true;
        return Math.Abs(light.VirtualZ - surface.MaterialDepthZ) <= 1e-7 &&
            surface.BackRimOnOverlap && light.Shape != LightShape.Directional &&
            ShadowGeometry.Contains(ownWall, light.Position);
    }

    public static IReadOnlyList<LightingOccluder> OtherBlockers(
        ProductionSurfaceSubmission surface, RadialLight light,
        IReadOnlyList<LightingOccluder> blockers)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(blockers);
        if (!light.CastShadows) return Array.Empty<LightingOccluder>();
        return blockers.Where(b => b.Owner != surface.CasterId &&
            LightLayerRouting.Occludes(light, b)).ToArray();
    }

    public static LightPoint Center(LightingOccluder wall)
    {
        ArgumentNullException.ThrowIfNull(wall);
        var v = wall.Vertices;
        return new LightPoint(v.Average(p => p.X), v.Average(p => p.Y));
    }
}
