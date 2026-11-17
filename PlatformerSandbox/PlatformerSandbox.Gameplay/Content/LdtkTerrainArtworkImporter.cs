using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PlatformerSandbox.Gameplay.Content;

/// <summary>
/// Hand-positioned standalone PNG art. Entirely presentation-only: an image
/// never creates a solid cell, collider, hitbox, or new gameplay entity.
/// </summary>
public sealed record LdtkTerrainArtwork(
    string InstanceId, LdtkVisualPlane Plane, int Depth, string ImagePath,
    double PixelX, double PixelY, int WidthPx, int HeightPx,
    float PivotX, float PivotY, float RotationDegrees, float Opacity,
    float ScrollFactorX, float ScrollFactorY, bool FlipX, bool FlipY,
    string TintHex);

public sealed record LdtkTerrainArtworkDefinition(
    string LevelId, bool DrawCollisionBackdrop, IReadOnlyList<LdtkTerrainArtwork> Artworks)
{
    public int ArtworkCount => Artworks.Count;
}

/// <summary>
/// Reads TerrainArtwork entities with an LDtk FilePath field named Image,
/// plus the optional level Bool field DrawCollisionBackdrop. Position and
/// size are native, resizable LDtk entity data. All paths are relative to the
/// LDtk project directory and are checked again by the graphics loader.
/// </summary>
public static class LdtkTerrainArtworkImporter
{
    public static LdtkTerrainArtworkDefinition FromFile(string filename,
        string? levelIdentifier = null) =>
        FromJson(File.ReadAllText(filename), levelIdentifier);

    public static LdtkTerrainArtworkDefinition FromJson(string json,
        string? levelIdentifier = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("LDtk JSON is required.", nameof(json));
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.GetProperty("externalLevels").GetBoolean() ||
            root.GetProperty("simplifiedExport").GetBoolean())
            throw new NotSupportedException("Terrain artwork requires embedded, non-simplified LDtk JSON.");

        JsonElement level = default;
        bool found = false;
        foreach (JsonElement candidate in root.GetProperty("levels").EnumerateArray())
        {
            if (levelIdentifier is not null &&
                candidate.GetProperty("identifier").GetString() != levelIdentifier)
                continue;
            level = candidate;
            found = true;
            break;
        }
        if (!found)
            throw new ArgumentException("LDtk terrain artwork level not found: " + levelIdentifier);
        string id = level.GetProperty("identifier").GetString() ?? "";
        int width = level.GetProperty("pxWid").GetInt32();
        int height = level.GetProperty("pxHei").GetInt32();
        if (width <= 0 || height <= 0)
            throw new ArgumentException("Invalid terrain artwork level bounds.");

        bool drawBacking = true; // Backwards-compatible with Phase 1–3 scenes.
        if (level.TryGetProperty("fieldInstances", out JsonElement levelFields) &&
            levelFields.ValueKind == JsonValueKind.Array)
        {
            bool seen = false;
            foreach (JsonElement field in levelFields.EnumerateArray())
            {
                if (field.GetProperty("__identifier").GetString() != "DrawCollisionBackdrop")
                    continue;
                if (seen) throw new ArgumentException("Duplicate DrawCollisionBackdrop level field.");
                seen = true;
                JsonElement value = field.GetProperty("__value");
                if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new ArgumentException("DrawCollisionBackdrop must be a Bool.");
                drawBacking = value.GetBoolean();
            }
        }

        var art = new List<LdtkTerrainArtwork>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement layer in level.GetProperty("layerInstances").EnumerateArray())
        {
            if (layer.GetProperty("__type").GetString() != "Entities") continue;
            int offsetX = layer.GetProperty("__pxTotalOffsetX").GetInt32();
            int offsetY = layer.GetProperty("__pxTotalOffsetY").GetInt32();
            foreach (JsonElement entity in layer.GetProperty("entityInstances").EnumerateArray())
            {
                if (entity.GetProperty("__identifier").GetString() != "TerrainArtwork") continue;
                string iid = entity.GetProperty("iid").GetString() ?? "";
                if (iid.Length == 0 || !seenIds.Add(iid))
                    throw new ArgumentException("Missing or duplicate TerrainArtwork instance identity.");
                var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (JsonElement field in entity.GetProperty("fieldInstances").EnumerateArray())
                {
                    string key = field.GetProperty("__identifier").GetString() ?? "";
                    if (!fields.TryAdd(key, field.GetProperty("__value")))
                        throw new ArgumentException("Duplicate TerrainArtwork field: " + key);
                }
                string image = ReadString(fields, "Image", "");
                if (string.IsNullOrWhiteSpace(image))
                    throw new ArgumentException("TerrainArtwork " + iid + " requires a PNG Image path.");
                LdtkVisualImporter.ValidateRelativePath(image);
                string plane = ReadString(fields, "Plane", "Terrain");
                LdtkVisualPlane parsedPlane = plane switch
                {
                    "Background" or "BG" => LdtkVisualPlane.Background,
                    "Terrain" => LdtkVisualPlane.Terrain,
                    "Foreground" or "FG" => LdtkVisualPlane.Foreground,
                    _ => throw new ArgumentException("Unknown TerrainArtwork Plane: " + plane)
                };
                int depth = ReadInt(fields, "Depth", 0, -10000, 10000);
                float rotation = ReadFloat(fields, "RotationDeg", 0, -360, 360);
                float opacity = ReadFloat(fields, "Opacity", 1, 0, 1);
                float parallaxX = ReadFloat(fields, "ParallaxX", 0, -100, 200);
                float parallaxY = ReadFloat(fields, "ParallaxY", 0, -100, 200);
                string tint = ReadString(fields, "Tint", "#FFFFFF");
                if (tint.Length != 7 || tint[0] != '#' ||
                    !tint.Skip(1).All(ch =>
                        ch is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f'))
                    throw new ArgumentException("Invalid TerrainArtwork Tint (expected #RRGGBB).");
                int entityWidth = entity.GetProperty("width").GetInt32();
                int entityHeight = entity.GetProperty("height").GetInt32();
                if (entityWidth < 1 || entityHeight < 1 ||
                    entityWidth > 8192 || entityHeight > 8192)
                    throw new ArgumentException("Invalid TerrainArtwork entity size: " + iid);
                JsonElement position = entity.GetProperty("px");
                JsonElement pivot = entity.GetProperty("__pivot");
                double x = position[0].GetDouble() + offsetX;
                double y = position[1].GetDouble() + offsetY;
                float px = pivot[0].GetSingle();
                float py = pivot[1].GetSingle();
                if (!double.IsFinite(x) || !double.IsFinite(y) ||
                    x < -width * 2.0 || x > width * 3.0 ||
                    y < -height * 2.0 || y > height * 3.0 ||
                    !float.IsFinite(px) || !float.IsFinite(py) ||
                    px is < 0 or > 1 || py is < 0 or > 1)
                    throw new ArgumentException("Invalid TerrainArtwork pivot/position: " + iid);
                art.Add(new LdtkTerrainArtwork(iid, parsedPlane, depth, image,
                    x, y, entityWidth, entityHeight, px, py, rotation, opacity,
                    1f + parallaxX / 100f, 1f + parallaxY / 100f,
                    ReadBool(fields, "FlipX", false), ReadBool(fields, "FlipY", false), tint));
            }
        }
        art.Sort((a, b) =>
        {
            int compare = a.Plane.CompareTo(b.Plane);
            if (compare == 0) compare = a.Depth.CompareTo(b.Depth);
            if (compare == 0) compare = string.CompareOrdinal(a.InstanceId, b.InstanceId);
            return compare;
        });
        if (!drawBacking && art.Count == 0)
            throw new ArgumentException("Art-only terrain needs at least one TerrainArtwork entity.");
        return new LdtkTerrainArtworkDefinition(id, drawBacking, art.AsReadOnly());
    }

    private static string ReadString(Dictionary<string, JsonElement> fields,
        string name, string fallback)
    {
        if (!fields.TryGetValue(name, out JsonElement field)) return fallback;
        if (field.ValueKind != JsonValueKind.String)
            throw new ArgumentException("Invalid TerrainArtwork string: " + name);
        return field.GetString() ?? fallback;
    }

    private static int ReadInt(Dictionary<string, JsonElement> fields,
        string name, int fallback, int min, int max)
    {
        if (!fields.TryGetValue(name, out JsonElement field)) return fallback;
        if (field.ValueKind != JsonValueKind.Number ||
            !field.TryGetInt32(out int value) || value < min || value > max)
            throw new ArgumentException("Invalid TerrainArtwork integer: " + name);
        return value;
    }

    private static float ReadFloat(Dictionary<string, JsonElement> fields,
        string name, float fallback, float min, float max)
    {
        if (!fields.TryGetValue(name, out JsonElement field)) return fallback;
        if (field.ValueKind != JsonValueKind.Number)
            throw new ArgumentException("Invalid TerrainArtwork float: " + name);
        float value = field.GetSingle();
        if (!float.IsFinite(value) || value < min || value > max)
            throw new ArgumentException("Out-of-range TerrainArtwork float: " + name);
        return value;
    }

    private static bool ReadBool(Dictionary<string, JsonElement> fields,
        string name, bool fallback)
    {
        if (!fields.TryGetValue(name, out JsonElement field)) return fallback;
        if (field.ValueKind == JsonValueKind.True) return true;
        if (field.ValueKind == JsonValueKind.False) return false;
        throw new ArgumentException("Invalid TerrainArtwork Bool: " + name);
    }
}

/// <summary>Pure camera-relative projection, independent of graphics and physics.</summary>
public static class LdtkTerrainArtworkTransform
{
    public static (double X, double Y, double Width, double Height) Project(
        LdtkTerrainArtwork art, double cameraLeftPx, double cameraTopPx) =>
        (art.PixelX - cameraLeftPx * art.ScrollFactorX,
         art.PixelY - cameraTopPx * art.ScrollFactorY,
         art.WidthPx, art.HeightPx);
}
