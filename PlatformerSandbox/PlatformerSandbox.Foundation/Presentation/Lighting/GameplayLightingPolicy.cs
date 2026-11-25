using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Explicit per-object shading override. Inherit preserves the existing
/// scene-layer behavior, including selectively illuminated foreground silhouettes.</summary>
public enum LightingSurfaceMode { Inherit, Lit, Unlit, Silhouette }

/// <summary>Independent 2D surface decisions, not inferred from collision or draw depth.</summary>
public sealed record LightingSurfaceOptions
{
    public bool ReceivesLight { get; init; } = true;
    public SurfaceReceiverProjection Receiver { get; init; } = new();
    public bool ReceivesShadows { get; init; } = true;
    public bool CastsShadows { get; init; }
    public bool ContactShadow { get; init; }
    public LightingSurfaceMode LightingMode { get; init; } = LightingSurfaceMode.Inherit;
    public LightChannel Channel { get; init; } = LightChannel.Terrain;
    public string LayerId { get; init; } = "PLAYER";
    /// <summary>Legacy by default. Explicit GPU material opt-in for non-shadow receivers.</summary>
    public ProductionSurfaceShading MaterialShading { get; init; } = ProductionSurfaceShading.Legacy;
    public string? NormalMap { get; init; }
    public string? EdgeMap { get; init; }
    public double MaterialNormalStrength { get; init; } = 1;
    public double MaterialRimStrength { get; init; } = 1;
    /// <summary>Depth coordinate of this artwork (not the LDtk layer order).</summary>
    public double MaterialDepthZ { get; init; }
    /// <summary>At equal depth, a light whose origin overlaps this polygon may be
    /// treated as behind it. Explicit opt-in; default is Front.</summary>
    public bool BackRimOnOverlap { get; init; }
    public double BackRimWidthPx { get; init; } = 4.5;
    public double BackRimIntensity { get; init; } = 1.25;
    // L19: emissive radiance does not require a nearby dynamic light.
    public LightRgb EmissionColor { get; init; } = LightRgb.White;
    public double EmissionIntensity { get; init; }
    public string? EmissionMap { get; init; }
    // Zero radius is explicit glow-only. Light energy is independent of surface radiance.
    public double EmissiveLightRadius { get; init; }
    public double EmissiveLightIntensity { get; init; } = 1;
    public bool EmissiveLightCastsShadows { get; init; } = true;
    /// <summary>Do not multiply receiver pixels by backdrop shadow masks when this
    /// surface is unlit, a shadow caster, explicitly refuses cast shadows,
    /// or has an authored contact-shadow decoration drawn beneath it.</summary>
    public bool NeedsIndependentPass => CastsShadows || !ReceivesLight || !ReceivesShadows ||
        ContactShadow || LightingMode != LightingSurfaceMode.Inherit ||
        MaterialShading != ProductionSurfaceShading.Legacy;
    public void Validate()
    {
        Receiver.Validate();
        if (!double.IsFinite(EmissiveLightRadius) || EmissiveLightRadius is < 0 or > 40 ||
            !double.IsFinite(EmissiveLightIntensity) || EmissiveLightIntensity is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(EmissiveLightRadius));
        if (Receiver.Active && (MaterialShading != ProductionSurfaceShading.Flat || CastsShadows ||
            NormalMap is not null || EdgeMap is not null))
            throw new NotSupportedException("Oriented receivers require non-casting Flat artwork without tangent maps.");
        if (!Enum.IsDefined(LightingMode) || !SceneLightingLayer.IsId(LayerId) || Channel == LightChannel.None ||
            (Channel & ~LightChannel.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(Channel));
        if (!Enum.IsDefined(MaterialShading) ||
            !double.IsFinite(MaterialNormalStrength) || MaterialNormalStrength is < 0 or > 1 ||
            !double.IsFinite(MaterialRimStrength) || MaterialRimStrength is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(MaterialShading));
        if (!double.IsFinite(MaterialDepthZ) || MaterialDepthZ is < -100 or > 100 ||
            !double.IsFinite(BackRimWidthPx) || BackRimWidthPx is < 0.5 or > 24 ||
            !double.IsFinite(BackRimIntensity) || BackRimIntensity is < 0 or > 4)
            throw new ArgumentOutOfRangeException(nameof(MaterialDepthZ));
        ProductionSurfaceSubmission.ValidateMap(NormalMap);
        ProductionSurfaceSubmission.ValidateMap(EdgeMap);
        ProductionSurfaceSubmission.ValidateMap(EmissionMap);
        EmissionColor.Validate(nameof(EmissionColor));
        if (!double.IsFinite(EmissionIntensity) || EmissionIntensity is < 0 or > 8)
            throw new ArgumentOutOfRangeException(nameof(EmissionIntensity));
        if (EmissionIntensity > 0 && MaterialShading == ProductionSurfaceShading.Legacy)
            throw new NotSupportedException("Emission requires a production GPU material (Flat/Normal/Rim/Hybrid).");
        if (MaterialShading != ProductionSurfaceShading.Legacy)
        {
            if (!ReceivesLight ||
                LightingMode is LightingSurfaceMode.Unlit or LightingSurfaceMode.Silhouette ||
                ContactShadow)
                throw new NotSupportedException(
                    "GPU materials require lit artwork without contact-shadow decorations.");
            if (BackRimOnOverlap && !CastsShadows)
                throw new NotSupportedException(
                    "Overlap back-rim requires a shadow-casting artwork silhouette.");
        }
    }
}

/// <summary>Headless per-sprite lighting policy, independent of MonoGame and of
/// whether a sprite casts occlusion geometry. A silhouette is deliberately
/// backlit: its front face is dark even beside a bright local light.</summary>
public static class LightingSurfaceShading
{
    public static LightRgb ResolveTint(LightingSurfaceOptions surface,
        SceneLightingLayer layer, LightRgb illuminatedTint,
        LightRgb? frontFacingReveal = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(layer);
        surface.Validate();
        layer.Validate();
        if (surface.LightingMode == LightingSurfaceMode.Silhouette)
        {
            // A silhouette stays dark under ordinary world/background lights.
            // Only explicitly designated front-facing lights can reveal it.
            LightRgb baseTint = new(.085, .105, .13);
            if (frontFacingReveal is not LightRgb reveal) return baseTint;
            return new LightRgb(Math.Max(baseTint.R, reveal.R),
                Math.Max(baseTint.G, reveal.G),
                Math.Max(baseTint.B, reveal.B)).Clamp();
        }
        if (surface.LightingMode == LightingSurfaceMode.Unlit ||
            (surface.LightingMode == LightingSurfaceMode.Inherit &&
                (!surface.ReceivesLight || layer.Mode == RenderLayerLightingMode.Unlit)))
            return LightRgb.White;
        // "Lit" overrides an unlit layer. The inherited FG silhouette still
        // uses layer-selected local lights so vegetation can be selectively revealed.
        return illuminatedTint.Clamp();
    }
}

public enum LightingQuality { Low, Balanced, High }

/// <summary>
/// Hard per-frame budgets. Reduce optional shadow work BEFORE dropping major lights.
/// A quality change never changes the authoritative simulation.
/// </summary>
public readonly record struct LightingFrameBudget(int MaxLights, int MaxShadowLights,
    int ShadowRadialSamples, int MaxParticleLights)
{
    public static LightingFrameBudget For(LightingQuality quality) => quality switch
    {
        LightingQuality.Low => new(4, 0, 64, 1),
        LightingQuality.Balanced => new(8, 3, 128, 3),
        LightingQuality.High => new(12, 5, 192, 6),
        _ => throw new ArgumentOutOfRangeException(nameof(quality))
    };
}

public sealed record LightingArtPreset
{
    public string Id { get; init; } = "";
    public LightRgb Ambient { get; init; } = new(.37, .40, .49);
    public double LightMultiplier { get; init; } = 1;
    /// <summary>SDR art exposure applied after lighting the surface. Values above 1
    /// brighten local light pools beyond the original albedo brightness; keep
    /// low ambient levels for dark, readable shadows.</summary>
    public double LightExposure { get; init; } = 1;
    /// <summary>Artist-tunable radius of every local light; 1 means authored radius.</summary>
    public double LightRadiusScale { get; init; } = 1;
    /// <summary>Lower bound on the independently lit hero's channel brightness.</summary>
    public double HeroMinimumBrightness { get; init; } = .52;
    /// <summary>Additional per-render-layer ambient multiplier, applied after layer defaults.</summary>
    public Dictionary<string, double> LayerAmbientScales { get; init; } = new(StringComparer.Ordinal);
    public double AmbientScaleFor(string? layerId) =>
        layerId is not null && LayerAmbientScales.TryGetValue(layerId, out double scale) ? scale : 1;
    public double AtmosphericDarkness { get; init; } = 0;
    public bool Bloom { get; init; }
    public double BloomStrength { get; init; } = .2;
    public bool Posterize { get; init; }
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64)
            throw new ArgumentException("Art preset ID is required.");
        Ambient.Validate(nameof(Ambient));
        if (!double.IsFinite(LightMultiplier) || LightMultiplier < 0 || LightMultiplier > 3 ||
            !double.IsFinite(LightExposure) || LightExposure < 1 || LightExposure > 3 ||
            !double.IsFinite(LightRadiusScale) || LightRadiusScale < .25 || LightRadiusScale > 2 ||
            !double.IsFinite(HeroMinimumBrightness) || HeroMinimumBrightness < 0 || HeroMinimumBrightness > .8 ||
            !double.IsFinite(AtmosphericDarkness) || AtmosphericDarkness < 0 || AtmosphericDarkness > .75 ||
            !double.IsFinite(BloomStrength) || BloomStrength < 0 || BloomStrength > 1)
            throw new ArgumentOutOfRangeException(nameof(LightMultiplier));
        if (LayerAmbientScales is null || LayerAmbientScales.Count > 32)
            throw new ArgumentException("Invalid per-layer ambient configuration.");
        foreach (var (id, scale) in LayerAmbientScales)
            if (!SceneLightingLayer.IsId(id) || !double.IsFinite(scale) || scale < 0 || scale > 3)
                throw new ArgumentOutOfRangeException(nameof(LayerAmbientScales));
    }
}

public sealed record LightingGameManifest
{
    public int Version { get; init; } = 1;
    public string DefaultPreset { get; init; } = "Illustrated";
    public LightingArtPreset[] Presets { get; init; } = Array.Empty<LightingArtPreset>();
    /// <summary>Presets offered outside a scene with an explicit selection.</summary>
    public string[]? GlobalPresetIds { get; init; }
    public LightingScenePresetSelection[] ScenePresets { get; init; } = Array.Empty<LightingScenePresetSelection>();
    public LightingParticleBinding[] ParticleLights { get; init; } = Array.Empty<LightingParticleBinding>();
}

/// <summary>Explicit scene-specific artistic choices, independent of scene layer count.</summary>
public sealed record LightingScenePresetSelection
{
    public string SceneId { get; init; } = "";
    public string DefaultPreset { get; init; } = "";
    public string[] PresetIds { get; init; } = Array.Empty<string>();
}

/// <summary>One optional shared light per particle effect INSTANCE, never per emitted mote.</summary>
public sealed record LightingParticleBinding
{
    public string EffectId { get; init; } = "";
    public LightRgb Color { get; init; } = LightRgb.White;
    public double Radius { get; init; } = 1.8;
    public double Intensity { get; init; } = .6;
    public int Priority { get; init; } = -5;
    public bool CastShadows { get; init; }
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(EffectId) || EffectId.Length > 100 ||
            EffectId.Any(x => !(char.IsAsciiLetterOrDigit(x) || x is '.' or '_' or '-')))
            throw new ArgumentException("Particle effect identifier invalid.");
        Color.Validate(nameof(Color));
        if (!double.IsFinite(Radius) || Radius <= 0 || Radius > 25 ||
            !double.IsFinite(Intensity) || Intensity < 0 || Intensity > 4 ||
            Priority is < -1000 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(Radius));
    }
}

public sealed class LightingGameCatalog
{
    private readonly Dictionary<string, LightingArtPreset> _presets;
    private readonly Dictionary<string, LightingParticleBinding> _particleLights;
    private readonly Dictionary<string, LightingScenePresetSelection> _scenePresets;
    private readonly string[] _globalPresetIds;
    public string DefaultPreset { get; }
    public IReadOnlyCollection<string> PresetIds => _globalPresetIds;
    public IReadOnlyList<string> GetPresetIdsForScene(string? sceneId) =>
        sceneId is not null && _scenePresets.TryGetValue(sceneId, out var selection)
            ? selection.PresetIds : _globalPresetIds;
    public string GetDefaultPresetForScene(string? sceneId) =>
        sceneId is not null && _scenePresets.TryGetValue(sceneId, out var selection)
            ? selection.DefaultPreset : DefaultPreset;
    public IEnumerable<LightingParticleBinding> ParticleLights => _particleLights.Values;

    private LightingGameCatalog(LightingGameManifest manifest)
    {
        if (manifest.Version != 1 || manifest.Presets is null ||
            manifest.Presets.Length is < 2 or > 12 || manifest.ParticleLights is null ||
            manifest.ParticleLights.Length > 32)
            throw new ArgumentException("Lighting gameplay manifest requires version 1 and 2-12 presets.");
        _presets = new Dictionary<string, LightingArtPreset>(StringComparer.Ordinal);
        foreach (LightingArtPreset preset in manifest.Presets)
        {
            preset.Validate();
            if (!_presets.TryAdd(preset.Id, preset))
                throw new ArgumentException("Duplicate lighting preset " + preset.Id);
        }
        DefaultPreset = manifest.DefaultPreset;
        if (!_presets.ContainsKey(DefaultPreset))
            throw new ArgumentException("Default lighting preset is undefined.");
        _globalPresetIds = manifest.GlobalPresetIds ?? _presets.Keys.ToArray();
        ValidateChoices(_globalPresetIds, _presets, DefaultPreset);
        _scenePresets = new Dictionary<string, LightingScenePresetSelection>(StringComparer.Ordinal);
        if (manifest.ScenePresets is null || manifest.ScenePresets.Length > 64)
            throw new ArgumentException("Invalid scene preset list.");
        foreach (LightingScenePresetSelection scene in manifest.ScenePresets)
        {
            if (scene is null || !SceneLightingLayer.IsId(scene.SceneId))
                throw new ArgumentException("Invalid lighting scene identifier.");
            ValidateChoices(scene.PresetIds, _presets, scene.DefaultPreset);
            if (!_scenePresets.TryAdd(scene.SceneId, scene))
                throw new ArgumentException("Duplicate lighting scene " + scene.SceneId);
        }
        _particleLights = new Dictionary<string, LightingParticleBinding>(StringComparer.Ordinal);
        foreach (LightingParticleBinding binding in manifest.ParticleLights)
        {
            binding.Validate();
            if (!_particleLights.TryAdd(binding.EffectId, binding))
                throw new ArgumentException("Duplicate particle-light binding " + binding.EffectId);
        }
    }

    private static void ValidateChoices(string[]? ids,
        Dictionary<string, LightingArtPreset> presets, string defaultId)
    {
        if (ids is null || ids.Length is < 1 or > 12 ||
            ids.Any(id => string.IsNullOrWhiteSpace(id) || !presets.ContainsKey(id)) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Length ||
            !ids.Contains(defaultId, StringComparer.Ordinal))
            throw new ArgumentException("Scene/global lighting presets must be unique, defined, and include the default.");
    }

    public LightingArtPreset GetPreset(string id) => _presets[id];
    public LightingParticleBinding? ForParticle(string effectId) =>
        _particleLights.TryGetValue(effectId, out var binding) ? binding : null;

    public static LightingGameCatalog FromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        LightingGameManifest manifest = JsonSerializer.Deserialize<LightingGameManifest>(json, options)
            ?? throw new JsonException("Lighting gameplay manifest missing.");
        return new LightingGameCatalog(manifest);
    }
}

/// <summary>
/// Stable light selection with reserved authored shadow capacity. A transient
/// effect must never turn an existing level light into an unshadowed floodlight.
/// </summary>
public static class LightingFramePlanner
{
    public static IReadOnlyList<RadialLight> Select(IEnumerable<RadialLight> lights,
        LightingViewport viewport, LightingFrameBudget budget, bool reducedFlashes)
    {
        ArgumentNullException.ThrowIfNull(lights);
        viewport.Validate();
        if (budget.MaxLights <= 0 || budget.MaxShadowLights < 0)
            throw new ArgumentOutOfRangeException(nameof(budget));

        RadialLight[] ordered = lights.Where(viewport.InfluencesViewport)
            .OrderByDescending(x => x.Priority).ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        // Even if many high-priority VFX flashes are active, leave room for
        // visible authored shadow lights so their existing shadow masks remain.
        RadialLight[] authoredShadows = ordered
            .Where(x => x.CastShadows && x.Owner == "level")
            .Take(Math.Min(budget.MaxLights, budget.MaxShadowLights)).ToArray();
        RadialLight[] candidates = authoredShadows.Concat(ordered)
            .DistinctBy(x => x.Id)
            .Take(budget.MaxLights)
            .OrderByDescending(x => x.Priority).ThenBy(x => x.Id, StringComparer.Ordinal)
            .ToArray();

        // Budget shadow masks FIRST for authored level lights, THEN for
        // transient/other lights. Light priority still orders visible lights;
        // it does not allow an ephemeral flash to steal a wall's shadow mask.
        var shadowOwners = new HashSet<string>(StringComparer.Ordinal);
        foreach (RadialLight light in candidates
            .Where(x => x.CastShadows && x.Owner == "level")
            .Concat(candidates.Where(x => x.CastShadows && x.Owner != "level")))
        {
            if (shadowOwners.Count >= budget.MaxShadowLights) break;
            shadowOwners.Add(light.Id);
        }

        var result = new List<RadialLight>(candidates.Length);
        foreach (RadialLight light in candidates)
        {
            bool shadow = light.CastShadows && shadowOwners.Contains(light.Id);
            // With shadows enabled, a front-facing, shadow-dependent flash
            // that has no remaining mask must be omitted rather than allowed
            // to shine through blockers and erase existing level shadows.
            // Low quality (0 masks) deliberately disables all shadowing.
            if ((light.Owner is "flash" or "emissive") && light.CastShadows && !shadow &&
                budget.MaxShadowLights > 0)
                continue;

            result.Add(light with
            {
                CastShadows = shadow,
                Intensity = reducedFlashes && light.Owner == "flash"
                    ? light.Intensity * .25 : light.Intensity
            });
        }
        return result;
    }
}
