using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Foundation.Presentation.Lighting;

namespace PlatformerSandbox.Gameplay.Content;

/// <summary>
/// An editor-authored level exposed to the game. LevelId is the public scene ID;
/// Identifier is the actual LDtk identifier. Only the four original single-level
/// demos use compatibility aliases, so existing shortcuts and tests keep working.
/// </summary>
public sealed record LdtkContentEntry(string LevelId, string Identifier,
    string FilePath, IReadOnlyCollection<string> Entrances);

/// <summary>
/// Prepared using ONE file snapshot: collision, gameplay entities, visuals and
/// independent artwork must all agree before the host can commit a scene.
/// </summary>
public sealed record LdtkPreparedContent(string FilePath, LdtkImportedLevel Imported,
    LdtkConnectedLevel Connected, LdtkVisualDefinition Visuals,
    LdtkSceneryDefinition Scenery, LdtkTerrainArtworkDefinition Terrain,
    LdtkParticleSceneDefinition? ParticleZones = null,
    LdtkLightingScene? Lighting = null);

/// <summary>
/// Headless discovery and validation of every embedded level in Content/Levels.
/// No MonoGame references, hardcoded per-level factory branches, or disk reads
/// during simulation. Invalid projects are reported and excluded independently
/// so a broken work-in-progress project does not prevent other levels starting.
/// </summary>
public sealed class LdtkContentCatalog
{
    private readonly IReadOnlyDictionary<string, LdtkContentEntry> _entries;
    private readonly IReadOnlyDictionary<string, string> _rejected;
    private readonly LdtkEntityImportRegistry _entityHandlers;
    private readonly SceneLightingLayerCatalog _layerSettings;
    private LdtkContentCatalog(Dictionary<string, LdtkContentEntry> entries,
        Dictionary<string, string> rejected, LdtkEntityImportRegistry entityHandlers,
        SceneLightingLayerCatalog layerSettings)
    {
        _entityHandlers = entityHandlers;
        _layerSettings = layerSettings;
        _entries = new ReadOnlyDictionary<string, LdtkContentEntry>(entries);
        _rejected = new ReadOnlyDictionary<string, string>(rejected);
    }

    public IReadOnlyDictionary<string, LdtkContentEntry> Levels => _entries;
    public IReadOnlyDictionary<string, string> RejectedProjects => _rejected;
    public bool Supports(SceneDestination destination) => destination is not null &&
        _entries.TryGetValue(destination.LevelId, out LdtkContentEntry? entry) &&
        entry.Entrances.Contains(destination.EntranceId, StringComparer.Ordinal);

    public static LdtkContentCatalog Discover(string contentLevelsDirectory,
        LdtkEntityImportRegistry? entityHandlers = null,
        SceneLightingLayerCatalog? layerSettings = null)
    {
        entityHandlers ??= LdtkEntityImportRegistry.CreateDefault();
        layerSettings ??= SceneLightingLayerCatalog.Standard();
        if (string.IsNullOrWhiteSpace(contentLevelsDirectory))
            throw new ArgumentException("Content/Levels directory is required.", nameof(contentLevelsDirectory));
        string directory = Path.GetFullPath(contentLevelsDirectory);
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException("LDtk level directory not found: " + directory);

        var entries = new Dictionary<string, LdtkContentEntry>(StringComparer.Ordinal);
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string file in Directory.EnumerateFiles(directory, "*.ldtk", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal))
        {
            try
            {
                // Validate the complete project and all of its exits before making
                // any of its destinations available to the level-select menu.
                string json = File.ReadAllText(file);
                var connected = LdtkConnectedWorldCatalog.FromJson(json, entityHandlers);
                var staged = new List<LdtkContentEntry>();
                foreach (LdtkConnectedLevel level in connected.Levels.Values)
                {
                    string id = AliasFor(Path.GetFileName(file), level.LevelId);
                    if (IsReservedId(id) || entries.ContainsKey(id) ||
                        staged.Any(other => other.LevelId == id))
                        throw new ArgumentException("Conflicting or reserved level ID: " + id);
                    // Validate each independently authored visual layer and free
                    // sprite before advertising this level as selectable.
                    LdtkVisualDefinition visuals = LdtkVisualImporter.FromJson(json, level.LevelId);
                    LdtkSceneryDefinition scenery = LdtkSceneryImporter.FromJson(json, level.LevelId);
                    LdtkTerrainArtworkDefinition terrain = LdtkTerrainArtworkImporter.FromJson(json, level.LevelId);
                    LdtkParticleZoneImporter.FromJson(json, level.LevelId);
                    LdtkLightingImporter.FromJson(json, level.LevelId,
                        layerSettings.ForScene(level.LevelId));
                    if (visuals.LevelId != level.LevelId || scenery.LevelId != level.LevelId ||
                        terrain.LevelId != level.LevelId)
                        throw new ArgumentException("Authored presentation IDs differ from world: " + level.LevelId);
                    staged.Add(new LdtkContentEntry(id, level.LevelId, file,
                        Array.AsReadOnly(level.Entrances.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray())));
                }
                foreach (LdtkContentEntry entry in staged) entries.Add(entry.LevelId, entry);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                JsonException or ArgumentException or NotSupportedException or KeyNotFoundException or
                InvalidOperationException)
            {
                errors[Path.GetFileName(file)] = ex.Message;
            }
        }
        return new LdtkContentCatalog(entries, errors, entityHandlers, layerSettings);
    }

    /// <summary>Freshly reload and revalidate selected content at the safe scene-preparation boundary.</summary>
    public LdtkPreparedContent Prepare(SceneDestination destination)
    {
        if (!Supports(destination))
            throw new ArgumentException("Unknown LDtk level or entrance: " +
                destination?.LevelId + "/" + destination?.EntranceId, nameof(destination));
        LdtkContentEntry entry = _entries[destination.LevelId];
        string json = File.ReadAllText(entry.FilePath);
        LdtkConnectedWorldCatalog linked = LdtkConnectedWorldCatalog.FromJson(json, _entityHandlers);
        LdtkConnectedLevel level = linked.Require(new SceneDestination(entry.Identifier,
            destination.EntranceId));
        LdtkImportedLevel imported = level.Imported with
            { StartSpawnId = level.Entrances[destination.EntranceId] };
        LdtkVisualDefinition visual = LdtkVisualImporter.FromJson(json, entry.Identifier);
        LdtkSceneryDefinition scenery = LdtkSceneryImporter.FromJson(json, entry.Identifier);
        LdtkTerrainArtworkDefinition terrain = LdtkTerrainArtworkImporter.FromJson(json, entry.Identifier);
        LdtkParticleSceneDefinition particles = LdtkParticleZoneImporter.FromJson(json, entry.Identifier);
        LdtkLightingScene lighting = LdtkLightingImporter.FromJson(json, entry.Identifier,
            _layerSettings.ForScene(entry.Identifier));
        if (visual.LevelId != imported.World.LevelId || scenery.LevelId != imported.World.LevelId ||
            terrain.LevelId != imported.World.LevelId)
            throw new InvalidOperationException("LDtk gameplay and visual level identifiers differ.");
        return new LdtkPreparedContent(entry.FilePath, imported, level, visual, scenery, terrain, particles, lighting);
    }

    private static string AliasFor(string filename, string identifier) =>
        (filename, identifier) switch
        {
            ("FirstAttempt.ldtk", "Level_0") => GameLevelIds.LdtkTest,
            ("ParallaxDemo.ldtk", "PARALLAX_DEMO") => GameLevelIds.LdtkParallaxDemo,
            ("SceneryDemo.ldtk", "SCENERY_DEMO") => GameLevelIds.LdtkSceneryDemo,
            ("TerrainDemo.ldtk", "TERRAIN_DEMO") => GameLevelIds.LdtkTerrainDemo,
            _ => identifier
        };

    private static bool IsReservedId(string id) => id is
        GameLevelIds.MovementGym or GameLevelIds.SharedWorld or
        GameLevelIds.AuthoredWorld or GameLevelIds.MotionLab or
        GameLevelIds.RouteA or GameLevelIds.RouteB;
}
