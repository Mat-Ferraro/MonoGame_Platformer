using System;
using System.IO;
using System.Text.Json;
using PlatformerSandbox.Foundation.Presentation.Lighting;

namespace PlatformerSandbox.Foundation.Performance;

/// <summary>Application settings, distinct from benchmark and lab overrides. No GPU dependencies.</summary>
public sealed record GraphicsPreferences
{
    public int Version { get; init; } = 1;
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public bool Fullscreen { get; init; }
    public bool VSync { get; init; } = true;
    public int FpsLimit { get; init; }
    public int Profile { get; init; } = 1;
    public int LightScale { get; init; } = 100;
    public int MaskScale { get; init; } = 100;
    public int BloomScale { get; init; } = 100;
    public int AreaSamples { get; init; }
    public bool Adaptive { get; init; }
    public bool? StaticFieldsOverride { get; init; }
    public bool StaticShadowFields { get; init; }
    public GraphicsPreferences EnableAllOptimizations() => this with { Profile=2, StaticFieldsOverride=true };
    public bool AllEstablishedOptimizations => Profile!=1 && (StaticFieldsOverride ?? LightingQualityLabProfile.For(Profile).StaticFields);
    public LightingQuality Quality { get; init; } = LightingQuality.Balanced;
    public static bool ValidScale(int v) => v is 25 or 50 or 75 or 100;
    public bool IsValid => Version==1 && ((Width==1280 && Height==720)||(Width==1920 && Height==1080)||(Width==1600 && Height==900)) &&
        FpsLimit is 0 or 30 or 60 or 120 or 144 && Profile is >=1 and <=6 && ValidScale(LightScale) && ValidScale(MaskScale) && ValidScale(BloomScale) && AreaSamples is 0 or 1 or 3 or 5 or 7 or 9 && (!Adaptive || AreaSamples==0) && (!StaticShadowFields || (StaticFieldsOverride ?? LightingQualityLabProfile.For(Profile).StaticFields)) && Enum.IsDefined(Quality);
    public GraphicsPreferences WithProfile(int n)
    {
        var p=LightingQualityLabProfile.For(n);
        return this with { Profile=n, LightScale=p.Scale, MaskScale=p.Scale, BloomScale=p.BloomScale, Adaptive=p.Adaptive, AreaSamples=0, StaticFieldsOverride=null, StaticShadowFields=false };
    }
    public LightingQualityLabProfile RenderProfile => LightingQualityLabProfile.For(Profile) with { LightScaleOverride=LightScale, MaskScaleOverride=MaskScale, BloomScale=BloomScale, Adaptive=Adaptive, AreaSamples=AreaSamples, StaticFields=StaticFieldsOverride ?? LightingQualityLabProfile.For(Profile).StaticFields, StaticShadowFields=StaticShadowFields };
    public static GraphicsPreferences? Load(string path)
    {
        try { if (!File.Exists(path)) return null; var v=JsonSerializer.Deserialize<GraphicsPreferences>(File.ReadAllText(path)); return v?.IsValid==true?v:null; }
        catch (Exception e) when(e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { return null; }
    }
    public void Save(string path)
    {
        if (!IsValid) throw new InvalidOperationException("Invalid graphics preferences.");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp=path+".tmp";
        File.WriteAllText(temp,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true,IgnoreReadOnlyProperties=true}));
        File.Move(temp,path,true);
    }
}
