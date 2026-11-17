using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Collision.Detection;

namespace PlatformerSandbox.Gameplay.Content;

/// <summary>
/// Validated, presentation-independent import of the small LDtk contract used
/// by our first authored level. The LDtk library is intentionally NOT needed
/// in the headless gameplay assembly. Visual tiles/parallax are a later pass.
/// </summary>
public sealed record LdtkImportedLevel(
    WorldDefinition World,
    int WidthPx,
    int HeightPx,
    int GridSizePx,
    int SolidCellCount,
    string StartSpawnId)
{
    public int ColliderCount => World.StaticColliders.Count;
    public int ColumnCount => WidthPx / GridSizePx;
    public int RowCount => HeightPx / GridSizePx;
}

public static class LdtkLevelImporter
{
    public const string CollisionLayer = "Collisions";
    public const string EntityLayer = "Entities";
    public const string SpawnEntity = "PlayerSpawn";
    public const string StartSpawn = "START";
    public const double PixelsPerWorldUnit = 32.0;
    public const double PlayerWidthWorld = 0.8;
    public const double PlayerHeightWorld = 1.6;
    private const int ColliderIdBase = 10000;

    public static LdtkImportedLevel FromFile(string filename,
        string? levelIdentifier = null)
    {
        if (string.IsNullOrWhiteSpace(filename))
            throw new ArgumentException("An LDtk filename is required.", nameof(filename));
        return FromJson(File.ReadAllText(filename), levelIdentifier);
    }

    public static LdtkImportedLevel FromJson(string json,
        string? levelIdentifier = null)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("LDtk JSON is required.", nameof(json));
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            root.GetProperty("externalLevels").GetBoolean() ||
            root.GetProperty("simplifiedExport").GetBoolean())
            throw new NotSupportedException("Import a complete embedded .ldtk project (external/simplified export disabled).");

        JsonElement levels = root.GetProperty("levels");
        if (levels.ValueKind != JsonValueKind.Array || levels.GetArrayLength() == 0)
            throw new ArgumentException("LDtk project has no levels.", nameof(json));
        JsonElement level = default;
        bool found = false;
        foreach (JsonElement candidate in levels.EnumerateArray())
        {
            if (levelIdentifier is null ||
                candidate.GetProperty("identifier").GetString() == levelIdentifier)
            {
                level = candidate;
                found = true;
                break;
            }
        }
        if (!found)
            throw new ArgumentException("Unknown LDtk level identifier: " + levelIdentifier,
                nameof(levelIdentifier));
        string id = level.GetProperty("identifier").GetString() ?? "";
        int width = level.GetProperty("pxWid").GetInt32();
        int height = level.GetProperty("pxHei").GetInt32();
        if (width <= 0 || height <= 0 || width > 131072 || height > 131072)
            throw new ArgumentException("Invalid LDtk level dimensions.", nameof(json));

        JsonElement layers = level.GetProperty("layerInstances");
        if (layers.ValueKind != JsonValueKind.Array)
            throw new NotSupportedException("This phase needs embedded LDtk layerInstances.");
        JsonElement collisionLayer = default;
        JsonElement entityLayer = default;
        int collisionMatches = 0, entityMatches = 0;
        foreach (JsonElement layer in layers.EnumerateArray())
        {
            string? layerId = layer.GetProperty("__identifier").GetString();
            if (layerId == CollisionLayer)
            {
                if (layer.GetProperty("__type").GetString() != "IntGrid")
                    throw new ArgumentException("Collisions must be an IntGrid layer.");
                collisionLayer = layer;
                collisionMatches++;
            }
            if (layerId == EntityLayer)
            {
                if (layer.GetProperty("__type").GetString() != "Entities")
                    throw new ArgumentException("Entities must be an Entities layer.");
                entityLayer = layer;
                entityMatches++;
            }
        }
        if (collisionMatches != 1 || entityMatches != 1)
            throw new ArgumentException("Exactly one Collisions and one Entities layer are required.");
        ValidateOffset(collisionLayer, CollisionLayer);
        ValidateOffset(entityLayer, EntityLayer);
        int grid = collisionLayer.GetProperty("__gridSize").GetInt32();
        int columns = collisionLayer.GetProperty("__cWid").GetInt32();
        int rows = collisionLayer.GetProperty("__cHei").GetInt32();
        if (grid <= 0 || width % grid != 0 || height % grid != 0 ||
            columns != width / grid || rows != height / grid ||
            (long)columns * rows > 1048576)
            throw new ArgumentException("Collision grid dimensions do not match level pixel dimensions.");
        if (grid != (int)PixelsPerWorldUnit)
            throw new NotSupportedException("This first importer uses a 32-pixel grid / world-unit convention.");

        JsonElement cells = collisionLayer.GetProperty("intGridCsv");
        if (cells.ValueKind != JsonValueKind.Array || cells.GetArrayLength() != columns * rows)
            throw new ArgumentException("Collision IntGrid length does not match its dimensions.");
        bool[,] solid = new bool[rows, columns];
        int occupied = 0, index = 0;
        foreach (JsonElement cell in cells.EnumerateArray())
        {
            int value = cell.GetInt32();
            if (value is not (0 or 1))
                throw new NotSupportedException("Only IntGrid 0=empty and 1=Solid are supported in Phase 1.");
            int row = index / columns, col = index % columns;
            solid[row, col] = value == 1;
            if (value == 1) occupied++;
            index++;
        }
        if (occupied == 0)
            throw new ArgumentException("The authored level needs solid collision cells.");

        // Deterministic rectangle merging avoids hundreds of tiny adjacent
        // contact edges. Every rectangle gets a stable ID from its first cell.
        // Changing unrelated cells doesn't renumber an existing rectangle.
        var rectangles = BuildRectangles(solid, columns, rows);
        var colliders = rectangles.Select(rect => new StaticAabbCollider(
            ColliderIdBase + rect.Row * columns + rect.Column + 1,
            Box(rect.Column * grid / PixelsPerWorldUnit,
                (height - (rect.Row + rect.Height) * grid) / PixelsPerWorldUnit,
                (rect.Column + rect.Width) * grid / PixelsPerWorldUnit,
                (height - rect.Row * grid) / PixelsPerWorldUnit))).ToArray();

        JsonElement entities = entityLayer.GetProperty("entityInstances");
        if (entities.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Entity instances must be an array.");
        WorldSpawnPoint? spawn = null;
        foreach (JsonElement entity in entities.EnumerateArray())
        {
            if (entity.GetProperty("__identifier").GetString() != SpawnEntity)
                continue;
            if (spawn is not null)
                throw new NotSupportedException("Phase 1 requires exactly one PlayerSpawn entity.");
            JsonElement px = entity.GetProperty("px");
            JsonElement pivot = entity.GetProperty("__pivot");
            if (pivot[0].GetDouble() != 0.0 || pivot[1].GetDouble() != 0.0)
                throw new NotSupportedException("Set PlayerSpawn pivot to top-left (0,0) in LDtk.");
            int markerWidth = entity.GetProperty("width").GetInt32();
            int markerHeight = entity.GetProperty("height").GetInt32();
            int markerX = px[0].GetInt32();
            int markerY = px[1].GetInt32();
            if (markerWidth <= 0 || markerHeight <= 0 || markerX < 0 || markerY < 0 ||
                markerX + markerWidth > width || markerY + markerHeight > height)
                throw new ArgumentException("PlayerSpawn marker is outside the level or has invalid dimensions.");
            // The LDtk entity is an authoring marker; our existing motor is
            // 0.8x1.6 world units, not the marker's 32x32 pixel rectangle.
            double left = (markerX + (markerWidth - PlayerWidthWorld * PixelsPerWorldUnit) / 2)
                / PixelsPerWorldUnit;
            double feet = (height - markerY - markerHeight) / PixelsPerWorldUnit;
            spawn = new WorldSpawnPoint(StartSpawn, Box(left, feet,
                left + PlayerWidthWorld, feet + PlayerHeightWorld));
        }
        if (spawn is null)
            throw new ArgumentException("Place one PlayerSpawn entity in the Entities layer.");

        // WorldDefinition validates collider IDs, spawns and intersections.
        WorldDefinition world = new(id, colliders,
            Array.Empty<PlatformerSandbox.Simulation.MovingPlatforms.MovingPlatformTrajectory>(),
            new[] { spawn });
        return new LdtkImportedLevel(world, width, height, grid, occupied, StartSpawn);
    }

    private static void ValidateOffset(JsonElement layer, string layerName)
    {
        // Cell positions and spawn positions are relative to their layer.
        // Reject visual offsets instead of silently shifting physics apart.
        if (layer.GetProperty("__pxTotalOffsetX").GetInt32() != 0 ||
            layer.GetProperty("__pxTotalOffsetY").GetInt32() != 0)
            throw new NotSupportedException(layerName + " must use zero pixel offsets in Phase 1.");
    }

    private readonly record struct CellRectangle(int Column, int Row, int Width, int Height);

    private static List<CellRectangle> BuildRectangles(bool[,] solid, int columns, int rows)
    {
        // A vertical extension occurs ONLY for identical horizontal runs.
        // Otherwise finalize the previous rectangle (no partial overlaps).
        var active = new Dictionary<(int Start, int Width), CellRectangle>();
        var result = new List<CellRectangle>();
        for (int row = 0; row < rows; row++)
        {
            var next = new Dictionary<(int Start, int Width), CellRectangle>();
            for (int col = 0; col < columns;)
            {
                if (!solid[row, col]) { col++; continue; }
                int start = col;
                while (col < columns && solid[row, col]) col++;
                int width = col - start;
                var key = (start, width);
                next[key] = active.TryGetValue(key, out CellRectangle old)
                    ? old with { Height = old.Height + 1 }
                    : new CellRectangle(start, row, width, 1);
            }
            foreach (var previous in active)
                if (!next.ContainsKey(previous.Key)) result.Add(previous.Value);
            active = next;
        }
        result.AddRange(active.Values);
        result.Sort((a, b) => (a.Row * columns + a.Column).CompareTo(b.Row * columns + b.Column));
        return result;
    }

    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));
}
