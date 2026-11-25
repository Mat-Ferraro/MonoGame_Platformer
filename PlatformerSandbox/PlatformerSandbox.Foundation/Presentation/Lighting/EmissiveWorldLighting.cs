using System;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Presentation-owned light proxy, independent of bloom and surface radiance.</summary>
public static class EmissiveWorldLighting
{
    public static RadialLight? Create(string id, LightPoint position, LightingSurfaceOptions surface)
    {
        surface.Validate();
        if (surface.EmissiveLightRadius <= 0 || surface.EmissiveLightIntensity <= 0) return null;
        var light = new RadialLight
        {
            Id = "emissive:" + id, Owner = "emissive", Tag = "EMISSIVE", Position = position,
            Color = surface.EmissionColor, Radius = surface.EmissiveLightRadius,
            Intensity = surface.EmissiveLightIntensity, CastShadows = surface.EmissiveLightCastsShadows,
            EmitterCasterId = surface.CastsShadows ? id : null, VirtualZ = surface.MaterialDepthZ,
            Channels = LightChannel.All, Priority = 0
        };
        light.Validate();
        return light;
    }
}
