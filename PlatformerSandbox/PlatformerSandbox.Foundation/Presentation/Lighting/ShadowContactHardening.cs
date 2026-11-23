using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Phase 17C: a compact polar first-hit profile derived from the SAME
/// star-shaped visibility fan used to rasterize the hard shadow mask.
/// No separate shadow raycaster, static-object assumptions, or GPU dependency.
/// Distances are normalized to the fan reach, encoded as UNorm16, and can be
/// sampled with POINT filtering at angular texel centers.
/// </summary>
public static class ShadowContactHardening
{
    public const int ProfileResolution = 1024;
    private const double TwoPi = 2.0 * Math.PI;

    public static ushort[] BuildProfile(LightPoint source,
        IReadOnlyList<LightPoint> visibleBoundary, double fanReach,
        int samples = ProfileResolution)
    {
        source.Validate();
        ArgumentNullException.ThrowIfNull(visibleBoundary);
        if (!double.IsFinite(fanReach) || fanReach <= 0)
            throw new ArgumentOutOfRangeException(nameof(fanReach));
        if (samples is < 64 or > ProfileResolution)
            throw new ArgumentOutOfRangeException(nameof(samples));
        var result = new ushort[samples];
        FillProfile(source, visibleBoundary, fanReach, result);
        return result;
    }

    /// <summary>Allocation-conscious production path; callers reuse the buffer.</summary>
    public static void FillProfile(LightPoint source,
        IReadOnlyList<LightPoint> visibleBoundary, double fanReach, Span<ushort> result)
    {
        source.Validate();
        ArgumentNullException.ThrowIfNull(visibleBoundary);
        if (!double.IsFinite(fanReach) || fanReach <= 0)
            throw new ArgumentOutOfRangeException(nameof(fanReach));
        int samples = result.Length;
        if (samples is < 64 or > ProfileResolution)
            throw new ArgumentOutOfRangeException(nameof(result));
        result.Fill(ushort.MaxValue);
        if (visibleBoundary.Count < 3) return;

        // ShadowVisibility.BuildPolygon produces counterclockwise points sorted
        // by polar angle. Do not reorder them: adjacent endpoints form the
        // exact triangle-fan boundary rasterized into the hard visibility mask.
        int n = visibleBoundary.Count;
        var angles = new double[n];
        for (int j = 0; j < n; j++)
        {
            LightPoint vertex = visibleBoundary[j];
            vertex.Validate();
            angles[j] = Normalize(Math.Atan2(vertex.Y - source.Y,
                vertex.X - source.X));
            if (j > 0 && angles[j] < angles[j - 1] - 1e-9)
                throw new ArgumentException("Boundary must be in increasing polar-angle order.",
                    nameof(visibleBoundary));
        }

        for (int i = 0; i < samples; i++)
        {
            double theta = (i + 0.5) * TwoPi / samples;
            int next = Array.BinarySearch(angles, theta);
            if (next < 0) next = ~next;
            int previous = (next + n - 1) % n;
            if (next == n) next = 0;
            LightPoint a = visibleBoundary[previous];
            LightPoint b = visibleBoundary[next];
            double ux = a.X - source.X, uy = a.Y - source.Y;
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double dx = Math.Cos(theta), dy = Math.Sin(theta);
            double det = dx * ey - dy * ex;
            // Degenerate near-parallel segments occur at certain light/vertex
            // alignments. The min endpoint reach is conservative (hardens, not
            // invents, a shadow). Ignore a NaN/negative intersection.
            double distance = Math.Abs(det) > 1e-12
                ? (ux * ey - uy * ex) / det
                : Math.Min(Math.Sqrt(ux * ux + uy * uy),
                    Math.Sqrt((b.X - source.X) * (b.X - source.X) +
                              (b.Y - source.Y) * (b.Y - source.Y)));
            if (!double.IsFinite(distance) || distance < 0) distance = fanReach;
            result[i] = (ushort)Math.Round(Math.Clamp(distance / fanReach, 0, 1) * ushort.MaxValue);
        }
    }

    public static double ContactDistancePixels(ushort hit, double fanReachPixels) =>
        hit * fanReachPixels / ushort.MaxValue;

    /// <summary>
    /// Bounded penumbra. A small antialiasing radius avoids a hard seam at a
    /// silhouette. The remaining width grows with the receiver's distance
    /// BEYOND the blocker, relative to light-to-blocker distance.
    /// </summary>
    public static double RadiusPixels(double receiverDistancePixels,
        double blockerDistancePixels, double emitterRadiusPixels, double maxRadiusPixels)
    {
        if (!double.IsFinite(receiverDistancePixels) || receiverDistancePixels < 0 ||
            !double.IsFinite(blockerDistancePixels) || blockerDistancePixels < 0 ||
            !double.IsFinite(emitterRadiusPixels) || emitterRadiusPixels < 0 ||
            !double.IsFinite(maxRadiusPixels) || maxRadiusPixels < 0)
            throw new ArgumentOutOfRangeException(nameof(receiverDistancePixels));
        if (maxRadiusPixels == 0) return 0;
        double separation = Math.Max(0, receiverDistancePixels - blockerDistancePixels);
        double projected = emitterRadiusPixels * separation / Math.Max(2, blockerDistancePixels);
        return Math.Min(maxRadiusPixels, Math.Max(Math.Min(.75, maxRadiusPixels), projected));
    }

    private static double Normalize(double angle)
    {
        angle %= TwoPi;
        return angle < 0 ? angle + TwoPi : angle;
    }
}

/// <summary>How the production renderer chooses its shadow filter.</summary>
public enum ShadowFilterMode
{
    UniformGaussian = 0,
    ContactHardening = 1
}

/// <summary>Manual edge width or quality-driven automatic selection.</summary>
public enum ShadowQualityPolicy
{
    Manual = 0,
    FollowLightingQuality = 1
}

public static class ShadowContactControls
{
    public static ShadowFilterMode Next(ShadowFilterMode mode) => mode switch
    {
        ShadowFilterMode.UniformGaussian => ShadowFilterMode.ContactHardening,
        ShadowFilterMode.ContactHardening => ShadowFilterMode.UniformGaussian,
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };

    public static ShadowQualityPolicy Next(ShadowQualityPolicy policy) => policy switch
    {
        ShadowQualityPolicy.Manual => ShadowQualityPolicy.FollowLightingQuality,
        ShadowQualityPolicy.FollowLightingQuality => ShadowQualityPolicy.Manual,
        _ => throw new ArgumentOutOfRangeException(nameof(policy))
    };

    public static ShadowEdgePreset QualityPreset(LightingQuality quality) => quality switch
    {
        LightingQuality.Low => ShadowEdgePreset.Hard,
        LightingQuality.Balanced => ShadowEdgePreset.Soft4Px,
        LightingQuality.High => ShadowEdgePreset.Soft8Px,
        _ => throw new ArgumentOutOfRangeException(nameof(quality))
    };

    public static float NextEmitter(float current) => current switch
    {
        2f => 4f,
        4f => 8f,
        8f => 12f,
        12f => 2f,
        _ => throw new ArgumentOutOfRangeException(nameof(current))
    };
}
