using System;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Projected 2D artwork rule, not collision or volumetric transmission.
/// For an externally illuminated visible wall, its first solid interval is
/// the visible face: only its downstream exit starts a receiver shadow.
/// Every other caster remains independently eligible to block that light.</summary>
public static class ReceiverWallShadowPolicy
{
    public static bool StartsAtExit(RadialLight light, LightingOccluder caster) =>
        caster.LitFaceLayer is { } layer &&
        light.VirtualZ >= caster.LitFaceDepthZ - 1e-7 &&
        LightLayerRouting.Illuminates(light, layer) &&
        LightLayerRouting.Occludes(light, caster);

    /// <summary>For either winding, an exit crosses from solid to exterior.</summary>
    public static bool IsExitEdge(LightPoint a, LightPoint b, double dx, double dy,
        double winding) => winding * ((b.Y-a.Y)*dx - (b.X-a.X)*dy) > 1e-12;
}
