using System;
namespace PlatformerSandbox.Foundation.Presentation.Lighting;
public static class PresentationLightPulse
{
    public static RadialLight At(RadialLight source, double seconds)
    {
        source.Validate();
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        if (source.PulseDepth == 0 && source.PulseTint is null) return source;
        double t = .5-.5*Math.Cos(2*Math.PI*((seconds*source.PulseHz)%1));
        LightRgb tint = source.PulseTint is LightRgb target ? source.Color*(1-t)+target*t : source.Color;
        return source with { Intensity = source.Intensity*(1-source.PulseDepth*t), Color = tint };
    }
}
