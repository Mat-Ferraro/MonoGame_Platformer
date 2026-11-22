using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using PlatformerSandbox.Foundation.Presentation.Lighting;

namespace PlatformerSandbox.Gameplay.Content;

/// <summary>Presentation-only scene lighting imported from the same LDtk snapshot as gameplay.</summary>
public sealed class LdtkLightingScene
{
    public LdtkLightingScene(string levelId, IReadOnlyList<RadialLight> lights,
        IReadOnlyList<LightingOccluder> blockers,
        IReadOnlyDictionary<string, LightingSurfaceOptions> surfaces,
        IReadOnlyDictionary<string, LightPoint> surfaceCenters,
        SceneLightingLayerSet? layers = null)
    {
        LevelId = levelId;
        Lights = lights;
        Occluders = blockers;
        Surfaces = surfaces;
        SurfaceCenters = surfaceCenters;
        Layers = layers ?? SceneLightingLayerSet.Standard11();
    }
    public SceneLightingLayerSet Layers { get; }
    public string LevelId { get; }
    public IReadOnlyList<RadialLight> Lights { get; }
    public IReadOnlyList<LightingOccluder> Occluders { get; }
    public IReadOnlyDictionary<string, LightingSurfaceOptions> Surfaces { get; }
    public IReadOnlyDictionary<string, LightPoint> SurfaceCenters { get; }
    public LightingSurfaceOptions GetSurface(string iid) => Surfaces.TryGetValue(iid, out var options)
        ? options : new LightingSurfaceOptions { LayerId = Layers.Gameplay.Id };
}

/// <summary>
/// LDtk entities LightSource and LightBlocker, and independent artwork light flags.
/// Collision grid is deliberately not used as an implicit shadow hull.
/// </summary>
public static class LdtkLightingImporter
{
    public const string LightEntity = "LightSource";
    public const string BlockerEntity = "LightBlocker";
    private const double UnitsPerPixel = 1.0 / LdtkLevelImporter.PixelsPerWorldUnit;

    public static LdtkLightingScene FromJson(string json, string levelId,
        SceneLightingLayerSet? sceneLayers = null)
    {
        sceneLayers ??= SceneLightingLayerSet.Standard11();
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(levelId))
            throw new ArgumentException("Lighting import requires LDtk JSON and a level ID.");
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.GetProperty("externalLevels").GetBoolean() ||
            root.GetProperty("simplifiedExport").GetBoolean())
            throw new NotSupportedException("Lighting authoring requires embedded LDtk levels.");
        JsonElement level = default;
        bool found = false;
        foreach (JsonElement candidate in root.GetProperty("levels").EnumerateArray())
        {
            if (candidate.GetProperty("identifier").GetString() != levelId) continue;
            if (found) throw new ArgumentException("Duplicate LDtk level " + levelId);
            level = candidate;
            found = true;
        }
        if (!found) throw new ArgumentException("Unknown LDtk level " + levelId);
        int width = level.GetProperty("pxWid").GetInt32();
        int height = level.GetProperty("pxHei").GetInt32();
        var lights = new List<RadialLight>();
        var occluders = new List<LightingOccluder>();
        var surfaces = new Dictionary<string, LightingSurfaceOptions>(StringComparer.Ordinal);
        var centers = new Dictionary<string, LightPoint>(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement layer in level.GetProperty("layerInstances").EnumerateArray())
        {
            if (layer.GetProperty("__type").GetString() != "Entities") continue;
            if (layer.GetProperty("__pxTotalOffsetX").GetInt32() != 0 ||
                layer.GetProperty("__pxTotalOffsetY").GetInt32() != 0)
                throw new ArgumentException("Lighting entity layer offsets are not supported.");
            foreach (JsonElement entity in layer.GetProperty("entityInstances").EnumerateArray())
            {
                string kind = entity.GetProperty("__identifier").GetString() ?? "";
                if (kind is not (LightEntity or BlockerEntity or "ScenerySprite" or
                    "PaintedSprite" or "TerrainArtwork")) continue;
                string iid = entity.GetProperty("iid").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(iid) || !ids.Add(iid))
                    throw new ArgumentException("Missing/duplicate lighting entity IID " + iid);
                var fields = ReadFields(entity);
                if (kind == LightEntity)
                {
                    if (lights.Count >= 48) throw new ArgumentException("Too many authored light sources.");
                    LdtkAuthoredEntity e = new(entity, width, height);
                    RadialLight light = new()
                    {
                        Id = iid,
                        Owner = "level",
                        Position = new LightPoint(e.Bounds.Center.X, e.Bounds.Center.Y),
                        Color = ReadColor(fields, "Color", new LightRgb(1, .75, .5)),
                        Radius = Number(fields, "RadiusWorld", 5, .1, 60),
                        Intensity = Number(fields, "Intensity", 1.4, 0, 4),
                        Priority = (int)Number(fields, "Priority", 1, -100, 100),
                        CastShadows = Bool(fields, "CastShadows", true),
                        Channels = LightChannel.All,
                        AffectsLayers = CsvLayers(fields, "AffectsLayers", sceneLayers),
                        OccluderLayers = CsvLayers(fields, "OccluderLayers", sceneLayers)
                    };
                    light.Validate();
                    LightLayerRouting.ValidateRouting(light, sceneLayers);
                    lights.Add(light);
                }
                else if (kind == BlockerEntity)
                {
                    if (occluders.Count >= 96) throw new ArgumentException("Too many authored light blockers.");
                    LdtkAuthoredEntity e = new(entity, width, height);
                    string blockerLayer = LayerId(fields, sceneLayers,
                        RenderLayerGroup.Gameplay);
                    occluders.Add(FromBounds(iid, e.Bounds.Left, e.Bounds.Bottom,
                        e.Bounds.Right, e.Bounds.Top, blockerLayer));
                }
                else
                {
                    LightChannel plane = ReadPlane(fields, kind);
                    RenderLayerGroup group = plane switch
                    {
                        LightChannel.Backdrop => RenderLayerGroup.Background,
                        LightChannel.Effects => RenderLayerGroup.Foreground,
                        _ => RenderLayerGroup.Gameplay
                    };
                    string surfaceLayerId = LayerId(fields, sceneLayers, group);
                    SceneLightingLayer layerDefaults = sceneLayers.Get(surfaceLayerId);
                    LightingSurfaceOptions options = new()
                    {
                        ReceivesLight = Bool(fields, "ReceivesLight", true),
                        ReceivesShadows = Bool(fields, "ReceivesShadows", layerDefaults.ReceivesShadows),
                        CastsShadows = Bool(fields, "CastsShadows", layerDefaults.CastsShadowsByDefault),
                        ContactShadow = Bool(fields, "ContactShadow", false),
                        LightingMode = SurfaceMode(fields),
                        Channel = plane,
                        LayerId = surfaceLayerId
                    };
                    options.Validate();
                    surfaces.Add(iid, options);
                    JsonElement px = entity.GetProperty("px");
                    JsonElement pivot = entity.GetProperty("__pivot");
                    double sx = px[0].GetDouble() + (0.5 - pivot[0].GetDouble()) *
                        entity.GetProperty("width").GetDouble();
                    double sy = px[1].GetDouble() + (0.5 - pivot[1].GetDouble()) *
                        entity.GetProperty("height").GetDouble();
                    centers.Add(iid, new LightPoint(sx * UnitsPerPixel,
                        (height - sy) * UnitsPerPixel));
                    if (options.CastsShadows)
                    {
                        if (occluders.Count >= 96)
                            throw new ArgumentException("Too many lighting blockers (including sprite geometry).");
                        var hull = FromArtworkBounds(entity, fields, width, height, iid, options.LayerId);
                        occluders.Add(hull);
                    }
                }
            }
        }
        foreach (LightingOccluder blocker in occluders) blocker.Validate();
        return new LdtkLightingScene(levelId, lights.AsReadOnly(), occluders.AsReadOnly(),
            new ReadOnlyDictionary<string, LightingSurfaceOptions>(surfaces),
            new ReadOnlyDictionary<string, LightPoint>(centers), sceneLayers);
    }

    private static string LayerId(Dictionary<string, JsonElement> fields,
        SceneLightingLayerSet layers, RenderLayerGroup group)
    {
        if (!fields.TryGetValue("RenderLayer", out JsonElement raw) ||
            raw.ValueKind == JsonValueKind.Null ||
            (raw.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(raw.GetString())))
            return layers.DefaultFor(group).Id;
        if (raw.ValueKind != JsonValueKind.String)
            throw new ArgumentException("RenderLayer must be an ID string.");
        string id = raw.GetString()!;
        var selected = layers.Get(id);
        if (selected.Group != group)
            throw new ArgumentException("RenderLayer " + id + " is not in expected depth group " + group);
        return selected.Id;
    }

    private static IReadOnlyList<string>? CsvLayers(Dictionary<string, JsonElement> fields,
        string key, SceneLightingLayerSet layers)
    {
        if (!fields.TryGetValue(key, out JsonElement raw) || raw.ValueKind == JsonValueKind.Null)
            return null;
        if (raw.ValueKind != JsonValueKind.String)
            throw new ArgumentException(key + " must be a comma-separated list of layer IDs.");
        string str = raw.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(str)) return null; // empty = use legacy channel default
        string[] ids = str.Split(',', StringSplitOptions.TrimEntries);
        if (ids.Length is < 1 or > 32 || ids.Any(id => !layers.Contains(id)) ||
            ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
            throw new ArgumentException("Unknown/duplicate layer ID in " + key);
        return Array.AsReadOnly(ids);
    }

    private static Dictionary<string, JsonElement> ReadFields(JsonElement entity)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement field in entity.GetProperty("fieldInstances").EnumerateArray())
        {
            string key = field.GetProperty("__identifier").GetString() ?? "";
            if (!fields.TryAdd(key, field.GetProperty("__value")))
                throw new ArgumentException("Duplicate lighting field " + key);
        }
        return fields;
    }
    private static LightingSurfaceMode SurfaceMode(Dictionary<string, JsonElement> fields)
    {
        if (!fields.TryGetValue("LightingMode", out JsonElement raw) ||
            raw.ValueKind == JsonValueKind.Null) return LightingSurfaceMode.Inherit;
        if (raw.ValueKind != JsonValueKind.String)
            throw new ArgumentException("LightingMode must be a string: Inherit, Lit, Unlit, Silhouette.");
        string value = raw.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(value)) return LightingSurfaceMode.Inherit;
        if (!Enum.TryParse(value, ignoreCase: false, out LightingSurfaceMode mode) ||
            !Enum.IsDefined(mode))
            throw new ArgumentException("Unknown LightingMode: " + value);
        return mode;
    }

    private static bool Bool(Dictionary<string, JsonElement> fields, string key, bool fallback)
    {
        if (!fields.TryGetValue(key, out JsonElement value)) return fallback;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ArgumentException("Lighting field " + key + " must be boolean.")
        };
    }
    private static double Number(Dictionary<string, JsonElement> fields, string key,
        double fallback, double min, double max)
    {
        if (!fields.TryGetValue(key, out JsonElement v)) return fallback;
        if (v.ValueKind != JsonValueKind.Number) throw new ArgumentException("Expected numeric " + key);
        double n = v.GetDouble();
        if (!double.IsFinite(n) || n < min || n > max)
            throw new ArgumentOutOfRangeException(key, "Lighting number outside accepted range.");
        return n;
    }
    private static LightRgb ReadColor(Dictionary<string, JsonElement> fields, string key, LightRgb fallback)
    {
        if (!fields.TryGetValue(key, out JsonElement v)) return fallback;
        string value = v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
        if (value.Length != 7 || value[0] != '#' ||
            !int.TryParse(value.AsSpan(1), NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out int rgb))
            throw new ArgumentException("Lighting color must be #RRGGBB.");
        return new LightRgb(((rgb >> 16) & 255) / 255.0, ((rgb >> 8) & 255) / 255.0,
            (rgb & 255) / 255.0);
    }
    private static LightChannel ReadPlane(Dictionary<string, JsonElement> fields, string kind)
    {
        if (!fields.TryGetValue("Plane", out JsonElement plane) || plane.ValueKind != JsonValueKind.String)
            return LightChannel.Terrain;
        return plane.GetString() switch
        {
            "BG" or "Background" => LightChannel.Backdrop,
            "FG" or "Foreground" => LightChannel.Effects,
            "Terrain" => LightChannel.Terrain,
            _ => throw new ArgumentException("Invalid light surface Plane on " + kind)
        };
    }
    private static LightingOccluder FromBounds(string id, double l, double b, double r, double t,
        string layerId) => new()
    {
        Id = id, LayerId = layerId,
        Vertices = [new LightPoint(l, b), new LightPoint(r, b),
            new LightPoint(r, t), new LightPoint(l, t)]
    };

    private static LightingOccluder FromArtworkBounds(JsonElement raw,
        Dictionary<string, JsonElement> fields, int levelWidth, int levelHeight, string id, string layerId)
    {
        double x = raw.GetProperty("px")[0].GetDouble();
        double y = raw.GetProperty("px")[1].GetDouble();
        double w = raw.GetProperty("width").GetDouble();
        double h = raw.GetProperty("height").GetDouble();
        JsonElement pivot = raw.GetProperty("__pivot");
        double px = pivot[0].GetDouble(), py = pivot[1].GetDouble();
        double degrees = Number(fields, "RotationDeg", 0, -360, 360);
        if (w <= 0 || h <= 0 || !double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(px) || !double.IsFinite(py) || px < 0 || px > 1 || py < 0 || py > 1)
            throw new ArgumentException("Invalid authored shadow caster sprite dimensions.");
        double a = degrees * Math.PI / 180;
        double co = Math.Cos(a), si = Math.Sin(a);
        LightPoint Transform(double ux, double uy)
        {
            double dx = ux - px * w, dy = uy - py * h;
            double sx = x + dx * co - dy * si;
            double sy = y + dx * si + dy * co;
            return new LightPoint(sx * UnitsPerPixel, (levelHeight - sy) * UnitsPerPixel);
        }
        return new LightingOccluder
        {
            Id = id, LayerId = layerId,
            Vertices = [Transform(0, 0), Transform(0, h), Transform(w, h), Transform(w, 0)]
        };
    }
}
