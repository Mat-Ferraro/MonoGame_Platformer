using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PlatformerSandbox.Gameplay.Content;

/// <summary>
/// Render-only LDtk layer data. Never used by WorldDefinition or the motor.
/// Order within each plane is back-to-front, matching LDtk's reversed
/// (top-most-first) layerInstances order.
/// </summary>
public enum LdtkVisualPlane { Background, Terrain, Foreground }

public sealed record LdtkVisualTile(
    int PixelX, int PixelY, int SourceX, int SourceY,
    int WidthPx, int HeightPx, int FlipBits, float Opacity);

public sealed record LdtkVisualLayer(
    string Name, LdtkVisualPlane Plane, string TilesetPath,
    int TilesetWidthPx, int TilesetHeightPx,
    float ScrollFactorX, float ScrollFactorY, bool ScaleWithParallax,
    int OffsetX, int OffsetY, float Opacity,
    IReadOnlyList<LdtkVisualTile> Tiles);

public sealed record LdtkVisualDefinition(string LevelId,
    IReadOnlyList<LdtkVisualLayer> Layers)
{
    public int TileCount => Layers.Sum(layer => layer.Tiles.Count);
}

/// <summary>
/// Turns authored LDtk Tiles/AutoLayer JSON into a small, graphics-independent
/// presentation contract. BG_*, Terrain_Art, FG_* establish explicit ordering;
/// IntGrid auto-layer artwork from Collisions may act as terrain art.
/// No pixels, textures, collision or player state are created here.
/// </summary>
public static class LdtkVisualImporter
{
    public static LdtkVisualDefinition FromFile(string filename,
        string? levelIdentifier = null) =>
        FromJson(File.ReadAllText(filename), levelIdentifier);

    public static LdtkVisualDefinition FromJson(string json,
        string? levelIdentifier = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("LDtk JSON is required.", nameof(json));
        using var document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.GetProperty("externalLevels").GetBoolean() ||
            root.GetProperty("simplifiedExport").GetBoolean())
            throw new NotSupportedException("Visual import requires embedded, non-simplified LDtk project JSON.");

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
            throw new ArgumentException("LDtk visual level not found: " + levelIdentifier,
                nameof(levelIdentifier));
        string id = level.GetProperty("identifier").GetString() ?? "";
        int levelWidth = level.GetProperty("pxWid").GetInt32();
        int levelHeight = level.GetProperty("pxHei").GetInt32();

        var layerDefs = new Dictionary<int, JsonElement>();
        foreach (JsonElement def in root.GetProperty("defs").GetProperty("layers").EnumerateArray())
        {
            int uid = def.GetProperty("uid").GetInt32();
            if (!layerDefs.TryAdd(uid, def))
                throw new ArgumentException("Duplicate LDtk layer definition UID " + uid);
        }
        var tilesets = new Dictionary<int, JsonElement>();
        foreach (JsonElement def in root.GetProperty("defs").GetProperty("tilesets").EnumerateArray())
        {
            int uid = def.GetProperty("uid").GetInt32();
            if (!tilesets.TryAdd(uid, def))
                throw new ArgumentException("Duplicate LDtk tileset UID " + uid);
        }

        var layers = new List<LdtkVisualLayer>();
        foreach (JsonElement instance in level.GetProperty("layerInstances").EnumerateArray())
        {
            string name = instance.GetProperty("__identifier").GetString() ?? "";
            string type = instance.GetProperty("__type").GetString() ?? "";
            if (type is not ("Tiles" or "AutoLayer" or "IntGrid"))
                continue;

            JsonElement gridTiles = instance.GetProperty("gridTiles");
            JsonElement autoTiles = instance.GetProperty("autoLayerTiles");
            int count = (type == "Tiles" ? gridTiles.GetArrayLength() : autoTiles.GetArrayLength());
            if (count == 0) continue;
            LdtkVisualPlane plane = Classify(name);
            int layerUid = instance.GetProperty("layerDefUid").GetInt32();
            if (!layerDefs.TryGetValue(layerUid, out JsonElement def))
                throw new ArgumentException("Undefined LDtk layer UID " + layerUid);
            if (def.GetProperty("identifier").GetString() != name)
                throw new ArgumentException("LDtk layer name/UID mismatch: " + name);

            float px = def.GetProperty("parallaxFactorX").GetSingle();
            float py = def.GetProperty("parallaxFactorY").GetSingle();
            if (!float.IsFinite(px) || !float.IsFinite(py) ||
                px < -1 || px > 1 || py < -1 || py > 1)
                throw new ArgumentException("Layer " + name + " has invalid parallax factors (range -1..1).");
            float opacity = instance.GetProperty("__opacity").GetSingle();
            if (!float.IsFinite(opacity) || opacity < 0 || opacity > 1)
                throw new ArgumentException("Layer " + name + " has invalid opacity.");
            if (instance.TryGetProperty("visible", out JsonElement visible) &&
                visible.ValueKind == JsonValueKind.False)
                continue;

            JsonElement tilesetUid = instance.GetProperty("__tilesetDefUid");
            if (tilesetUid.ValueKind != JsonValueKind.Number ||
                !tilesets.TryGetValue(tilesetUid.GetInt32(), out JsonElement tileset))
                throw new ArgumentException("Layer " + name + " has tiles but no valid tileset.");
            string? path = instance.GetProperty("__tilesetRelPath").GetString() ??
                tileset.GetProperty("relPath").GetString();
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Layer " + name + " has no tileset image path.");
            ValidateRelativePath(path);
            int atlasWidth = tileset.GetProperty("pxWid").GetInt32();
            int atlasHeight = tileset.GetProperty("pxHei").GetInt32();
            int tileSize = tileset.GetProperty("tileGridSize").GetInt32();
            if (atlasWidth <= 0 || atlasHeight <= 0 || tileSize <= 0 ||
                atlasWidth > 16384 || atlasHeight > 16384)
                throw new ArgumentException("Invalid tileset dimensions on " + name);
            int offsetX = instance.GetProperty("__pxTotalOffsetX").GetInt32();
            int offsetY = instance.GetProperty("__pxTotalOffsetY").GetInt32();
            var tiles = new List<LdtkVisualTile>(count);
            JsonElement instances = type == "Tiles" ? gridTiles : autoTiles;
            foreach (JsonElement tile in instances.EnumerateArray())
            {
                JsonElement pos = tile.GetProperty("px");
                JsonElement src = tile.GetProperty("src");
                int x = pos[0].GetInt32(), y = pos[1].GetInt32();
                int sourceX = src[0].GetInt32(), sourceY = src[1].GetInt32();
                int flip = tile.GetProperty("f").GetInt32();
                float alpha = tile.TryGetProperty("a", out JsonElement a) ? a.GetSingle() : 1f;
                if (!float.IsFinite(alpha) || alpha < 0 || alpha > 1 || flip is < 0 or > 3)
                    throw new ArgumentException("Layer " + name + " has invalid tile alpha/flip bits.");
                // Decorative sprites may deliberately overhang the level, but
                // must have bounded positions and valid image source rectangles.
                if (x < -levelWidth * 2 || y < -levelHeight * 2 ||
                    x > levelWidth * 3 || y > levelHeight * 3 ||
                    sourceX < 0 || sourceY < 0 ||
                    sourceX + tileSize > atlasWidth || sourceY + tileSize > atlasHeight)
                    throw new ArgumentException("Layer " + name + " contains out-of-bounds tile coordinates.");
                tiles.Add(new LdtkVisualTile(x, y, sourceX, sourceY,
                    tileSize, tileSize, flip, alpha));
            }
            layers.Add(new LdtkVisualLayer(name, plane, path,
                atlasWidth, atlasHeight, px, py,
                def.GetProperty("parallaxScaling").GetBoolean(),
                offsetX, offsetY, opacity, tiles.AsReadOnly()));
        }
        // LDtk stores layer instances topmost first. SpriteBatch draws the
        // reverse order so lower layers are under upper layers.
        layers.Reverse();
        return new LdtkVisualDefinition(id, layers.AsReadOnly());
    }

    private static LdtkVisualPlane Classify(string name)
    {
        if (name.StartsWith("BG_", StringComparison.Ordinal)) return LdtkVisualPlane.Background;
        if (name.StartsWith("FG_", StringComparison.Ordinal)) return LdtkVisualPlane.Foreground;
        if (name is "Terrain_Art" or "Collisions") return LdtkVisualPlane.Terrain;
        throw new NotSupportedException("Unsupported visual layer '" + name +
            "'. Use BG_*, Terrain_Art, or FG_*.");
    }

    public static void ValidateRelativePath(string path)
    {
        if (Path.IsPathRooted(path) || path.Contains(':') ||
            !string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase) ||
            path.Split('/', '\\').Any(part => part is ".." or ""))
            throw new ArgumentException("Tileset must be a safe project-relative PNG path: " + path);
    }
}

/// <summary>
/// Pure camera transform, testable without MonoGame. Without scale, sprites
/// keep their authored dimensions; with scale, layers zoom about camera center.
/// LDtk factor 0 = ordinary camera, negative = slower background,
/// positive = faster foreground.
/// </summary>
public static class LdtkVisualTransform
{
    public static (double X, double Y, double Width, double Height) Project(
        LdtkVisualLayer layer, LdtkVisualTile tile,
        double cameraLeftPx, double cameraTopPx,
        int viewportWidth, int viewportHeight)
    {
        double fx = 1.0 + layer.ScrollFactorX;
        double fy = 1.0 + layer.ScrollFactorY;
        double x = tile.PixelX + layer.OffsetX;
        double y = tile.PixelY + layer.OffsetY;
        if (layer.ScaleWithParallax)
            return (viewportWidth / 2.0 + (x - cameraLeftPx - viewportWidth / 2.0) * fx,
                viewportHeight / 2.0 + (y - cameraTopPx - viewportHeight / 2.0) * fy,
                tile.WidthPx * fx, tile.HeightPx * fy);
        return (x - cameraLeftPx * fx, y - cameraTopPx * fy,
            tile.WidthPx, tile.HeightPx);
    }
}
