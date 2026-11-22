using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Material appearance is independent of scene order, light layer routing, and
/// the object's shadow-caster flag. A material profile never participates in physics.
/// </summary>
public enum SurfaceLightResponse { Unlit, Flat, NormalMapped, Rim, Hybrid }

public sealed record SurfaceMaterialProfile
{
    public string Id { get; init; } = "";
    public SurfaceLightResponse Response { get; init; } = SurfaceLightResponse.Flat;
    public double Ambient { get; init; } = .16;
    public double NormalStrength { get; init; } = 1;
    public double RimStrength { get; init; } = 0;
    public double SpecularStrength { get; init; } = 0;
    public double SpecularPower { get; init; } = 16;
    public double EmissionStrength { get; init; } = 0;
    public double LightMaskStrength { get; init; } = 1;
    public string Albedo { get; init; } = "SlateAlbedo.png";
    public string? Normal { get; init; }
    public string? Emission { get; init; }
    public string? Mask { get; init; }
    public string? Edge { get; init; }

    public void Validate()
    {
        if (!SceneLightingLayer.IsId(Id) || !Enum.IsDefined(Response))
            throw new ArgumentException("Invalid surface material ID/response.");
        Check(Ambient, 0, 2, nameof(Ambient));
        Check(NormalStrength, 0, 2, nameof(NormalStrength));
        Check(RimStrength, 0, 4, nameof(RimStrength));
        Check(SpecularStrength, 0, 2, nameof(SpecularStrength));
        Check(SpecularPower, 1, 128, nameof(SpecularPower));
        Check(EmissionStrength, 0, 4, nameof(EmissionStrength));
        Check(LightMaskStrength, 0, 1, nameof(LightMaskStrength));
        CheckMap(Albedo, true);
        CheckMap(Normal);
        CheckMap(Emission);
        CheckMap(Mask);
        CheckMap(Edge);
    }

    private static void Check(double value, double min, double max, string name)
    {
        if (!double.IsFinite(value) || value < min || value > max)
            throw new ArgumentOutOfRangeException(name);
    }

    private static void CheckMap(string? value, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) throw new ArgumentException("Albedo must be specified.");
            return;
        }
        if (value.Length > 100 || value.Contains("..", StringComparison.Ordinal) ||
            value.Contains('/') || value.Contains('\\') || value.Contains(':') ||
            !value.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Material maps must be local PNG filenames.");
    }
}

public sealed record SurfaceMaterialManifest
{
    public int Version { get; init; } = 1;
    public SurfaceMaterialProfile[] Materials { get; init; } = Array.Empty<SurfaceMaterialProfile>();
}

public sealed class SurfaceMaterialCatalog
{
    public IReadOnlyList<SurfaceMaterialProfile> Materials { get; }
    private SurfaceMaterialCatalog(SurfaceMaterialManifest manifest)
    {
        if (manifest.Version != 1 || manifest.Materials is null ||
            manifest.Materials.Length is < 1 or > 32)
            throw new ArgumentException("Material manifest requires version 1 and 1-32 profiles.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (SurfaceMaterialProfile material in manifest.Materials)
        {
            if (material is null) throw new ArgumentException("Null material profile.");
            material.Validate();
            if (!ids.Add(material.Id)) throw new ArgumentException("Duplicate material ID " + material.Id);
        }
        Materials = Array.AsReadOnly(manifest.Materials.ToArray());
    }
    public static SurfaceMaterialCatalog FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        var manifest = JsonSerializer.Deserialize<SurfaceMaterialManifest>(json, options)
            ?? throw new JsonException("Material manifest was empty.");
        return new SurfaceMaterialCatalog(manifest);
    }
}

/// <summary>
/// GPU-facing decision rules. Keeping this pure makes material policies testable
/// without constructing an XNA device; shadows, depth, and visibility remain in
/// the independent scene/light renderer.
/// </summary>
public static class SurfaceMaterialRules
{
    public static bool ReceivesDiffuse(SurfaceLightResponse mode) => mode is
        SurfaceLightResponse.Flat or SurfaceLightResponse.NormalMapped or SurfaceLightResponse.Hybrid;
    public static bool ReceivesRim(SurfaceLightResponse mode) => mode is
        SurfaceLightResponse.Rim or SurfaceLightResponse.Hybrid;
    public static bool ReceivesNormal(SurfaceLightResponse mode) => mode is
        SurfaceLightResponse.NormalMapped or SurfaceLightResponse.Hybrid;
    public static bool ReceivesDirectLight(SurfaceLightResponse mode) => mode != SurfaceLightResponse.Unlit;
}
