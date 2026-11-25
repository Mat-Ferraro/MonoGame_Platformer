using System;

namespace PlatformerSandbox.Foundation.Performance;

/// <summary>Process-lifetime A/B switches. Set before launch; never affect simulation rate.</summary>
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
    public static int ProductionLightingMask { get; } = ReadInt("PS_PO_LIGHT_MASK", 0, 0, 127);
    public static int RenderCap { get; } = ReadInt("PS_PO_RENDER_CAP", 0, 0, 1000);
    public static int InactiveCap { get; } = ReadInt("PS_PO_INACTIVE_CAP", 30, 1, 1000);
    public static bool ForceVariableLoop { get; } = Environment.GetEnvironmentVariable("PS_PO_VARIABLE_LOOP") == "1";
    public static bool InitialVSync { get; } = Enabled("PS_PO_VSYNC");
    private static int ReadInt(string name, int fallback, int min, int max) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out int value) && value >= min && value <= max ? value : fallback;
}
