using PlatformerSandbox.Foundation.Presentation.Lighting;

namespace PlatformerSandbox.Foundation.Performance;

/// <summary>Quality-only presets shared by manual labs and saved graphics settings.
/// Never changes optimization flags, display mode, pacing, or test workload.</summary>
public sealed record GraphicsQualityPreset(string Label, LightingQuality Quality,
    int LightScale, int MaskScale, int BloomScale, bool Adaptive)
{
    private static readonly GraphicsQualityPreset[] Presets =
    {
        new("ULTRA (100/100)", LightingQuality.High, 100, 100, 100, false),
        new("HIGH (75/100)", LightingQuality.High, 75, 100, 100, false),
        new("BALANCED (50/50)", LightingQuality.Balanced, 50, 50, 75, true),
        new("PERFORMANCE (25/50)", LightingQuality.Low, 25, 50, 50, true)
    };
    public static int Count => Presets.Length;
    public static GraphicsQualityPreset For(int index) => index >= 0 && index < Count
        ? Presets[index] : throw new System.ArgumentOutOfRangeException(nameof(index));

    public GraphicsPreferences Apply(GraphicsPreferences preferences) => preferences with
    {
        Quality=Quality, LightScale=LightScale, MaskScale=MaskScale,
        BloomScale=BloomScale, Adaptive=Adaptive, AreaSamples=0
    };

    public LightingQualityLabProfile Apply(LightingQualityLabProfile profile) => profile with
    {
        Number=0, Label="CUSTOM / QUALITY PRESET", LightScaleOverride=LightScale,
        MaskScaleOverride=MaskScale, BloomScale=BloomScale, Adaptive=Adaptive, AreaSamples=0
    };

    public static string CurrentLabel(LightingQuality quality, int lightScale, int maskScale,
        int bloomScale, bool adaptive, int areaSamples)
    {
        for(int i=0;i<Count;i++)
        {
            var p=For(i);
            if(p.Quality==quality && p.LightScale==lightScale && p.MaskScale==maskScale &&
               p.BloomScale==bloomScale && p.Adaptive==adaptive && areaSamples==0)
                return p.Label;
        }
        return "CUSTOM";
    }
}
