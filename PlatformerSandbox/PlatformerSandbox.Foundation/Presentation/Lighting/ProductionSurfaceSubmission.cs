using System;
using System.Collections.Generic;
using System.Linq;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Explicit per-surface production GPU material selection. Legacy surfaces never enter
/// the per-sprite pass. This initial bridge deliberately forbids projected-shadow
/// receivers/casters; they must retain the existing layered/forward shadow path.
/// </summary>
public enum ProductionSurfaceShading { Legacy, Flat, Normal, Rim, Hybrid }

/// <summary>Presentation draw contract, never a simulation or collider property.</summary>
public sealed record ProductionSurfaceSubmission
{
    public string InstanceId { get; init; } = "";
    public string LayerId { get; init; } = "PLAYER";
    public LightChannel Channel { get; init; } = LightChannel.Terrain;
    public string? CasterId { get; init; }
    public LightPoint? Center { get; init; }
    public ProductionSurfaceShading Shading { get; init; } = ProductionSurfaceShading.Legacy;
    public SurfaceReceiverProjection Receiver { get; init; } = new();
    public bool ReceivesShadows { get; init; }
    public bool CastsShadows { get; init; }
    public string? NormalMap { get; init; }
    public string? EdgeMap { get; init; }
    public double MapRotationDegrees { get; init; }
    public bool MapFlipX { get; init; }
    public bool MapFlipY { get; init; }
    public bool ExplicitLitOverride { get; init; }
    public double NormalStrength { get; init; } = 1;
    public double RimStrength { get; init; } = 1;
    public double MaterialDepthZ { get; init; }
    public bool BackRimOnOverlap { get; init; }
    public double BackRimWidthPx { get; init; } = 4.5;
    public double BackRimIntensity { get; init; } = 1.25;
    public LightRgb EmissionColor { get; init; } = LightRgb.White;
    public double EmissionIntensity { get; init; }
    public string? EmissionMap { get; init; }
    /// <summary>Authored artwork alpha multiplier, independent of shadow-casting.</summary>
    public double VisualOpacity { get; init; } = 1;
    /// <summary>Optional ambient readability floor, not a post-light clamp.</summary>
    public double MinimumAmbient { get; init; }

    public static ProductionSurfaceSubmission From(string instanceId, LightingSurfaceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        var result = new ProductionSurfaceSubmission
        {
            InstanceId = instanceId,
            LayerId = options.LayerId,
            Channel = options.Channel,
            CasterId = options.CastsShadows ? instanceId : null,
            Shading = options.MaterialShading,
            ExplicitLitOverride = options.LightingMode == LightingSurfaceMode.Lit,
            ReceivesShadows = options.ReceivesShadows,
            Receiver = options.Receiver,
            CastsShadows = options.CastsShadows,
            NormalMap = options.NormalMap,
            EdgeMap = options.EdgeMap,
            NormalStrength = options.MaterialNormalStrength,
            RimStrength = options.MaterialRimStrength,
            MaterialDepthZ = options.MaterialDepthZ,
            BackRimOnOverlap = options.BackRimOnOverlap,
            BackRimWidthPx = options.BackRimWidthPx,
            BackRimIntensity = options.BackRimIntensity,
            EmissionColor = options.EmissionColor,
            EmissionIntensity = options.EmissionIntensity,
            EmissionMap = options.EmissionMap
        };
        result.Validate();
        return result;
    }

    public void Validate()
    {
        Receiver.Validate();
        if (Receiver.Active && (Shading != ProductionSurfaceShading.Flat || CastsShadows ||
            NormalMap is not null || EdgeMap is not null))
            throw new NotSupportedException("Oriented receivers require non-casting Flat artwork without tangent maps.");
        if (string.IsNullOrWhiteSpace(InstanceId) || InstanceId.Length > 100 ||
            !SceneLightingLayer.IsId(LayerId) || Channel == LightChannel.None ||
            (Channel & ~LightChannel.All) != 0 || !Enum.IsDefined(Shading))
            throw new ArgumentException("Invalid production surface submission.");
        if (!double.IsFinite(NormalStrength) || NormalStrength is < 0 or > 1 ||
            !double.IsFinite(RimStrength) || RimStrength is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(NormalStrength));
        if (Center is LightPoint point) point.Validate();
        if (!double.IsFinite(MapRotationDegrees)) throw new ArgumentOutOfRangeException(nameof(MapRotationDegrees));
        ValidateMap(NormalMap);
        ValidateMap(EdgeMap);
        ValidateMap(EmissionMap);
        if (!double.IsFinite(VisualOpacity) || VisualOpacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(VisualOpacity));
        if (!double.IsFinite(MinimumAmbient) || MinimumAmbient is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(MinimumAmbient));
        EmissionColor.Validate(nameof(EmissionColor));
        if (!double.IsFinite(EmissionIntensity) || EmissionIntensity is < 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(EmissionIntensity));
        if (!double.IsFinite(MaterialDepthZ) || MaterialDepthZ is < -100 or > 100 ||
            !double.IsFinite(BackRimWidthPx) || BackRimWidthPx is < 0.5 or > 24 ||
            !double.IsFinite(BackRimIntensity) || BackRimIntensity is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(MaterialDepthZ));
        if (CastsShadows != (CasterId is not null) ||
            (CastsShadows && CasterId != InstanceId))
            throw new ArgumentException("Surface caster identity must match its artwork IID.");
        if (EmissionIntensity > 0 && Shading == ProductionSurfaceShading.Legacy)
            throw new NotSupportedException("Emissive radiance needs the opted-in material pass.");
        if (BackRimOnOverlap && !CastsShadows)
            throw new NotSupportedException("Overlap back-rim requires a casting wall silhouette.");
    }

    public static void ValidateMap(string? filename)
    {
        if (string.IsNullOrEmpty(filename)) return;
        if (filename.Length > 240 || !filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
            filename.Contains("..", StringComparison.Ordinal) || filename.Contains(':') ||
            filename.StartsWith('/') || filename.StartsWith('\\') ||
            filename.Contains('\\') || filename.Split('/').Any(s => s.Length == 0 || s == "."))
            throw new ArgumentException("Material map must be a safe relative PNG path.");
    }
}

/// <summary>
/// Stable, bounded per-surface selection for production multi-pass materials.
/// Author-authored layer routing takes precedence over channel fallback; light
/// influence is ranked at the surface center and ties resolve by ID.
/// </summary>
public static class ProductionSurfaceLightSelection
{
    public static int CountEligible(IReadOnlyList<RadialLight> visible,
        SceneLightingLayer layer, LightPoint center, double radiusScale = 1)
    {
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentNullException.ThrowIfNull(layer);
        center.Validate();
        if (!double.IsFinite(radiusScale) || radiusScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(radiusScale));
        return visible.Count(l => LightLayerRouting.Illuminates(l, layer) &&
            l.Intensity > 0 && RadialLighting.Attenuation(l with
            { Radius = Math.Min(10000, l.Radius * radiusScale) }, center) > 0);
    }

    public static IReadOnlyList<RadialLight> Select(IReadOnlyList<RadialLight> visible,
        SceneLightingLayer layer, LightPoint center, int maximum = 2, double radiusScale = 1)
    {
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentNullException.ThrowIfNull(layer);
        center.Validate();
        if (maximum is < 0 or > 12 || !double.IsFinite(radiusScale) || radiusScale <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        return visible.Where(l => LightLayerRouting.Illuminates(l, layer) && l.Intensity > 0)
            .Select(l => new { Light = l, Score = RadialLighting.Attenuation(l with
                { Radius = Math.Min(10000, l.Radius * radiusScale) }, center) })
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Light.Id, StringComparer.Ordinal)
            .Take(maximum)
            .Select(x => x.Light)
            .ToArray();
    }
}
