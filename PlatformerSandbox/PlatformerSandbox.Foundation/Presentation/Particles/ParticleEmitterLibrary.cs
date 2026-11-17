using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlatformerSandbox.Foundation.Presentation.Particles;

/// <summary>
/// Lightweight P01 JSON preset loader. It defines individual emitters only;
/// P03 adds multi-emitter effects and external authoring integration.
/// </summary>
public sealed record ParticleEmitterManifest
{
    public int Version { get; init; }
    public ParticleEmitterDefinition[]? Emitters { get; init; }
}

public sealed class ParticleEmitterLibrary
{

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    private readonly Dictionary<string, ParticleEmitterDefinition> _definitions;

    public int Count => _definitions.Count;
    public IReadOnlyCollection<string> Ids => _definitions.Keys;

    public ParticleEmitterLibrary(IEnumerable<ParticleEmitterDefinition> definitions)
    {
        if (definitions is null) throw new ArgumentNullException(nameof(definitions));
        _definitions = new Dictionary<string, ParticleEmitterDefinition>(StringComparer.Ordinal);
        foreach (ParticleEmitterDefinition definition in definitions)
        {
            if (definition is null)
                throw new ArgumentException("Null emitter definition.", nameof(definitions));
            definition.Validate();
            if (!_definitions.TryAdd(definition.Id, definition))
                throw new ArgumentException($"Duplicate emitter ID: {definition.Id}", nameof(definitions));
        }
        if (_definitions.Count == 0)
            throw new ArgumentException("An emitter library cannot be empty.", nameof(definitions));
    }

    public bool TryGet(string id, out ParticleEmitterDefinition definition) =>
        _definitions.TryGetValue(id, out definition!);

    public ParticleEmitterDefinition Get(string id) =>
        _definitions.TryGetValue(id, out ParticleEmitterDefinition? value) ? value :
            throw new KeyNotFoundException($"Unknown particle emitter: {id}");

    public static ParticleEmitterLibrary FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("Particle emitter JSON must not be empty.", nameof(json));
        ParticleEmitterManifest document = JsonSerializer.Deserialize<ParticleEmitterManifest>(json, JsonOptions)
            ?? throw new JsonException("Particle emitter document must be an object.");
        if (document.Version != 1)
            throw new NotSupportedException($"Particle emitter schema version {document.Version} is unsupported.");
        return new ParticleEmitterLibrary(document.Emitters ??
            throw new JsonException("Particle emitter document is missing 'emitters'."));
    }
}
