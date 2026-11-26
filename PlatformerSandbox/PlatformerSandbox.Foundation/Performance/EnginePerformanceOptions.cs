using System;

namespace PlatformerSandbox.Foundation.Performance;

/// <summary>Process-lifetime A/B switches, with an explicit manual-lab-only lighting override. Never affect simulation rate.</summary>
public static class EnginePerformanceOptions
{
    private static bool Enabled(string name) => Environment.GetEnvironmentVariable(name) != "0";
    public static bool Snapshots { get; } = Enabled("PS_PO_SNAPSHOTS");
    public static bool HudAtlas { get; } = Enabled("PS_PO_HUD_ATLAS");
    public static bool ArtworkPreparation { get; } = Enabled("PS_PO_ARTWORK");
    public static bool DiagnosticRecords { get; } = Enabled("PS_PO_DIAGNOSTICS");
    public static bool LightingValidation { get; } = Enabled("PS_PO_LIGHT_VALIDATION");
    public static bool LightingBuffers { get; } = Enabled("PS_PO_LIGHT_BUFFERS");
    public static bool LightingSamples { get; } = Enabled("PS_PO_LIGHT_SAMPLES");
    public static bool LightingParameters { get; } = Enabled("PS_PO_LIGHT_PARAMETERS");
    public static bool SharedSamples { get; } = Enabled("PS_PO_B_SHARED_SAMPLES");
    public static bool HashedVisibility { get; } = Enabled("PS_PO_B_HASHED_VISIBILITY");
    public static bool VisibleLayers { get; } = Enabled("PS_PO_B_VISIBLE_LAYERS");
    public static bool PreparedMaterials { get; } = Enabled("PS_PO_B_PREPARED_MATERIALS");
    // Main game thread only. Host clears this on every successful scene change.
    // Automated benchmark and visual captures never select the manual lab.
    public static LightingQualityLabProfile? ManualLightingLab { get; set; }
    private static GraphicsPreferences? _savedGraphics;
    private static LightingQualityLabProfile? _savedProfile;
    public static GraphicsPreferences? SavedGraphics
    {
        get => _savedGraphics;
        set { _savedGraphics=value; _savedProfile=value?.RenderProfile; }
    }
    // Lab settings take precedence. A manual graphics selection is never used by automation.
    private static LightingQualityLabProfile? ActiveProfile => ManualLightingLab ?? _savedProfile;
    public static bool CameraConvergence { get; } = Enabled("PS_PO_10D_CAMERA_SNAP");
    // Opt-in until image parity and runtime validation on DesktopGL.
    private static int ProcessPo10LightScale { get; } = ReadScale("PS_PO_10_LIGHT_SCALE");
    public static int Po10LightScale => ActiveProfile is { } p ? p.LightScaleOverride ?? p.Scale : ProcessPo10LightScale;
    private static int ProcessPo10MaskScale { get; } = ReadScale("PS_PO_10_MASK_SCALE");
    public static int Po10MaskScale => ActiveProfile is { } p ? p.MaskScaleOverride ?? p.Scale : ProcessPo10MaskScale;
    private static bool ProcessPo10MaterialRegions { get; } = OptIn("PS_PO_10_MATERIAL_REGIONS");
    public static bool Po10MaterialRegions => ActiveProfile?.Enabled(1) ?? ProcessPo10MaterialRegions;
    private static bool ProcessPo10LightRegions { get; } = OptIn("PS_PO_10_LIGHT_REGIONS");
    public static bool Po10LightRegions => ActiveProfile?.Enabled(2) ?? ProcessPo10LightRegions;
    private static bool ProcessPo10SharedFields { get; } = OptIn("PS_PO_10_SHARED_FIELDS");
    public static bool Po10SharedFields => ActiveProfile?.Enabled(4) ?? ProcessPo10SharedFields;
    private static bool ProcessPo10SkipClears { get; } = OptIn("PS_PO_10_SKIP_CLEARS");
    public static bool Po10SkipClears => ActiveProfile?.Enabled(8) ?? ProcessPo10SkipClears;
    private static bool ProcessPo10CacheCapacity { get; } = OptIn("PS_PO_10_CACHE_CAPACITY");
    public static bool Po10CacheCapacity => ActiveProfile?.Enabled(16) ?? ProcessPo10CacheCapacity;
    // PO10B quality experiments: disabled by default; 100 means the original half-size bloom base.
    private static int ProcessPo10bBloomScale { get; } = ReadScale("PS_PO_10B_BLOOM_SCALE");
    public static int Po10bBloomScale => ActiveProfile?.BloomScale ?? ProcessPo10bBloomScale;
    private static bool ProcessPo10bAdaptiveSamples { get; } = OptIn("PS_PO_10B_ADAPTIVE");
    public static bool Po10bAdaptiveSamples => ActiveProfile?.Adaptive ?? ProcessPo10bAdaptiveSamples;
    private static bool ProcessPo10bStaticFields { get; } = OptIn("PS_PO_10B_STATIC_FIELDS");
    public static bool Po10bStaticFields => ActiveProfile?.StaticFields ?? ProcessPo10bStaticFields;
    private static bool ProcessStaticShadowFields { get; } = OptIn("PS_PO_10F_STATIC_SHADOWS");
    public static bool Po10fStaticShadowFields => ActiveProfile?.StaticShadowFields ?? ProcessStaticShadowFields;
    public static bool Po10fCasterBuffers { get; } = Enabled("PS_PO_10F_CASTER_BUFFERS");
    private static bool OptIn(string name) => Environment.GetEnvironmentVariable(name) == "1";
    private static int ReadScale(string name) => int.TryParse(Environment.GetEnvironmentVariable(name), out int n) && n is 25 or 50 or 75 or 100 ? n : 100;
    private static int ProcessAreaSamples { get; } = ReadAreaSamples();
    public static int AreaSamples => ActiveProfile?.AreaSamples ?? ProcessAreaSamples;
    private static int ReadAreaSamples() => int.TryParse(Environment.GetEnvironmentVariable("PS_PO_B_AREA_SAMPLES"), out int n) && n is 1 or 3 or 5 or 7 or 9 ? n : 0;
    public static string LightingProfile { get; } = Environment.GetEnvironmentVariable("PS_PO_B_LIGHT_PROFILE") ?? "Reference";
    public static int ProductionLightingMask => SavedGraphics is { } saved ? (saved.Profile == 1 ? 0 : 127) : ProcessProductionMask;
    private static int ProcessProductionMask { get; } = ReadInt("PS_PO_LIGHT_MASK",
        string.Equals(LightingProfile, "OptimizedCandidate", StringComparison.OrdinalIgnoreCase) ? 127 : 0, 0, 127);
    public static int RenderCap => SavedGraphics?.FpsLimit ?? ProcessRenderCap;
    private static int ProcessRenderCap { get; } = ReadInt("PS_PO_RENDER_CAP", 0, 0, 1000);
    public static int InactiveCap { get; } = ReadInt("PS_PO_INACTIVE_CAP", 30, 1, 1000);
    public static bool ForceVariableLoop { get; } = Environment.GetEnvironmentVariable("PS_PO_VARIABLE_LOOP") == "1";
    public static bool InitialVSync { get; } = Enabled("PS_PO_VSYNC");
    private static int ReadInt(string name, int fallback, int min, int max) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out int value) && value >= min && value <= max ? value : fallback;
}
