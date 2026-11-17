using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PlatformerSandbox.Gameplay.Content;

/// <summary>Presentation-only, free-positioned LDtk artwork. No physical collider or gameplay entity.</summary>
public sealed record LdtkScenerySprite(
    string InstanceId, LdtkVisualPlane Plane, int Depth,
    string TilesetPath, int AtlasWidthPx, int AtlasHeightPx,
    int SourceX, int SourceY, int SourceWidth, int SourceHeight,
    double PixelX, double PixelY, int WidthPx, int HeightPx,
    float PivotX, float PivotY, float RotationDegrees, float Opacity,
    float ScrollFactorX, float ScrollFactorY, bool FlipX, bool FlipY);

public sealed record LdtkSceneryDefinition(string LevelId, IReadOnlyList<LdtkScenerySprite> Sprites)
{
    public int SpriteCount => Sprites.Count;
}

/// <summary>
/// Imports LDtk ScenerySprite entities. Each entity's Art field is an LDtk Tile value
/// (the built-in __tile preview is also supported). The native LDtk entity width,
/// height, pivot and pixel position are the on-screen authoring handles. The
/// custom fields control visual planes, order, rotation, opacity and parallax.
/// It never creates or mutates an authoritative world/collider/player object.
/// </summary>
public static class LdtkSceneryImporter
{
    public static LdtkSceneryDefinition FromFile(string filename, string? levelIdentifier = null) =>
        FromJson(File.ReadAllText(filename), levelIdentifier);

    public static LdtkSceneryDefinition FromJson(string json, string? levelIdentifier = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("LDtk JSON is required.", nameof(json));
        using var doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.GetProperty("externalLevels").GetBoolean() ||
            root.GetProperty("simplifiedExport").GetBoolean())
            throw new NotSupportedException("Scenery requires embedded, non-simplified LDtk JSON.");
        JsonElement level = default;
        bool found = false;
        foreach (JsonElement candidate in root.GetProperty("levels").EnumerateArray())
        {
            if (levelIdentifier is not null && candidate.GetProperty("identifier").GetString() != levelIdentifier)
                continue;
            level = candidate;
            found = true;
            break;
        }
        if (!found) throw new ArgumentException("LDtk scenery level not found: " + levelIdentifier);
        string id = level.GetProperty("identifier").GetString() ?? "";
        int levelWidth = level.GetProperty("pxWid").GetInt32();
        int levelHeight = level.GetProperty("pxHei").GetInt32();
        var tilesets = new Dictionary<int, JsonElement>();
        foreach (JsonElement tileset in root.GetProperty("defs").GetProperty("tilesets").EnumerateArray())
            if (!tilesets.TryAdd(tileset.GetProperty("uid").GetInt32(), tileset))
                throw new ArgumentException("Duplicate tileset UID.");
        var sprites = new List<LdtkScenerySprite>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement layer in level.GetProperty("layerInstances").EnumerateArray())
        {
            if (layer.GetProperty("__type").GetString() != "Entities") continue;
            if (layer.TryGetProperty("visible", out var visible) && visible.ValueKind == JsonValueKind.False)
                continue;
            int offsetX = layer.GetProperty("__pxTotalOffsetX").GetInt32();
            int offsetY = layer.GetProperty("__pxTotalOffsetY").GetInt32();
            foreach (JsonElement entity in layer.GetProperty("entityInstances").EnumerateArray())
            {
                if (entity.GetProperty("__identifier").GetString() is not ("ScenerySprite" or "PaintedSprite")) continue;
                string iid = entity.GetProperty("iid").GetString() ?? "";
                if (string.IsNullOrWhiteSpace(iid) || !seen.Add(iid))
                    throw new ArgumentException("Missing or duplicate ScenerySprite instance IID.");
                var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (JsonElement field in entity.GetProperty("fieldInstances").EnumerateArray())
                {
                    string name = field.GetProperty("__identifier").GetString() ?? "";
                    if (!fields.TryAdd(name, field.GetProperty("__value")))
                        throw new ArgumentException("Duplicate field " + name + " on " + iid);
                }

                // The editable Tile field is authoritative when supplied; the
                // LDtk entity preview tile is a compatible fallback.
                JsonElement image = fields.TryGetValue("Art", out JsonElement art) &&
                    art.ValueKind != JsonValueKind.Null ? art : entity.GetProperty("__tile");
                if (image.ValueKind != JsonValueKind.Object)
                    throw new ArgumentException("ScenerySprite " + iid + " needs an Art Tile selection.");
                int uid = image.GetProperty("tilesetUid").GetInt32();
                if (!tilesets.TryGetValue(uid, out JsonElement tileset))
                    throw new ArgumentException("ScenerySprite " + iid + " refers to unknown tileset " + uid);
                string? path = tileset.GetProperty("relPath").GetString();
                if (string.IsNullOrWhiteSpace(path))
                    throw new ArgumentException("ScenerySprite " + iid + " tileset lacks a PNG path.");
                LdtkVisualImporter.ValidateRelativePath(path);
                int atlasWidth = tileset.GetProperty("pxWid").GetInt32();
                int atlasHeight = tileset.GetProperty("pxHei").GetInt32();
                JsonElement source = image.GetProperty("srcRect");
                if (source.GetArrayLength() != 4) throw new ArgumentException("Invalid ScenerySprite source rect.");
                int sx = source[0].GetInt32(), sy = source[1].GetInt32();
                int sw = source[2].GetInt32(), sh = source[3].GetInt32();
                if (atlasWidth <= 0 || atlasHeight <= 0 || atlasWidth > 16384 || atlasHeight > 16384 ||
                    sx < 0 || sy < 0 || sw <= 0 || sh <= 0 || sw > 4096 || sh > 4096 ||
                    (long)sx + sw > atlasWidth || (long)sy + sh > atlasHeight)
                    throw new ArgumentException("Invalid ScenerySprite source rect on " + iid);
                int width = entity.GetProperty("width").GetInt32();
                int height = entity.GetProperty("height").GetInt32();
                if (width < 1 || height < 1 || width > 4096 || height > 4096)
                    throw new ArgumentException("Invalid ScenerySprite display size on " + iid);
                JsonElement px = entity.GetProperty("px");
                JsonElement pivot = entity.GetProperty("__pivot");
                double x = px[0].GetDouble() + offsetX, y = px[1].GetDouble() + offsetY;
                float pivotX = pivot[0].GetSingle(), pivotY = pivot[1].GetSingle();
                if (!double.IsFinite(x) || !double.IsFinite(y) ||
                    x < -levelWidth * 2.0 || x > levelWidth * 3.0 ||
                    y < -levelHeight * 2.0 || y > levelHeight * 3.0 ||
                    !float.IsFinite(pivotX) || !float.IsFinite(pivotY) ||
                    pivotX is < 0 or > 1 || pivotY is < 0 or > 1)
                    throw new ArgumentException("Invalid ScenerySprite position/pivot on " + iid);

                string planeName = ReadString(fields, "Plane", "Terrain");
                LdtkVisualPlane plane = planeName switch
                {
                    "Background" or "BG" => LdtkVisualPlane.Background,
                    "Terrain" => LdtkVisualPlane.Terrain,
                    "Foreground" or "FG" => LdtkVisualPlane.Foreground,
                    _ => throw new ArgumentException("Unknown scenery Plane: " + planeName)
                };
                int depth = ReadInt(fields, "Depth", 0, -10000, 10000);
                float rotate = ReadFloat(fields, "RotationDeg", 0, -360, 360);
                float opacity = ReadFloat(fields, "Opacity", 1, 0, 1);
                float parallaxX = ReadFloat(fields, "ParallaxX", 0, -100, 200);
                float parallaxY = ReadFloat(fields, "ParallaxY", 0, -100, 200);
                bool flipX = ReadBool(fields, "FlipX", false);
                bool flipY = ReadBool(fields, "FlipY", false);
                sprites.Add(new LdtkScenerySprite(iid, plane, depth,
                    path, atlasWidth, atlasHeight, sx, sy, sw, sh,
                    x, y, width, height, pivotX, pivotY, rotate, opacity,
                    1f + parallaxX / 100f, 1f + parallaxY / 100f, flipX, flipY));
            }
        }
        sprites.Sort((a, b) =>
        {
            int c = a.Plane.CompareTo(b.Plane);
            if (c == 0) c = a.Depth.CompareTo(b.Depth);
            if (c == 0) c = string.CompareOrdinal(a.InstanceId, b.InstanceId);
            return c;
        });
        return new LdtkSceneryDefinition(id, sprites.AsReadOnly());
    }

    private static string ReadString(Dictionary<string, JsonElement> fields, string name, string fallback) =>
        !fields.TryGetValue(name, out var value) ? fallback :
        value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback :
        throw new ArgumentException("Invalid scenery string field: " + name);
    private static int ReadInt(Dictionary<string, JsonElement> fields, string name, int fallback, int min, int max)
    {
        if (!fields.TryGetValue(name, out var field)) return fallback;
        if (field.ValueKind != JsonValueKind.Number || !field.TryGetInt32(out int value) || value < min || value > max)
            throw new ArgumentException("Invalid scenery integer field: " + name);
        return value;
    }
    private static float ReadFloat(Dictionary<string, JsonElement> fields, string name, float fallback, float min, float max)
    {
        if (!fields.TryGetValue(name, out var field)) return fallback;
        if (field.ValueKind != JsonValueKind.Number) throw new ArgumentException("Invalid scenery float field: " + name);
        float value = field.GetSingle();
        if (!float.IsFinite(value) || value < min || value > max)
            throw new ArgumentException("Out-of-range scenery float field: " + name);
        return value;
    }
    private static bool ReadBool(Dictionary<string, JsonElement> fields, string name, bool fallback)
    {
        if (!fields.TryGetValue(name, out var field)) return fallback;
        return field.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ArgumentException("Invalid scenery bool field: " + name)
        };
    }
}

/// <summary>Camera-relative anchor position. Independent of the collision solver.</summary>
public static class LdtkSceneryTransform
{
    public static (double X, double Y, double Width, double Height) Project(
        LdtkScenerySprite sprite, double cameraLeftPx, double cameraTopPx)
    {
        return (sprite.PixelX - cameraLeftPx * sprite.ScrollFactorX,
            sprite.PixelY - cameraTopPx * sprite.ScrollFactorY,
            sprite.WidthPx, sprite.HeightPx);
    }
}
