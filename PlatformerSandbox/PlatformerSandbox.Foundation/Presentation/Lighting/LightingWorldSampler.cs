using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Forward point-light approximation for optional lit sprite surfaces.</summary>
public static class LightingWorldSampler
{
    public static LightRgb Sample(LightPoint point, LightChannel channel,
        bool receivesShadows, string? ownOccluderId, LightingArtPreset art,
        IReadOnlyList<RadialLight> lights, IReadOnlyList<LightingOccluder> blockers,
        SceneLightingLayer? layer = null)
    {
        point.Validate(); art.Validate();
        ArgumentNullException.ThrowIfNull(lights);
        ArgumentNullException.ThrowIfNull(blockers);
        LightRgb total = art.Ambient * (1 - art.AtmosphericDarkness) *
            (layer?.AmbientScale ?? 1) * art.AmbientScaleFor(layer?.Id);
        foreach (RadialLight light in lights)
        {
            if (layer is null ? (light.Channels & channel) == 0 :
                !LightLayerRouting.Illuminates(light, layer)) continue;
            double attenuation = RadialLighting.Attenuation(
                light with { Radius = Math.Min(10000, light.Radius * art.LightRadiusScale) }, point);
            if (attenuation <= 0) continue;
            if (light.CastShadows && receivesShadows && layer?.ReceivesShadows != false &&
                IsShadowed(light, point, blockers, ownOccluderId))
                continue;
            total += light.Color * (attenuation * art.LightMultiplier);
        }
        // Match the lit-world layer exposure for independently shaded sprites.
        return (total * art.LightExposure).Clamp();
    }

    /// <summary>Front-facing flashes (such as a player's Pulse dash) may
    /// illuminate a silhouetted sprite without also exposing it to every
    /// ambient/background light. Returns ONLY eligible light contributions,
    /// with no global ambient; the silhouette's dark baseline is supplied by
    /// LightingSurfaceShading. Its own shadow hull never darkens its front face.</summary>
    public static LightRgb SampleSilhouetteReveal(LightPoint point, LightChannel channel,
        bool receivesShadows, string? ownOccluderId, LightingArtPreset art,
        IReadOnlyList<RadialLight> lights, IReadOnlyList<LightingOccluder> blockers,
        SceneLightingLayer layer)
    {
        point.Validate(); art.Validate();
        ArgumentNullException.ThrowIfNull(lights);
        ArgumentNullException.ThrowIfNull(blockers);
        ArgumentNullException.ThrowIfNull(layer);
        layer.Validate();
        LightRgb total = LightRgb.Black;
        foreach (RadialLight light in lights)
        {
            if (!light.RevealsSilhouettes || !LightLayerRouting.Illuminates(light, layer))
                continue;
            double attenuation = RadialLighting.Attenuation(
                light with { Radius = Math.Min(10000, light.Radius * art.LightRadiusScale) }, point);
            if (attenuation <= 0) continue;
            if (light.CastShadows && receivesShadows && layer.ReceivesShadows &&
                IsShadowed(light, point, blockers, ownOccluderId))
                continue;
            total += light.Color * (attenuation * art.LightMultiplier);
        }
        return (total * art.LightExposure).Clamp();
    }

    private static bool IsShadowed(RadialLight light, LightPoint point,
        IReadOnlyList<LightingOccluder> blockers, string? ownId)
    {
        // A sprite that casts shadows is shaded on its visible face as a *separate*
        // forward receiver. It cannot receive the blackness of its own blocker.
        foreach (LightingOccluder blocker in blockers)
        {
            if (blocker.Owner == ownId || !LightLayerRouting.Occludes(light, blocker)) continue;
            // A finite directional shadow must not darken the hero for the
            // whole level. Match the GPU world's authored projection reach.
            double directionLength = Math.Sqrt(light.Direction.X * light.Direction.X +
                light.Direction.Y * light.Direction.Y);
            LightPoint rayOrigin = light.Shape == LightShape.Directional
                ? new LightPoint(
                    point.X - light.Direction.X / directionLength * light.DirectionalShadowLength,
                    point.Y - light.Direction.Y / directionLength * light.DirectionalShadowLength)
                : light.Position;
            if (light.Shape == LightShape.Directional)
            {
                // GPU sweeps the closed polygon over the finite segment. Match
                // that coverage, even when both ray endpoints lie inside it.
                if (ShadowGeometry.Contains(blocker, point) || ShadowGeometry.Contains(blocker, rayOrigin))
                    return true;
                for (int i=0;i<blocker.Vertices.Length;i++)
                    if (ShadowGeometry.EdgesTouchOrCross(rayOrigin, point,
                        blocker.Vertices[i], blocker.Vertices[(i+1)%blocker.Vertices.Length])) return true;
            }
            else if (ShadowGeometry.IsOccluded(rayOrigin, point, new[] { blocker }))
                return true;
        }
        return false;
    }
}
