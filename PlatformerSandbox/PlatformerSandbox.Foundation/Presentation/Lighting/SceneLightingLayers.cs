using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Ordered, presentation-only depth planes; not physics/collision layers.</summary>
public enum RenderLayerGroup { Background, Gameplay, Foreground }
public enum RenderLayerLightingMode { Lit, Unlit, Silhouette }

public sealed record SceneLightingLayer
{
    public string Id { get; init; } = "";
    public int Order { get; init; }
    public RenderLayerGroup Group { get; init; }
    public RenderLayerLightingMode Mode { get; init; } = RenderLayerLightingMode.Lit;
    public double AmbientScale { get; init; } = 1;
    /// <summary>Multiplier for an art asset's existing parallax factor; 1 preserves LDtk behavior.</summary>
    public double ParallaxMultiplier { get; init; } = 1;
    public bool Visible { get; init; } = true;
    public bool ReceivesShadows { get; init; } = true;
    public bool CastsShadowsByDefault { get; init; }
    public void Validate()
    {
        if (!IsId(Id) || Order is < -1000 or > 1000 || !Enum.IsDefined(Group) ||
            !Enum.IsDefined(Mode) || !double.IsFinite(AmbientScale) || AmbientScale is < 0 or > 3 ||
            !double.IsFinite(ParallaxMultiplier) || ParallaxMultiplier is < 0 or > 3)
            throw new ArgumentException("Invalid lighting scene layer: " + Id);
    }
    public static bool IsId(string? id) => id is { Length: > 0 and <= 64 } &&
        id.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
}

/// <summary>Every scene may replace the entire ordered layer stack, without an 11-layer engine limit.</summary>
public sealed class SceneLightingLayerSet
{
    private readonly Dictionary<string, SceneLightingLayer> _byId;
    public IReadOnlyList<SceneLightingLayer> Layers { get; }
    public SceneLightingLayerSet(IEnumerable<SceneLightingLayer> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        SceneLightingLayer[] ordered = layers.ToArray();
        if (ordered.Length is < 1 or > 32) throw new ArgumentException("Scene needs 1-32 render layers.");
        _byId = new Dictionary<string, SceneLightingLayer>(StringComparer.Ordinal);
        var orders = new HashSet<int>();
        foreach (var layer in ordered)
        {
            if (layer is null) throw new ArgumentException("Null render layer.");
            layer.Validate();
            if (!_byId.TryAdd(layer.Id, layer) || !orders.Add(layer.Order))
                throw new ArgumentException("Duplicate lighting layer ID/order: " + layer.Id);
        }
        if (ordered.Count(x => x.Group == RenderLayerGroup.Gameplay) != 1)
            throw new ArgumentException("Exactly one gameplay render layer is required.");
        Layers = Array.AsReadOnly(ordered.OrderBy(x => x.Order).ToArray());
    }
    public bool Contains(string id) => _byId.ContainsKey(id);
    public SceneLightingLayer Get(string id) => _byId.TryGetValue(id, out var layer)
        ? layer : throw new ArgumentException("Unknown scene render layer: " + id);
    public SceneLightingLayer Gameplay => Layers.First(x => x.Group == RenderLayerGroup.Gameplay);
    public SceneLightingLayer DefaultFor(RenderLayerGroup group) =>
        Layers.FirstOrDefault(x => x.Group == group && x.Id == (group switch
            { RenderLayerGroup.Background => "BG1", RenderLayerGroup.Gameplay => "PLAYER", _ => "FG1" }))
        ?? Layers.FirstOrDefault(x => x.Group == group)
        ?? Gameplay;

    public SceneLightingLayer ForTile(string name)
    {
        if (_byId.TryGetValue(name, out var direct)) return direct;
        if (name.StartsWith("BG_", StringComparison.Ordinal))
        {
            string id = name switch
            {
                "BG_Far" => "BG5", "BG_Mid" => "BG3", "BG_Near" => "BG1", _ => "BG1"
            };
            return _byId.TryGetValue(id, out var layer) ? layer : DefaultFor(RenderLayerGroup.Background);
        }
        if (name.StartsWith("FG_", StringComparison.Ordinal))
        {
            string id = name switch { "FG_Close" => "FG3", "FG_Near" => "FG1", _ => "FG1" };
            return _byId.TryGetValue(id, out var layer) ? layer : DefaultFor(RenderLayerGroup.Foreground);
        }
        return Gameplay;
    }

    public SceneLightingLayer ForArt(string? explicitId, RenderLayerGroup group)
    {
        if (!string.IsNullOrWhiteSpace(explicitId)) return Get(explicitId);
        return DefaultFor(group);
    }

    public static SceneLightingLayerSet Standard11() => new(
        Enumerable.Range(1, 5).Select(i => new SceneLightingLayer
        {
            Id = "BG" + i, Order = -i, Group = RenderLayerGroup.Background,
            Mode = RenderLayerLightingMode.Lit
        }).Concat(new[] { new SceneLightingLayer
        { Id = "PLAYER", Order = 0, Group = RenderLayerGroup.Gameplay } })
          .Concat(Enumerable.Range(1, 5).Select(i => new SceneLightingLayer
          {
              Id = "FG" + i, Order = i, Group = RenderLayerGroup.Foreground,
              Mode = i >= 3 ? RenderLayerLightingMode.Silhouette : RenderLayerLightingMode.Lit,
              AmbientScale = i >= 3 ? .06 : 1
          })));
}

public sealed record SceneLightingLayerOverride
{
    public string SceneId { get; init; } = "";
    public SceneLightingLayer[] Layers { get; init; } = Array.Empty<SceneLightingLayer>();
}
public sealed record SceneLightingLayerManifest
{
    public int Version { get; init; } = 1;
    public SceneLightingLayer[] DefaultLayers { get; init; } = Array.Empty<SceneLightingLayer>();
    public SceneLightingLayerOverride[] Scenes { get; init; } = Array.Empty<SceneLightingLayerOverride>();
}
public sealed class SceneLightingLayerCatalog
{
    private readonly SceneLightingLayerSet _default;
    private readonly Dictionary<string, SceneLightingLayerSet> _scenes;
    private SceneLightingLayerCatalog(SceneLightingLayerSet fallback,
        Dictionary<string, SceneLightingLayerSet> scenes)
    { _default = fallback; _scenes = scenes; }
    public SceneLightingLayerSet ForScene(string scene) =>
        _scenes.TryGetValue(scene, out var set) ? set : _default;
    public static SceneLightingLayerCatalog Standard() =>
        new(SceneLightingLayerSet.Standard11(), new(StringComparer.Ordinal));
    public static SceneLightingLayerCatalog FromJson(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        var manifest = JsonSerializer.Deserialize<SceneLightingLayerManifest>(json, options)
            ?? throw new JsonException("Scene lighting layer manifest missing.");
        if (manifest.Version != 1 || manifest.DefaultLayers is null || manifest.Scenes is null ||
            manifest.Scenes.Length > 128)
            throw new ArgumentException("Invalid scene lighting layer manifest.");
        var fallback = manifest.DefaultLayers.Length == 0
            ? SceneLightingLayerSet.Standard11() : new SceneLightingLayerSet(manifest.DefaultLayers);
        var scenes = new Dictionary<string, SceneLightingLayerSet>(StringComparer.Ordinal);
        foreach (var entry in manifest.Scenes)
        {
            if (entry is null || !SceneLightingLayer.IsId(entry.SceneId) || entry.Layers is null ||
                !scenes.TryAdd(entry.SceneId, new SceneLightingLayerSet(entry.Layers)))
                throw new ArgumentException("Invalid or duplicate scene lighting layer definition.");
        }
        return new SceneLightingLayerCatalog(fallback, scenes);
    }
}

/// <summary>Layer IDs are authoring labels; legacy channels only provide a fallback.</summary>
public static class LightLayerRouting
{
    public static bool Illuminates(RadialLight light, SceneLightingLayer layer)
    {
        if (light.AffectsLayers is { } ids) return ids.Contains(layer.Id, StringComparer.Ordinal);
        var channel = layer.Group switch
        {
            RenderLayerGroup.Background => LightChannel.Backdrop | LightChannel.Terrain,
            RenderLayerGroup.Gameplay => LightChannel.Terrain | LightChannel.Hero,
            _ => LightChannel.Effects
        };
        return (light.Channels & channel) != 0;
    }
    public static bool Occludes(RadialLight light, LightingOccluder blocker)
        => light.OccluderLayers is null ||
            light.OccluderLayers.Contains(blocker.LayerId, StringComparer.Ordinal);
    public static void ValidateRouting(RadialLight light, SceneLightingLayerSet layers)
    {
        foreach (var list in new[] { light.AffectsLayers, light.OccluderLayers })
        {
            if (list is null) continue;
            if (list.Count is < 1 or > 32 || list.Any(id => !layers.Contains(id)) ||
                list.Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new ArgumentException("Light layer routing references duplicate or unknown layer IDs.");
        }
    }
}
