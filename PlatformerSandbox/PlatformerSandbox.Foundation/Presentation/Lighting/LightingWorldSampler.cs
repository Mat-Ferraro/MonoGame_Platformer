using PlatformerSandbox.Foundation.Performance;
using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Forward point-light approximation for optional lit sprite surfaces.</summary>
public static class LightingWorldSampler
{
    // Two radii are intentional: attenuation uses the art radius scale, while
    // the existing shadow test uses the original authored sample geometry.
    public readonly record struct PreparedSample(RadialLight Original, RadialLight? AttenuationLight);
    public static IReadOnlyList<PreparedSample> Prepare(IReadOnlyList<RadialLight> lights, LightingArtPreset art)
    {
        var samples = new List<PreparedSample>();
        foreach (RadialLight light in AreaEmitterSampling.Expand(lights, AreaEmitterSampling.MaximumSamples))
            samples.Add(new PreparedSample(light, light with { Radius = Math.Min(10000, light.Radius * art.LightRadiusScale) }));
        return samples.AsReadOnly();
    }
    private static IEnumerable<PreparedSample> ReferenceSamples(IReadOnlyList<RadialLight> lights, LightingArtPreset art)
    {
        foreach (RadialLight light in AreaEmitterSampling.Expand(lights, AreaEmitterSampling.MaximumSamples))
            yield return new PreparedSample(light, null);
    }
    public static LightRgb Sample(LightPoint point, LightChannel channel,
        bool receivesShadows, string? ownOccluderId, LightingArtPreset art,
        IReadOnlyList<RadialLight> lights, IReadOnlyList<LightingOccluder> blockers,
        SceneLightingLayer? layer = null, IReadOnlyList<PreparedSample>? prepared = null)
    {
        point.Validate(); art.Validate();
        ArgumentNullException.ThrowIfNull(lights);
        ArgumentNullException.ThrowIfNull(blockers);
        LightRgb total = art.Ambient * (1 - art.AtmosphericDarkness) *
            (layer?.AmbientScale ?? 1) * art.AmbientScaleFor(layer?.Id);
        foreach (PreparedSample sample in (IEnumerable<PreparedSample>?)prepared ?? ReferenceSamples(lights, art))
        {
            RadialLight light = sample.Original;
            if (layer is null ? (light.Channels & channel) == 0 :
                !LightLayerRouting.Illuminates(light, layer)) continue;
            double attenuation = RadialLighting.Attenuation(
                sample.AttenuationLight ?? (light with { Radius = Math.Min(10000, light.Radius * art.LightRadiusScale) }), point);
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
        SceneLightingLayer layer, IReadOnlyList<PreparedSample>? prepared = null)
    {
        point.Validate(); art.Validate();
        ArgumentNullException.ThrowIfNull(lights);
        ArgumentNullException.ThrowIfNull(blockers);
        ArgumentNullException.ThrowIfNull(layer);
        layer.Validate();
        LightRgb total = LightRgb.Black;
        foreach (PreparedSample sample in (IEnumerable<PreparedSample>?)prepared ?? ReferenceSamples(lights, art))
        {
            RadialLight light = sample.Original;
            if (!light.RevealsSilhouettes || !LightLayerRouting.Illuminates(light, layer))
                continue;
            double attenuation = RadialLighting.Attenuation(
                sample.AttenuationLight ?? (light with { Radius = Math.Min(10000, light.Radius * art.LightRadiusScale) }), point);
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
        double directionLength = Math.Sqrt(light.Direction.X * light.Direction.X +
            light.Direction.Y * light.Direction.Y);
        LightPoint rayOrigin = light.Shape == LightShape.Directional
            ? new LightPoint(
                point.X - light.Direction.X / directionLength * light.DirectionalShadowLength,
                point.Y - light.Direction.Y / directionLength * light.DirectionalShadowLength)
            : light.Position;
        foreach (LightingOccluder blocker in blockers)
        {
            if (blocker.Owner == ownId || !LightLayerRouting.Occludes(light, blocker)) continue;
            // A finite directional shadow must not darken the hero for the
            // whole level. Match the GPU world's authored projection reach.
            if (ReceiverWallShadowPolicy.StartsAtExit(light, blocker))
            {
                // Use the same downstream-edge rule as the production mask.
                double reach = Math.Sqrt(ShadowGeometry.DistanceSquared(rayOrigin, point));
                if (reach <= 1e-9) continue;
                double dx = (point.X-rayOrigin.X)/reach, dy = (point.Y-rayOrigin.Y)/reach;
                double winding = Math.Sign(ShadowGeometry.SignedDoubleArea(blocker.Vertices));
                for (int i=0; i<blocker.Vertices.Length; i++)
                {
                    LightPoint a = blocker.Vertices[i], b = blocker.Vertices[(i+1)%blocker.Vertices.Length];
                    if (ReceiverWallShadowPolicy.IsExitEdge(a,b,dx,dy,winding) &&
                        ShadowVisibility.RaySegmentIntersection(rayOrigin,dx,dy,a,b,out double hit) &&
                        hit <= reach - 1e-9) return true;
                }
                continue;
            }
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
            else if (EnginePerformanceOptions.LightingValidation ? ShadowGeometry.IsOccludedBy(rayOrigin, point, blocker) : ShadowGeometry.IsOccluded(rayOrigin, point, new[] { blocker }))
                return true;
        }
        return false;
    }
}
