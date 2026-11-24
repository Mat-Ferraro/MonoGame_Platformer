using System;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Pure CPU reference for the bounded Phase 19 post-process controls. The
/// GPU executes the matching functions in L_BASIC_SurfaceLighting.fx.
/// No gameplay state, physics, or scene ownership is affected.
/// </summary>
public readonly record struct L19PostProcessPolicy(
    double Exposure, double BloomThreshold, double BloomStrength)
{
    public void Validate()
    {
        if (!double.IsFinite(Exposure) || Exposure is < 0.1 or > 5 ||
            !double.IsFinite(BloomThreshold) || BloomThreshold is < 0.05 or > 8 ||
            !double.IsFinite(BloomStrength) || BloomStrength is < 0 or > 3)
            throw new ArgumentOutOfRangeException(nameof(Exposure),
                "HDR exposure/threshold/bloom gain outside bounded presentation settings.");
    }

    public double Tone(double radiance)
    {
        Validate();
        if (!double.IsFinite(radiance)) throw new ArgumentOutOfRangeException(nameof(radiance));
        return 1 - Math.Exp(-Math.Max(0, radiance) * Exposure);
    }

    /// <summary>Soft-knee bright-pass ratio; zero if source radiance is zero.</summary>
    public double BloomRatio(double maxChannel)
    {
        Validate();
        if (!double.IsFinite(maxChannel) || maxChannel < 0)
            throw new ArgumentOutOfRangeException(nameof(maxChannel));
        if (maxChannel == 0) return 0;
        double knee = Math.Max(0.001, BloomThreshold * 0.4);
        double soft = Math.Clamp((maxChannel - BloomThreshold + knee) / (2 * knee), 0, 1);
        return (Math.Max(0, maxChannel - BloomThreshold) + soft * soft * knee) /
            maxChannel;
    }

    /// <summary>Threshold + horizontal + vertical + final scene composite.</summary>
    public static int BloomGpuPassCount(bool enabled, bool allocated) =>
        enabled && allocated ? 4 : 0;

    /// <summary>L20 three-level bloom pass estimate (extract, six blur passes,
    /// two downsamples, final composite). L19 retains its original contract.</summary>
    public static int MultiResolutionBloomGpuPassCount(bool enabled, bool allocated) =>
        enabled && allocated ? 10 : 0;
}
