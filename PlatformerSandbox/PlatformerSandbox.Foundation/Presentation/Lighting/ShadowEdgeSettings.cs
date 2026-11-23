using System;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Screen-space penumbra widths for production visibility masks. Hard is the
/// exact Phase 16 visibility result. Soft presets filter only already-built
/// visibility; they never alter ray intersections or blocker depth rules.
/// </summary>
public enum ShadowEdgePreset
{
    Hard = 0,
    Soft2Px = 1,
    Soft4Px = 2,
    Soft8Px = 3
}

/// <summary>
/// A pair of adjacent positive-distance Gaussian taps combined into one
/// bilinearly filtered sample. The matching sample at -Offset has equal weight.
/// </summary>
public readonly record struct ShadowGaussianPair(float Offset, float Weight);

/// <summary>
/// Normalized, symmetric nine-fetch-per-axis Gaussian kernel. Four paired taps
/// on each side plus the center reconstruct a discrete 17-tap kernel at 8px.
/// </summary>
public readonly record struct ShadowGaussianKernel(
    float CenterWeight,
    ShadowGaussianPair Pair0,
    ShadowGaussianPair Pair1,
    ShadowGaussianPair Pair2,
    ShadowGaussianPair Pair3)
{
    public double TotalWeight => CenterWeight + 2.0 *
        (Pair0.Weight + Pair1.Weight + Pair2.Weight + Pair3.Weight);

    /// <summary>Evaluate a 1-D filtered reference signal using linear texture samples.</summary>
    public double Evaluate(Func<double, double> linearSample, double position)
    {
        ArgumentNullException.ThrowIfNull(linearSample);
        double sum = CenterWeight * linearSample(position);
        sum += Pair0.Weight * (linearSample(position + Pair0.Offset) + linearSample(position - Pair0.Offset));
        sum += Pair1.Weight * (linearSample(position + Pair1.Offset) + linearSample(position - Pair1.Offset));
        sum += Pair2.Weight * (linearSample(position + Pair2.Offset) + linearSample(position - Pair2.Offset));
        sum += Pair3.Weight * (linearSample(position + Pair3.Offset) + linearSample(position - Pair3.Offset));
        return sum;
    }
}

public static class ShadowEdgeSettings
{
    public static ShadowEdgePreset Next(ShadowEdgePreset current) => current switch
    {
        ShadowEdgePreset.Hard => ShadowEdgePreset.Soft2Px,
        ShadowEdgePreset.Soft2Px => ShadowEdgePreset.Soft4Px,
        ShadowEdgePreset.Soft4Px => ShadowEdgePreset.Soft8Px,
        ShadowEdgePreset.Soft8Px => ShadowEdgePreset.Hard,
        _ => throw new ArgumentOutOfRangeException(nameof(current))
    };

    public static float WidthPixels(ShadowEdgePreset preset) => preset switch
    {
        ShadowEdgePreset.Hard => 0f,
        ShadowEdgePreset.Soft2Px => 2f,
        ShadowEdgePreset.Soft4Px => 4f,
        ShadowEdgePreset.Soft8Px => 8f,
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };

    public static string Label(ShadowEdgePreset preset) => preset switch
    {
        ShadowEdgePreset.Hard => "HARD",
        ShadowEdgePreset.Soft2Px => "SOFT 2PX",
        ShadowEdgePreset.Soft4Px => "SOFT 4PX",
        ShadowEdgePreset.Soft8Px => "SOFT 8PX",
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };

    /// <summary>No projected shadows in Low quality.</summary>
    public static float EffectiveWidth(ShadowEdgePreset preset, LightingFrameBudget budget)
        => budget.MaxShadowLights > 0 ? WidthPixels(preset) : 0f;

    private static readonly ShadowGaussianKernel Kernel2 = CreateGaussianKernel(2);
    private static readonly ShadowGaussianKernel Kernel4 = CreateGaussianKernel(4);
    private static readonly ShadowGaussianKernel Kernel8 = CreateGaussianKernel(8);

    /// <summary>
    /// Returns a normalized kernel with a radius of 2, 4, or 8 real screen
    /// pixels. Paired bilinear taps fill every pixel between center and radius;
    /// unlike the old sparse 9-point filter, they do not leave distinct bands.
    /// </summary>
    public static ShadowGaussianKernel GaussianKernel(float radiusPixels) => radiusPixels switch
    {
        2f => Kernel2,
        4f => Kernel4,
        8f => Kernel8,
        _ => throw new ArgumentOutOfRangeException(nameof(radiusPixels),
            "Gaussian filtering requires one of the 2/4/8-pixel soft presets.")
    };

    private static ShadowGaussianKernel CreateGaussianKernel(int radius)
    {
        // A 2.5-sigma cutoff retains a smooth, visibly feathered edge without
        // spreading much illumination deep into a fully occluded region.
        double sigma = radius / 2.5;
        double[] discrete = new double[radius + 1];
        double normalization = 0;
        for (int i = 0; i <= radius; i++)
        {
            discrete[i] = Math.Exp(-(i * (double)i) / (2 * sigma * sigma));
            normalization += i == 0 ? discrete[i] : 2 * discrete[i];
        }
        for (int i = 0; i <= radius; i++) discrete[i] /= normalization;

        ShadowGaussianPair PairAt(int index)
        {
            int a = index * 2 + 1;
            int b = a + 1;
            if (a > radius) return default;
            double wa = discrete[a];
            double wb = b <= radius ? discrete[b] : 0;
            double combined = wa + wb;
            return new ShadowGaussianPair(
                (float)((a * wa + b * wb) / combined), (float)combined);
        }

        return new ShadowGaussianKernel((float)discrete[0],
            PairAt(0), PairAt(1), PairAt(2), PairAt(3));
    }
}
