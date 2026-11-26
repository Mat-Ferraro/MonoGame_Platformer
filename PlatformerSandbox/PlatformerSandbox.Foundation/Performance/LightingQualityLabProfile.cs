namespace PlatformerSandbox.Foundation.Performance;

/// <summary>Manual lab-only comparison presets. Reference means PO10A experiments off,
/// with the seven established lighting optimizations still enabled (mask 127).</summary>
public sealed record LightingQualityLabProfile(int Number, string Label, int Scale,
    bool Optimized, int BloomScale, bool Adaptive, bool StaticFields)
{
    public int? LightScaleOverride { get; init; }
    public int? MaskScaleOverride { get; init; }
    public int AreaSamples { get; init; }
    public int? OptimizationMask { get; init; }
    public bool StaticShadowFields { get; init; }
    public bool AllRuntimeOptimizations => (OptimizationMask ?? (Optimized ? 31 : 0)) == 31 && StaticFields && StaticShadowFields;
    /// <summary>Quality-neutral lab switches only. Do not alter resolution or sampling.</summary>
    public LightingQualityLabProfile EnableAllOptimizations() => this with
    { Number=0, Label="CUSTOM / ALL OPTIMIZATIONS", OptimizationMask=31, StaticFields=true, StaticShadowFields=true };
    public bool Enabled(int bit) => ((OptimizationMask ?? (Optimized ? 31 : 0)) & bit) != 0;
    public static LightingQualityLabProfile For(int number) => number switch
    {
        1 => new(1, "REFERENCE 100%", 100, false, 100, false, false),
        2 => new(2, "OPTIMIZED 100%", 100, true, 100, false, false),
        3 => new(3, "OPTIMIZED 75%", 75, true, 100, false, false),
        4 => new(4, "OPTIMIZED 50%", 50, true, 100, false, false),
        5 => new(5, "OPTIMIZED 25%", 25, true, 100, false, false),
        6 => new(6, "50% + ADAPTIVE / BLOOM / STATIC", 50, true, 50, true, true),
        _ => throw new System.ArgumentOutOfRangeException(nameof(number))
    };
}
