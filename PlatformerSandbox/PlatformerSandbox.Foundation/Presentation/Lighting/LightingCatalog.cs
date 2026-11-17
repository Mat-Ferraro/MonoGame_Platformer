using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

public sealed record LightingEnvironment
{
    public string Id { get; init; } = "";
    public LightRgb Ambient { get; init; } = new(.16, .19, .26);
    public RadialLight[] Lights { get; init; } = Array.Empty<RadialLight>();
    public LightingOccluder[] Occluders { get; init; } = Array.Empty<LightingOccluder>();
    public LightingMaterialDefinition[] Materials { get; init; } = Array.Empty<LightingMaterialDefinition>();

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 80)
            throw new ArgumentException("Environment ID required.");
        Ambient.Validate(nameof(Ambient));
        if (Lights is null || Lights.Length > 64 || Occluders is null || Occluders.Length > 64 ||
            Materials is null || Materials.Length > 32)
            throw new ArgumentOutOfRangeException(nameof(Lights));
        static void EnsureUnique(IEnumerable<string> ids, string category)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in ids)
                if (!seen.Add(id)) throw new ArgumentException($"Duplicate {category} ID: {id}");
        }
        foreach (RadialLight l in Lights) l.Validate();
        foreach (LightingOccluder o in Occluders) o.Validate();
        foreach (LightingMaterialDefinition m in Materials) m.Validate();
        EnsureUnique(Lights.Select(x => x.Id), "light");
        EnsureUnique(Occluders.Select(x => x.Id), "occluder");
        EnsureUnique(Materials.Select(x => x.Id), "material");
    }
}

public sealed record LightingManifest
{
    public int Version { get; init; } = 1;
    public LightingEnvironment[] Scenes { get; init; } = Array.Empty<LightingEnvironment>();
}

/// <summary>Strictly validated, platform-independent lighting authoring manifest.</summary>
public sealed class LightingCatalog
{
    private readonly Dictionary<string, LightingEnvironment> _scenes;
    public IReadOnlyCollection<string> SceneIds => _scenes.Keys;
    private LightingCatalog(Dictionary<string, LightingEnvironment> scenes) => _scenes = scenes;

    public LightingEnvironment Get(string id) => _scenes.TryGetValue(id, out var result)
        ? result : throw new KeyNotFoundException($"Lighting scene '{id}' not found.");

    public static LightingCatalog FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Missing lighting JSON.", nameof(json));
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new JsonStringEnumConverter());
        LightingManifest manifest = JsonSerializer.Deserialize<LightingManifest>(json, options)
            ?? throw new JsonException("Missing lighting manifest.");
        if (manifest.Version != 1 || manifest.Scenes is null || manifest.Scenes.Length is < 1 or > 20)
            throw new ArgumentException("Lighting manifest version 1 requires 1–20 scenes.");
        var scenes = new Dictionary<string, LightingEnvironment>(StringComparer.Ordinal);
        foreach (LightingEnvironment scene in manifest.Scenes)
        {
            scene.Validate();
            if (!scenes.TryAdd(scene.Id, scene))
                throw new ArgumentException($"Duplicate lighting scene '{scene.Id}'.");
        }
        return new LightingCatalog(scenes);
    }

    public static LightingCatalog FromFile(string filename) => FromJson(File.ReadAllText(filename));
}
