using System;
using System.Collections.Generic;
using System.Linq;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Shared material entry point for runtime sprites and legacy independent artwork.</summary>
public static class SpatialSurfaceLighting
{
    public static bool CanPromote(LightingSurfaceOptions options, SceneLightingLayer layer) =>
        options.ReceivesLight &&
        (options.LightingMode == LightingSurfaceMode.Lit ||
         (options.LightingMode == LightingSurfaceMode.Inherit && layer.Mode != RenderLayerLightingMode.Unlit)) &&
        options.MaterialShading == ProductionSurfaceShading.Legacy;

    public static ProductionSurfaceSubmission Promote(string id, LightingSurfaceOptions options) =>
        ProductionSurfaceSubmission.From(id, options with
        {
            MaterialShading = ProductionSurfaceShading.Flat, ContactShadow = false,
            BackRimIntensity = options.BackRimOnOverlap ? options.BackRimIntensity : 0
        }) with { ExplicitLitOverride = options.LightingMode == LightingSurfaceMode.Lit };

    public static ProductionSurfaceSubmission RuntimeSprite(string id, SceneLightingLayer layer,
        LightChannel channel, LightPoint center, double minimumAmbient = 0) => new()
    {
        InstanceId = id, LayerId = layer.Id, Channel = channel, Center = center,
        Shading = ProductionSurfaceShading.Flat, ReceivesShadows = layer.ReceivesShadows,
        MinimumAmbient = minimumAmbient, BackRimIntensity = 0
    };

    // Conservative upper bound: a light whose center misses the receiver can still
    // light its edge. Spot cones and extended emitter samples are evaluated in the GPU;
    // the enclosing disk is used only for candidate selection, never shading.
    public static double Influence(RadialLight light, LightingViewport viewport,
        ProductionMaterialRegion region, double radiusScale)
    {
        if (region.IsEmpty || light.Intensity <= 0) return 0;
        if (light.Shape == LightShape.Directional) return light.Intensity;
        LightPoint p = viewport.Project(light.Position);
        double dx = p.X - Math.Clamp(p.X, region.X, (double)region.X + region.Width);
        double dy = p.Y - Math.Clamp(p.Y, region.Y, (double)region.Y + region.Height);
        double distance = Math.Sqrt(dx*dx + dy*dy) / viewport.PixelsPerUnit;
        distance = Math.Max(0, distance - AreaEmitterSampling.Extent(light));
        double radius = Math.Min(10000, light.Radius * radiusScale);
        double t = Math.Clamp(1 - distance / radius, 0, 1);
        return light.Intensity * t * t;
    }

    public static IReadOnlyList<RadialLight> Candidates(IReadOnlyList<RadialLight> lights,
        SceneLightingLayer layer, LightingViewport viewport, ProductionMaterialRegion region,
        double radiusScale) => lights.Where(l => LightLayerRouting.Illuminates(l, layer))
        .Select(l => (Light: l, Score: Influence(l, viewport, region, radiusScale)))
        .Where(x => x.Score > 0).OrderByDescending(x => x.Score)
        .ThenBy(x => x.Light.Id, StringComparer.Ordinal).Select(x => x.Light).ToArray();
}
