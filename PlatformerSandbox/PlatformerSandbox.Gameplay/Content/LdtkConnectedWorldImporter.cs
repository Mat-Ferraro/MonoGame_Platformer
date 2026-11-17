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

    public static LdtkConnectedWorldCatalog FromJson(string json) =>
        FromJson(json, LdtkEntityImportRegistry.CreateDefault());

    public static LdtkConnectedWorldCatalog FromJson(string json,
        LdtkEntityImportRegistry entityHandlers)
    {
        ArgumentNullException.ThrowIfNull(entityHandlers);
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
            var context = new LdtkEntityImportContext(imported);
            var instanceIds = new HashSet<string>(StringComparer.Ordinal);
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
                    string iid = entity.GetProperty("iid").GetString() ?? "";
                    if (string.IsNullOrWhiteSpace(iid) || !instanceIds.Add(iid))
                        throw new ArgumentException("Missing or duplicate entity IID in " + levelId);
                    string kind = entity.GetProperty("__identifier").GetString() ?? "";
                    entityHandlers.Import(kind, entity, imported.WidthPx,
                        imported.HeightPx, context);
                }
            }
            WorldDefinition world = new(levelId, imported.World.StaticColliders,
                context.Platforms, context.Spawns, context.Triggers);
            var resolved = imported with { World = world };
            levels.Add(levelId, new LdtkConnectedLevel(resolved,
                new ReadOnlyDictionary<string, string>(context.Entrances),
                new ReadOnlyDictionary<int, LdtkExitLink>(context.Exits),
                new ReadOnlyDictionary<int, LdtkCheckpointLink>(context.Checkpoints),
                new ReadOnlyDictionary<int, LdtkWindRegion>(context.Winds)));
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

}
