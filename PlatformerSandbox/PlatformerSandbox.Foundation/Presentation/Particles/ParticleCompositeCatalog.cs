using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlatformerSandbox.Foundation.Presentation.Particles;

/// <summary>Whether new particles originate from the currently supplied anchor or the start anchor.</summary>
public enum ParticleEmitterAnchor { Fixed, Follow }

/// <summary>
/// One authored component of a multi-emitter particle effect. Position offsets are
/// in the component's declared coordinate space; particles themselves remain in world
/// or screen coordinates once spawned and do not inherit later anchor movement.
/// </summary>
public sealed record ParticleEffectPartDefinition
{
    public string EmitterId { get; init; } = "";
    public double OffsetX { get; init; }
    public double OffsetY { get; init; }
    public double DelaySeconds { get; init; }
    /// <summary>Continuous emission duration; zero = until stopped explicitly.</summary>
    public double DurationSeconds { get; init; }
    public int Capacity { get; init; } = 128;
    public ParticleEmitterAnchor Anchor { get; init; } = ParticleEmitterAnchor.Fixed;
    public ParticleCoordinateSpace Space { get; init; } = ParticleCoordinateSpace.World;
    public ParticleVisualLayer Layer { get; init; } = ParticleVisualLayer.World;
    public ParticleBlendMode Blend { get; init; } = ParticleBlendMode.Alpha;
    public ParticleSpriteShape Sprite { get; init; } = ParticleSpriteShape.SoftDisc;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(EmitterId))
            throw new ArgumentException("Each effect part must reference an emitter.", nameof(EmitterId));
        if (!double.IsFinite(OffsetX) || !double.IsFinite(OffsetY))
            throw new ArgumentOutOfRangeException(nameof(OffsetX), "Offsets must be finite.");
        if (!double.IsFinite(DelaySeconds) || DelaySeconds < 0 || DelaySeconds > 10)
            throw new ArgumentOutOfRangeException(nameof(DelaySeconds));
        if (!double.IsFinite(DurationSeconds) || DurationSeconds < 0 || DurationSeconds > 60)
            throw new ArgumentOutOfRangeException(nameof(DurationSeconds));
        if (Capacity < 1 || Capacity > 2048)
            throw new ArgumentOutOfRangeException(nameof(Capacity));
        if (!Enum.IsDefined(Anchor) || !Enum.IsDefined(Space) ||
            !Enum.IsDefined(Layer) || !Enum.IsDefined(Blend) || !Enum.IsDefined(Sprite))
            throw new ArgumentOutOfRangeException(nameof(Layer), "Invalid visual style or anchor.");
        if ((Space == ParticleCoordinateSpace.Screen) != (Layer == ParticleVisualLayer.Ui))
            throw new ArgumentException("Screen-space parts must use Ui layer; world-space parts cannot use Ui.", nameof(Space));
    }
}

/// <summary>One named, reusable multi-component effect recipe.</summary>
public sealed record ParticleCompositeDefinition
{
    public string Id { get; init; } = "";
    public ParticleEffectPartDefinition[]? Parts { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 100 ||
            Id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new ArgumentException("Invalid effect ID.", nameof(Id));
        if (Parts is null || Parts.Length is < 1 or > 8)
            throw new ArgumentException("Effects need 1-8 parts.", nameof(Parts));
        foreach (ParticleEffectPartDefinition part in Parts)
        {
            if (part is null)
                throw new ArgumentException("Null effect part.", nameof(Parts));
            part.Validate();
        }
    }
}

public sealed record ParticleCompositeManifest
{
    public int Version { get; init; }
    public ParticleEmitterDefinition[]? Emitters { get; init; }
    public ParticleCompositeDefinition[]? Effects { get; init; }
}

/// <summary>
/// P03 authoring catalog: immutable validated emitter presets and named composite
/// effect recipes. No graphics or gameplay assemblies are referenced.
/// </summary>
public sealed class ParticleCompositeCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private readonly Dictionary<string, ParticleCompositeDefinition> _effects =
        new(StringComparer.Ordinal);

    public ParticleEmitterLibrary Emitters { get; }
    public int Count => _effects.Count;
    public IReadOnlyCollection<string> Ids => _effects.Keys;

    public ParticleCompositeCatalog(IEnumerable<ParticleEmitterDefinition> emitters,
        IEnumerable<ParticleCompositeDefinition> effects)
    {
        Emitters = new ParticleEmitterLibrary(emitters);
        ArgumentNullException.ThrowIfNull(effects);
        foreach (ParticleCompositeDefinition effect in effects)
        {
            if (effect is null)
                throw new ArgumentException("Null effect definition.", nameof(effects));
            effect.Validate();
            foreach (ParticleEffectPartDefinition part in effect.Parts!)
                if (!Emitters.TryGet(part.EmitterId, out _))
                    throw new ArgumentException(
                        $"Effect '{effect.Id}' refers to missing emitter '{part.EmitterId}'.", nameof(effects));
            if (!_effects.TryAdd(effect.Id, effect))
                throw new ArgumentException($"Duplicate effect ID '{effect.Id}'.", nameof(effects));
        }
        if (_effects.Count == 0)
            throw new ArgumentException("An effect catalog cannot be empty.", nameof(effects));
    }

    public ParticleCompositeDefinition Get(string id) =>
        _effects.TryGetValue(id, out ParticleCompositeDefinition? effect) ? effect :
        throw new KeyNotFoundException($"Unknown particle effect: {id}");

    public bool TryGet(string id, out ParticleCompositeDefinition effect) =>
        _effects.TryGetValue(id, out effect!);

    public static ParticleCompositeCatalog FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Composite manifest JSON cannot be empty.", nameof(json));
        ParticleCompositeManifest manifest = JsonSerializer.Deserialize<ParticleCompositeManifest>(json, JsonOptions)
            ?? throw new JsonException("Composite manifest must be a JSON object.");
        if (manifest.Version != 1)
            throw new NotSupportedException($"Unsupported composite effect version: {manifest.Version}.");
        return new ParticleCompositeCatalog(
            manifest.Emitters ?? throw new JsonException("Missing emitters."),
            manifest.Effects ?? throw new JsonException("Missing effects."));
    }
}
