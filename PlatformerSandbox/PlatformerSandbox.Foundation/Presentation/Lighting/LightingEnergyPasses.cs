using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Converts an authored linear-ish light gain into bounded RGB draw passes.
/// Each SpriteBatch tint channel is restricted to [0,1]. Decomposing the
/// contribution before the radial falloff is applied preserves the requested
/// energy, instead of prematurely discarding all gain above 1.
/// The render target ultimately clamps the summed result to its color range.
/// This is a modest SDR approximation, not an HDR lighting pipeline.
/// </summary>
public static class LightingEnergyPasses
{
    public static IReadOnlyList<LightRgb> Split(LightRgb color, double gain)
    {
        color.Validate(nameof(color));
        // RadialLight.Intensity <= 4, LightingArtPreset.LightMultiplier <= 3.
        if (!double.IsFinite(gain) || gain < 0 || gain > 12)
            throw new ArgumentOutOfRangeException(nameof(gain));

        int count = (int)Math.Ceiling(Math.Max(color.R,
            Math.Max(color.G, color.B)) * gain);
        if (count == 0) return Array.Empty<LightRgb>();

        var passes = new LightRgb[count];
        double red = color.R * gain;
        double green = color.G * gain;
        double blue = color.B * gain;
        for (int i = 0; i < count; i++)
            passes[i] = new LightRgb(
                Math.Clamp(red - i, 0, 1),
                Math.Clamp(green - i, 0, 1),
                Math.Clamp(blue - i, 0, 1));
        return passes;
    }
}
