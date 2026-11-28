namespace PlatformerSandbox.Foundation.Spatial;

/// <summary>Process preference plus scoped override for headless differential tests. Reference is the safe default.</summary>
public static class SpatialQueryPolicy
{
    private static readonly AsyncLocal<SpatialQueryMode?> Override=new();
    private static readonly SpatialQueryMode Startup=Enum.TryParse<SpatialQueryMode>(Environment.GetEnvironmentVariable("PS_PO05_MODE"),true,out var mode)&&Enum.IsDefined(mode)?mode:SpatialQueryMode.Reference;
    public static SpatialQueryMode? LabMode { get; set; }
    public static SpatialQueryMode Current=>Override.Value??LabMode??Startup;
    // Lab-only ablations; scoped tests and process settings keep all integrations enabled.
    public static bool LabCollision { get; set; }=true;
    public static bool LabRendering { get; set; }=true;
    public static bool LabTriggers { get; set; }=true;
    public static SpatialQueryMode Collision=>LabMode.HasValue&&!LabCollision&&!Override.Value.HasValue?SpatialQueryMode.Reference:Current;
    public static SpatialQueryMode Rendering=>LabMode.HasValue&&!LabRendering&&!Override.Value.HasValue?SpatialQueryMode.Reference:Current;
    public static SpatialQueryMode Triggers=>LabMode.HasValue&&!LabTriggers&&!Override.Value.HasValue?SpatialQueryMode.Reference:Current;
    public static void ResetLab(){ LabMode=null; LabCollision=LabRendering=LabTriggers=true; }
    public static IDisposable Use(SpatialQueryMode mode)
    {
        if(!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        var scope=new Scope(Override.Value); Override.Value=mode; return scope;
    }
    private sealed class Scope(SpatialQueryMode? previous):IDisposable
    { private bool _disposed; public void Dispose() { if(!_disposed) { Override.Value=previous; _disposed=true; } } }
}
