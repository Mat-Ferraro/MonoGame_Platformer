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
    public static LightingOccluder? OwnWall(ProductionSurfaceSubmission surface,
        IReadOnlyList<LightingOccluder> blockers)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(blockers);
        if (surface.CasterId is null) return null;
        LightingOccluder[] matches = blockers.Where(b =>
            string.Equals(b.Id, surface.CasterId, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1 || matches[0].LayerId != surface.LayerId ||
            matches[0].Vertices.Length != 4)
            throw new InvalidOperationException(
                "GPU casting artwork must have exactly one matching four-vertex LDtk hull on its render layer.");
        return matches[0];
    }

    public static bool Behind(ProductionSurfaceSubmission surface,
        RadialLight light, LightingOccluder? ownWall)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(light);
        if (ownWall is null) return false; // Ordinary material panels are not walls.
        if (light.VirtualZ < surface.MaterialDepthZ - 1e-7) return true;
        return Math.Abs(light.VirtualZ - surface.MaterialDepthZ) <= 1e-7 &&
            surface.BackRimOnOverlap && ShadowGeometry.Contains(ownWall, light.Position);
    }

    public static IReadOnlyList<LightingOccluder> OtherBlockers(
        ProductionSurfaceSubmission surface, RadialLight light,
        IReadOnlyList<LightingOccluder> blockers)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(blockers);
        if (!light.CastShadows) return Array.Empty<LightingOccluder>();
        return blockers.Where(b => b.Id != surface.CasterId &&
            LightLayerRouting.Occludes(light, b)).ToArray();
    }

    public static LightPoint Center(LightingOccluder wall)
    {
        ArgumentNullException.ThrowIfNull(wall);
        var v = wall.Vertices;
        return new LightPoint(v.Average(p => p.X), v.Average(p => p.Y));
    }
}
