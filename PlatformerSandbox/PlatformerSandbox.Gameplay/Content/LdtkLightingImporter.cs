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
        SceneLightingLayerSet? layers = null,
        IReadOnlyDictionary<string, PresentationLightMotion>? lightMotions = null,
        IReadOnlyDictionary<string, PresentationOccluderMotion>? occluderMotions = null,
        double animationSeconds = 0)
    {
        LightMotions = lightMotions ?? new ReadOnlyDictionary<string, PresentationLightMotion>(
            new Dictionary<string, PresentationLightMotion>(StringComparer.Ordinal));
        OccluderMotions = occluderMotions ?? new ReadOnlyDictionary<string, PresentationOccluderMotion>(
            new Dictionary<string, PresentationOccluderMotion>(StringComparer.Ordinal));
        AnimationSeconds = animationSeconds;
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
    /// <summary>Optional, presentation-only animation keyed by authored light IID.</summary>
    public IReadOnlyDictionary<string, PresentationLightMotion> LightMotions { get; }
    public IReadOnlyDictionary<string, PresentationOccluderMotion> OccluderMotions { get; }
    public double AnimationSeconds { get; }
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
        var lightMotions = new Dictionary<string, PresentationLightMotion>(StringComparer.Ordinal);
        var occluders = new List<LightingOccluder>();
        var occluderMotions = new Dictionary<string, PresentationOccluderMotion>(StringComparer.Ordinal);
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
                        Tag = OptionalLightTag(fields),
                        Position = new LightPoint(e.Bounds.Center.X, e.Bounds.Center.Y),
                        Color = ReadColor(fields, "Color", new LightRgb(1, .75, .5)),
                        Radius = Number(fields, "RadiusWorld", 5, .1, 60),
                        Intensity = Number(fields, "Intensity", 1.4, 0, 4),
                        Shape = LightShapeKind(fields),
                        Direction = LightShapeDirection(fields),
                        ConeAngleDegrees = Number(fields, "ConeAngleDegrees", 55, 5, 170),
                        ConeSoftness = Number(fields, "ConeSoftness", .15, 0, .95),
                        TubeLength = Number(fields, "TubeLengthWorld", 3, .1, 40),
                        EmitterFootprint = Enum.TryParse<AreaEmitterFootprint>(Text(fields, "EmitterFootprint", "Line"), false, out var footprint) && Enum.IsDefined(footprint)
                            ? footprint : throw new ArgumentException("EmitterFootprint must be Line, Disc or Rectangle."),
                        EmitterWidth = Number(fields, "EmitterWidthWorld", 2, .1, 40),
                        PulseDepth = Number(fields, "LightPulseDepth", 0, 0, 1),
                        PulseHz = Number(fields, "LightPulseHz", 1, .01, 5),
                        PulseTint = fields.TryGetValue("LightPulseTint", out var pulseTint) && pulseTint.ValueKind != JsonValueKind.Null
                            ? ReadColor(fields, "LightPulseTint", LightRgb.White) : null,
                        DirectionalShadowLength = Number(fields, "ShadowLengthWorld", 6, .25, 30),
                        Priority = (int)Number(fields, "Priority", 1, -100, 100),
                        CastShadows = Bool(fields, "CastShadows", true),
                        VirtualZ = Number(fields, "LightDepthZ", 0, -100, 100),
                        ReceiverDirectionalDepth = Number(fields, "ReceiverDirectionalDepth", 1, -4, 4),
                        Channels = LightChannel.All,
                        AffectsLayers = CsvLayers(fields, "AffectsLayers", sceneLayers),
                        OccluderLayers = CsvLayers(fields, "OccluderLayers", sceneLayers)
                    };
                    light.Validate();
                    LightLayerRouting.ValidateRouting(light, sceneLayers);
                    lights.Add(light);
                    PresentationLightMotion motion = ReadLightMotion(fields);
                    if (motion.Axis != PresentationLightMotionAxis.Static)
                        lightMotions.Add(iid, motion);
                }
                else if (kind == BlockerEntity)
                {
                    if (occluders.Count >= 96) throw new ArgumentException("Too many authored light blockers.");
                    LdtkAuthoredEntity e = new(entity, width, height);
                    string blockerLayer = LayerId(fields, sceneLayers,
                        RenderLayerGroup.Gameplay);
                    if (Text(fields, "ShadowShape", "Rectangle") != "Rectangle")
                    {
                        var hulls = FromArtworkBounds(entity, fields, width, height, iid, blockerLayer);
                        if (occluders.Count + hulls.Count > 96)
                            throw new ArgumentException("Too many lighting blocker contours.");
                        occluders.AddRange(hulls);
                    }
                    else occluders.Add(FromBounds(iid, e.Bounds.Left, e.Bounds.Bottom,
                        e.Bounds.Right, e.Bounds.Top, blockerLayer));
                    var motion = ReadOccluderMotion(fields,
                        new LightPoint(e.Bounds.Center.X, e.Bounds.Center.Y));
                    if (motion.IsAnimated) occluderMotions.Add(iid, motion);
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
                    string receiverText = Text(fields, "ReceiverProjection", "Legacy");
                    if (!Enum.TryParse<ReceiverProjectionMode>(receiverText, false, out var receiverMode) ||
                        !Enum.IsDefined(receiverMode) || receiverText != receiverMode.ToString())
                        throw new ArgumentException("Unknown ReceiverProjection: " + receiverText);
                    double topPx = entity.GetProperty("px")[1].GetDouble() -
                        entity.GetProperty("__pivot")[1].GetDouble() * entity.GetProperty("height").GetDouble();
                    var receiver = new SurfaceReceiverProjection
                    {
                        Mode = receiverMode, SeamY = (height - topPx) * UnitsPerPixel,
                        BandWorld = Number(fields, "ReceiverBandWorld", .75, .05, 20),
                        Compression = Number(fields, "ReceiverCompression", .3, .1, 1),
                        Shear = Number(fields, "ReceiverShear", .15, -2, 2),
                        PitchDegrees = Number(fields, "ReceiverPitchDeg", 90, -180, 180),
                        FacePitchDegrees = Number(fields, "ReceiverFacePitchDeg", 0, -180, 180),
                        OrientationStrength = Number(fields, "ReceiverNormalStrength", .8, 0, 1)
                    };
                    if (receiver.Active && (kind != "TerrainArtwork" ||
                        Number(fields, "RotationDeg", 0, -360, 360) != 0 ||
                        Number(fields, "ParallaxX", 0, -100, 200) != 0 ||
                        Number(fields, "ParallaxY", 0, -100, 200) != 0 ||
                        layerDefaults.ParallaxMultiplier != 1 ||
                        Bool(fields, "FlipY", false)))
                        throw new NotSupportedException("Oriented receivers require stationary, axis-aligned, world-parallax TerrainArtwork.");
                    LightingSurfaceOptions options = new()
                    {
                        ReceivesLight = Bool(fields, "ReceivesLight", true),
                        ReceivesShadows = Bool(fields, "ReceivesShadows", layerDefaults.ReceivesShadows),
                        Receiver = receiver,
                        CastsShadows = Bool(fields, "CastsShadows", layerDefaults.CastsShadowsByDefault),
                        ContactShadow = Bool(fields, "ContactShadow", false),
                        LightingMode = SurfaceMode(fields),
                        Channel = plane,
                        LayerId = surfaceLayerId,
                        MaterialShading = MaterialMode(fields),
                        NormalMap = OptionalMap(fields, "NormalMap"),
                        EdgeMap = OptionalMap(fields, "EdgeMap"),
                        MaterialNormalStrength = Number(fields, "MaterialNormalStrength", 1, 0, 1),
                        MaterialRimStrength = Number(fields, "MaterialRimStrength", 1, 0, 2),
                        MaterialDepthZ = Number(fields, "MaterialDepthZ", 0, -100, 100),
                        BackRimOnOverlap = Bool(fields, "BackRimOnOverlap", false),
                        BackRimWidthPx = Number(fields, "BackRimWidthPx", 4.5, .5, 24),
                        BackRimIntensity = Number(fields, "BackRimIntensity", 1.25, 0, 4),
                        EmissionColor = ReadColor(fields, "EmissionColor", LightRgb.White),
                        EmissionIntensity = Number(fields, "EmissionIntensity", 0, 0, 8),
                        EmissionMap = OptionalMap(fields, "EmissionMap"),
                        EmissiveLightRadius = Number(fields, "EmissiveLightRadius", 0, 0, 40),
                        EmissiveLightIntensity = Number(fields, "EmissiveLightIntensity", 1, 0, 4),
                        EmissiveLightCastsShadows = Bool(fields, "EmissiveLightCastsShadows", true)
                    };
                    options.Validate();
                    if (options.MaterialShading != ProductionSurfaceShading.Legacy &&
                        options.LightingMode != LightingSurfaceMode.Lit &&
                        sceneLayers.Get(options.LayerId).Mode == RenderLayerLightingMode.Unlit)
                        throw new NotSupportedException(
                            "Stage 15B GPU material opt-in requires a lit scene layer.");
                    if (options.MaterialShading != ProductionSurfaceShading.Legacy &&
                        kind != "TerrainArtwork")
                        throw new NotSupportedException(
                            "Stage 15B GPU material opt-in currently supports TerrainArtwork only.");
                    var blockerMotion = ReadOccluderMotion(fields,
                        ArtworkPivot(entity, height));
                    if (blockerMotion.IsAnimated)
                    {
                        if (kind != "TerrainArtwork")
                            throw new NotSupportedException(
                                "Animated artwork is supported for TerrainArtwork only.");
                        if (Number(fields, "ParallaxX", 0, -100, 200) != 0 ||
                            Number(fields, "ParallaxY", 0, -100, 200) != 0)
                            throw new NotSupportedException(
                                "Animated shadow casters must use world parallax (0% authoring offset).");
                        if (!options.CastsShadows)
                            throw new ArgumentException("Animated TerrainArtwork must cast shadows.");
                        occluderMotions.Add(iid, blockerMotion);
                    }
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
                        var hulls = FromArtworkBounds(entity, fields, width, height, iid, options.LayerId);
                        if (occluders.Count + hulls.Count > 96)
                            throw new ArgumentException("Too many lighting blocker contours.");
                        bool litFace = options.ReceivesLight &&
                            (options.LightingMode == LightingSurfaceMode.Lit ||
                             options.LightingMode == LightingSurfaceMode.Inherit &&
                             layerDefaults.Mode != RenderLayerLightingMode.Unlit);
                        foreach (var hull in hulls)
                            occluders.Add(litFace ? hull with
                            {
                                LitFaceLayer = layerDefaults,
                                LitFaceDepthZ = options.MaterialDepthZ
                            } : hull);
                    }
                }
            }
        }
        foreach (LightingOccluder blocker in occluders) blocker.Validate();
        return new LdtkLightingScene(levelId, lights.AsReadOnly(), occluders.AsReadOnly(),
            new ReadOnlyDictionary<string, LightingSurfaceOptions>(surfaces),
            new ReadOnlyDictionary<string, LightPoint>(centers), sceneLayers,
            new ReadOnlyDictionary<string, PresentationLightMotion>(lightMotions),
            new ReadOnlyDictionary<string, PresentationOccluderMotion>(occluderMotions));
    }

    private static string OptionalLightTag(Dictionary<string, JsonElement> fields)
    {
        if (!fields.TryGetValue("LightRole", out var raw) || raw.ValueKind == JsonValueKind.Null)
            return "";
        if (raw.ValueKind != JsonValueKind.String)
            throw new ArgumentException("LightRole must be a string.");
        string tag = raw.GetString() ?? "";
        if (tag.Length > 32 || tag.Any(c => !(char.IsAsciiLetterOrDigit(c) || c == '_')))
            throw new ArgumentException("LightRole is an invalid lighting tag.");
        return tag;
    }

    private static LightShape LightShapeKind(Dictionary<string, JsonElement> fields)
    {
        if (!fields.TryGetValue("LightType", out var raw) || raw.ValueKind == JsonValueKind.Null)
            return LightShape.Point;
        if (raw.ValueKind != JsonValueKind.String ||
            !Enum.TryParse<LightShape>(raw.GetString(), false, out var kind) ||
            !Enum.IsDefined(kind))
            throw new ArgumentException("LightType must be Point, Directional, Spot or Tube.");
        return kind;
    }

    private static LightPoint LightShapeDirection(Dictionary<string, JsonElement> fields)
    {
        double degrees = Number(fields, "DirectionDegrees", -33, -360, 360);
        double radians = degrees * Math.PI / 180;
        return new LightPoint(Math.Cos(radians), Math.Sin(radians));
    }

    private static ProductionSurfaceShading MaterialMode(Dictionary<string, JsonElement> fields)
    {
        if (!fields.TryGetValue("MaterialShading", out var raw) || raw.ValueKind == JsonValueKind.Null)
            return ProductionSurfaceShading.Legacy;
        if (raw.ValueKind != JsonValueKind.String ||
            !Enum.TryParse<ProductionSurfaceShading>(raw.GetString(), false, out var parsed) ||
            !Enum.IsDefined(parsed))
            throw new ArgumentException("MaterialShading must be Legacy, Flat, Normal, Rim or Hybrid.");
        return parsed;
    }

    private static string? OptionalMap(Dictionary<string, JsonElement> fields, string name)
    {
        if (!fields.TryGetValue(name, out var raw) || raw.ValueKind == JsonValueKind.Null)
            return null;
        if (raw.ValueKind != JsonValueKind.String)
            throw new ArgumentException(name + " must be a relative PNG path.");
        string? path = raw.GetString();
        if (string.IsNullOrWhiteSpace(path)) return null;
        ProductionSurfaceSubmission.ValidateMap(path);
        return path;
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
    private static LightPoint ArtworkPivot(JsonElement entity, int levelHeight)
    {
        JsonElement xy = entity.GetProperty("px");
        return new LightPoint(xy[0].GetDouble() * UnitsPerPixel,
            (levelHeight - xy[1].GetDouble()) * UnitsPerPixel);
    }

    private static PresentationOccluderMotion ReadOccluderMotion(
        Dictionary<string, JsonElement> fields, LightPoint anchor)
    {
        string axisText = Text(fields, "BlockerMotionAxis", "Static");
        string rotationText = Text(fields, "BlockerRotationMode", "Static");
        if (!Enum.TryParse(axisText, false, out PresentationOccluderMotionAxis axis) ||
            !Enum.IsDefined(axis))
            throw new ArgumentException("BlockerMotionAxis must be Static/Horizontal/Vertical.");
        if (!Enum.TryParse(rotationText, false, out PresentationOccluderRotationMode rotation) ||
            !Enum.IsDefined(rotation))
            throw new ArgumentException("Invalid BlockerRotationMode.");
        // Explicit defaults keep old LDtk scenes completely static.
        var motion = new PresentationOccluderMotion(anchor, axis,
            Number(fields, "BlockerTravelWorld", 0, 0, 10),
            Number(fields, "BlockerCyclesPerSecond", 0, 0, 2), rotation,
            Number(fields, "BlockerAngularSpeedDegPerSecond", 0, 0, 360),
            Number(fields, "BlockerSwingDegrees", 0, 0, 180));
        motion.Validate();
        return motion;
    }

    private static string Text(Dictionary<string, JsonElement> fields, string key, string fallback)
    {
        if (!fields.TryGetValue(key, out JsonElement value) ||
            value.ValueKind == JsonValueKind.Null) return fallback;
        if (value.ValueKind != JsonValueKind.String)
            throw new ArgumentException(key + " must be a string.");
        return value.GetString() ?? fallback;
    }

    private static PresentationLightMotion ReadLightMotion(Dictionary<string, JsonElement> fields)
    {
        if (!fields.TryGetValue("MotionAxis", out JsonElement axisValue) ||
            axisValue.ValueKind == JsonValueKind.Null)
            return new PresentationLightMotion(PresentationLightMotionAxis.Static, 0, 0);
        if (axisValue.ValueKind != JsonValueKind.String ||
            !Enum.TryParse(axisValue.GetString(), ignoreCase: false,
                out PresentationLightMotionAxis axis) || !Enum.IsDefined(axis))
            throw new ArgumentException("MotionAxis must be Static, Horizontal, or Vertical.");
        if (axis == PresentationLightMotionAxis.Static)
            return new PresentationLightMotion(axis, 0, 0);
        var motion = new PresentationLightMotion(axis,
            Number(fields, "MotionDistanceWorld", 2, .01, 10),
            Number(fields, "MotionCyclesPerSecond", .2, .01, 2));
        motion.Validate();
        return motion;
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

    private static IReadOnlyList<LightingOccluder> FromArtworkBounds(JsonElement raw,
        Dictionary<string, JsonElement> fields, int levelWidth, int levelHeight, string id, string layerId)
    {
        double x = raw.GetProperty("px")[0].GetDouble();
        double y = raw.GetProperty("px")[1].GetDouble();
        double w = raw.GetProperty("width").GetDouble();
        double h = raw.GetProperty("height").GetDouble();
        JsonElement pivot = raw.GetProperty("__pivot");
        double px = pivot[0].GetDouble(), py = pivot[1].GetDouble();
        double degrees = Number(fields, "RotationDeg", 0, -360, 360);
        // An art PNG may contain transparent margin around its visible pixels.
        // Opt-in inset keeps the caster hull and back-rim contour aligned with
        // that visible silhouette. Zero preserves every existing level.
        double shadowInset = Number(fields, "ShadowHullInsetPx", 0, 0, 24);
        if (w <= 0 || h <= 0 || shadowInset * 2 >= w || shadowInset * 2 >= h ||
            !double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(px) || !double.IsFinite(py) || px < 0 || px > 1 || py < 0 || py > 1)
            throw new ArgumentException("Invalid authored shadow caster sprite dimensions or inset.");
        double a = degrees * Math.PI / 180;
        double co = Math.Cos(a), si = Math.Sin(a);
        bool flipX = Bool(fields, "FlipX", false), flipY = Bool(fields, "FlipY", false);
        LightPoint Transform(double ux, double uy)
        {
            if (flipX) ux = w - ux;
            if (flipY) uy = h - uy;
            double dx = ux - px * w, dy = uy - py * h;
            double sx = x + dx * co - dy * si;
            double sy = y + dx * si + dy * co;
            return new LightPoint(sx * UnitsPerPixel, (levelHeight - sy) * UnitsPerPixel);
        }
        string shape = Text(fields, "ShadowShape", "Rectangle");
        if (shape == "Custom")
        {
            if (shadowInset != 0) throw new ArgumentException("Custom vertices already define the hull; set ShadowHullInsetPx to 0.");
            var contours = ShadowContourAuthoring.Parse(Text(fields, "ShadowVertices", ""));
            return contours.Select((points, index) => new LightingOccluder
            {
                Id = index == 0 ? id : id + ":contour:" + index,
                OwnerId = id, LayerId = layerId,
                Vertices = points.Select(p => Transform(p.X * w, p.Y * h)).ToArray()
            }).ToArray();
        }
        LightPoint[] vertices = shape switch
        {
            "Rectangle" => [Transform(shadowInset, shadowInset),
                Transform(shadowInset, h - shadowInset),
                Transform(w - shadowInset, h - shadowInset),
                Transform(w - shadowInset, shadowInset)],
            "Triangle" => [Transform(w * .5, shadowInset),
                Transform(shadowInset, h - shadowInset),
                Transform(w - shadowInset, h - shadowInset)],
            "Trapezoid" => [Transform(w * .22, shadowInset),
                Transform(shadowInset, h - shadowInset),
                Transform(w - shadowInset, h - shadowInset),
                Transform(w * .78, shadowInset)],
            // A five-point house/obelisk silhouette (tip, shoulders, and base).
            // LDtk imagery may remain independently alpha-blended.
            "Pentagon" => [Transform(w * .5, shadowInset),
                Transform(w - shadowInset, h * .28),
                Transform(w - shadowInset, h - shadowInset),
                Transform(shadowInset, h - shadowInset),
                Transform(shadowInset, h * .28)],
            _ => throw new ArgumentException(
                "ShadowShape must be Rectangle, Triangle, Trapezoid, Pentagon, or Custom.")
        };
        return new[] { new LightingOccluder { Id = id, LayerId = layerId, Vertices = vertices } };
    }
}
