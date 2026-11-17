using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Gameplay.Content;

/// <summary>A validated LDtk gameplay region, consumed only through committed world events.</summary>
public sealed record LdtkWindRegion(int TriggerId, double WalkingSpeedFraction);
public sealed record LdtkExitLink(int TriggerId, SceneDestination Destination);
public sealed record LdtkCheckpointLink(int TriggerId, string SpawnId);

public sealed class LdtkConnectedLevel
{
    public LdtkConnectedLevel(LdtkImportedLevel imported,
        IReadOnlyDictionary<string, string> entrances,
        IReadOnlyDictionary<int, LdtkExitLink> exits,
        IReadOnlyDictionary<int, LdtkCheckpointLink> checkpoints,
        IReadOnlyDictionary<int, LdtkWindRegion> windRegions)
    {
        Imported = imported;
        Entrances = entrances;
        Exits = exits;
        Checkpoints = checkpoints;
        WindRegions = windRegions;
    }
    public LdtkImportedLevel Imported { get; }
    public string LevelId => Imported.World.LevelId;
    public IReadOnlyDictionary<string, string> Entrances { get; }
    public IReadOnlyDictionary<int, LdtkExitLink> Exits { get; }
    public IReadOnlyDictionary<int, LdtkCheckpointLink> Checkpoints { get; }
    public IReadOnlyDictionary<int, LdtkWindRegion> WindRegions { get; }
    public bool TrySpawnForEntrance(string entranceId, out string spawnId) =>
        Entrances.TryGetValue(entranceId, out spawnId!);
}

/// <summary>
/// Imports all embedded levels from one LDtk project. This creates WorldDefinition
/// objects and route metadata; it does NOT update world/player state, load textures,
/// or perform transitions. Every exit target is validated before a scene is replaced.
/// </summary>
public sealed class LdtkConnectedWorldCatalog
{
    private readonly IReadOnlyDictionary<string, LdtkConnectedLevel> _levels;
    private LdtkConnectedWorldCatalog(Dictionary<string, LdtkConnectedLevel> levels) =>
        _levels = new ReadOnlyDictionary<string, LdtkConnectedLevel>(levels);

    public IReadOnlyDictionary<string, LdtkConnectedLevel> Levels => _levels;
    public bool Supports(SceneDestination destination) => destination is not null &&
        _levels.TryGetValue(destination.LevelId, out LdtkConnectedLevel? level) &&
        level.Entrances.ContainsKey(destination.EntranceId);
    public LdtkConnectedLevel Require(SceneDestination destination) =>
        Supports(destination) ? _levels[destination.LevelId] :
        throw new ArgumentException("Unknown LDtk level/entrance: " +
            destination.LevelId + "/" + destination.EntranceId, nameof(destination));

    public static LdtkConnectedWorldCatalog FromFile(string filename) =>
        FromJson(File.ReadAllText(filename));

    public static LdtkConnectedWorldCatalog FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new ArgumentException("An LDtk project is required.", nameof(json));
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.GetProperty("externalLevels").GetBoolean() ||
            root.GetProperty("simplifiedExport").GetBoolean())
            throw new NotSupportedException("Use embedded levels and normal LDtk JSON export.");
        var levels = new Dictionary<string, LdtkConnectedLevel>(StringComparer.Ordinal);
        foreach (JsonElement levelNode in root.GetProperty("levels").EnumerateArray())
        {
            string levelId = levelNode.GetProperty("identifier").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(levelId) || levels.ContainsKey(levelId))
                throw new ArgumentException("Missing/duplicate LDtk level identifier: " + levelId);
            LdtkImportedLevel imported = LdtkLevelImporter.FromJson(json, levelId);
            var spawns = new List<WorldSpawnPoint>(imported.World.SpawnPoints);
            var platforms = new List<MovingPlatformTrajectory>();
            var triggers = new List<WorldTriggerDefinition>();
            var entrances = new Dictionary<string, string>(StringComparer.Ordinal)
                { [GameLevelIds.DefaultEntrance] = imported.StartSpawnId };
            var exits = new Dictionary<int, LdtkExitLink>();
            var checkpoints = new Dictionary<int, LdtkCheckpointLink>();
            var winds = new Dictionary<int, LdtkWindRegion>();
            var instanceIds = new HashSet<string>(StringComparer.Ordinal);
            var triggerIds = new HashSet<int>();
            foreach (JsonElement layer in levelNode.GetProperty("layerInstances").EnumerateArray())
            {
                if (layer.GetProperty("__type").GetString() != "Entities") continue;
                string layerId = layer.GetProperty("__identifier").GetString() ?? "";
                if (layerId != LdtkLevelImporter.EntityLayer) continue;
                if (layer.GetProperty("__pxTotalOffsetX").GetInt32() != 0 ||
                    layer.GetProperty("__pxTotalOffsetY").GetInt32() != 0)
                    throw new ArgumentException("Gameplay entity layer offsets must be zero.");
                foreach (JsonElement entity in layer.GetProperty("entityInstances").EnumerateArray())
                {
                    string kind = entity.GetProperty("__identifier").GetString() ?? "";
                    if (kind is not ("LevelEntrance" or "LevelExit" or "Checkpoint" or
                        "WindZone" or "MovingPlatform")) continue;
                    string iid = entity.GetProperty("iid").GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(iid) || !instanceIds.Add(iid))
                        throw new ArgumentException("Missing or duplicate gameplay entity IID.");
                    var fields = ReadFields(entity);
                    int x = entity.GetProperty("px")[0].GetInt32();
                    int y = entity.GetProperty("px")[1].GetInt32();
                    int w = entity.GetProperty("width").GetInt32();
                    int h = entity.GetProperty("height").GetInt32();
                    if (w <= 0 || h <= 0 || w > 8192 || h > 8192 ||
                        x < 0 || y < 0 || x + w > imported.WidthPx || y + h > imported.HeightPx)
                        throw new ArgumentException(kind + " must have valid in-level bounds: " + iid);
                    Aabb bounds = Box(x, y, w, h, imported.HeightPx);
                    switch (kind)
                    {
                        case "LevelEntrance":
                        {
                            string name = String(fields, "EntranceId");
                            if (name == GameLevelIds.DefaultEntrance || entrances.ContainsKey(name))
                                throw new ArgumentException("Duplicate/reserved entrance: " + name);
                            spawns.Add(Spawn(name, x, y, w, h, imported.HeightPx));
                            entrances.Add(name, name);
                            break;
                        }
                        case "LevelExit":
                        {
                            int id = PositiveId(fields, "TriggerId", triggerIds);
                            string targetLevel = String(fields, "TargetLevel");
                            string targetEntrance = String(fields, "TargetEntrance");
                            triggers.Add(new WorldTriggerDefinition(id, "LDtkExit", bounds));
                            exits.Add(id, new LdtkExitLink(id,
                                new SceneDestination(targetLevel, targetEntrance)));
                            break;
                        }
                        case "Checkpoint":
                        {
                            int id = PositiveId(fields, "TriggerId", triggerIds);
                            string spawnId = String(fields, "SpawnId");
                            spawns.Add(Spawn(spawnId, x, y, w, h, imported.HeightPx));
                            triggers.Add(new WorldTriggerDefinition(id, "LDtkCheckpoint", bounds));
                            checkpoints.Add(id, new LdtkCheckpointLink(id, spawnId));
                            break;
                        }
                        case "WindZone":
                        {
                            int id = PositiveId(fields, "TriggerId", triggerIds);
                            double percent = Number(fields, "SpeedPercent", -150, 150);
                            triggers.Add(new WorldTriggerDefinition(id, "LDtkWind", bounds));
                            winds.Add(id, new LdtkWindRegion(id, percent / 100.0));
                            break;
                        }
                        case "MovingPlatform":
                        {
                            int id = PositiveId(fields, "ColliderId", null);
                            double speedX = Number(fields, "VelocityX", -50, 50);
                            double speedY = Number(fields, "VelocityY", -50, 50);
                            double travelSeconds = Number(fields, "TravelSeconds", 0.01, 60);
                            platforms.Add(new MovingPlatformTrajectory(id, bounds,
                                new Vector2D(speedX, speedY), travelSeconds));
                            break;
                        }
                    }
                }
            }
            WorldDefinition world = new(levelId, imported.World.StaticColliders,
                platforms, spawns, triggers);
            var resolved = imported with { World = world };
            levels.Add(levelId, new LdtkConnectedLevel(resolved,
                new ReadOnlyDictionary<string, string>(entrances),
                new ReadOnlyDictionary<int, LdtkExitLink>(exits),
                new ReadOnlyDictionary<int, LdtkCheckpointLink>(checkpoints),
                new ReadOnlyDictionary<int, LdtkWindRegion>(winds)));
        }
        if (levels.Count == 0) throw new ArgumentException("LDtk project must contain levels.");
        var catalog = new LdtkConnectedWorldCatalog(levels);
        foreach (LdtkConnectedLevel level in levels.Values)
            foreach (LdtkExitLink exit in level.Exits.Values)
                if (!catalog.Supports(exit.Destination))
                    throw new ArgumentException("Broken LDtk exit " + level.LevelId +
                        "/" + exit.TriggerId + " => " + exit.Destination.LevelId + "/" +
                        exit.Destination.EntranceId);
        return catalog;
    }

    private static int PositiveId(Dictionary<string, JsonElement> fields, string key, HashSet<int>? ids)
    {
        if (!fields.TryGetValue(key, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int id) ||
            id <= 0 || ids is not null && !ids.Add(id))
            throw new ArgumentException("Missing, duplicate, or invalid ID field: " + key);
        return id;
    }

    private static Dictionary<string, JsonElement> ReadFields(JsonElement entity)
    {
        var result = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement field in entity.GetProperty("fieldInstances").EnumerateArray())
        {
            string name = field.GetProperty("__identifier").GetString() ?? "";
            if (!result.TryAdd(name, field.GetProperty("__value")))
                throw new ArgumentException("Duplicate gameplay field: " + name);
        }
        return result;
    }
    private static string String(Dictionary<string, JsonElement> fields, string key)
    {
        if (!fields.TryGetValue(key, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()) ||
            value.GetString() != value.GetString()!.Trim())
            throw new ArgumentException("Missing or invalid string field: " + key);
        return value.GetString()!;
    }
    private static double Number(Dictionary<string, JsonElement> fields,
        string key, double min, double max)
    {
        if (!fields.TryGetValue(key, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number)
            throw new ArgumentException("Missing numeric field: " + key);
        double number = value.GetDouble();
        if (!double.IsFinite(number) || number < min || number > max)
            throw new ArgumentException("Invalid numeric field: " + key);
        return number;
    }
    private static Aabb Box(int x, int y, int width, int height, int levelHeight) =>
        new(new Vector2D(x / LdtkLevelImporter.PixelsPerWorldUnit,
                (levelHeight - y - height) / LdtkLevelImporter.PixelsPerWorldUnit),
            new Vector2D((x + width) / LdtkLevelImporter.PixelsPerWorldUnit,
                (levelHeight - y) / LdtkLevelImporter.PixelsPerWorldUnit));
    private static WorldSpawnPoint Spawn(string name, int x, int y, int w, int h, int levelHeight)
    {
        double width = LdtkLevelImporter.PlayerWidthWorld;
        double height = LdtkLevelImporter.PlayerHeightWorld;
        double left = (x + (w - width * LdtkLevelImporter.PixelsPerWorldUnit) / 2.0) /
            LdtkLevelImporter.PixelsPerWorldUnit;
        double bottom = (levelHeight - y - h) / LdtkLevelImporter.PixelsPerWorldUnit;
        return new WorldSpawnPoint(name,
            new Aabb(new Vector2D(left, bottom), new Vector2D(left + width, bottom + height)));
    }
}
