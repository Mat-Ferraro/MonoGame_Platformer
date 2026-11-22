using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Gameplay.Content;

/// <summary>Strict, graphics-independent LDtk entity data made available to
/// individual gameplay entity handlers. Field names are case-sensitive.</summary>
public sealed class LdtkAuthoredEntity
{
    private readonly IReadOnlyDictionary<string, JsonElement> _fields;
    internal LdtkAuthoredEntity(JsonElement entity, int levelWidth, int levelHeight)
    {
        Kind = entity.GetProperty("__identifier").GetString() ?? "";
        InstanceId = entity.GetProperty("iid").GetString() ?? "";
        X = entity.GetProperty("px")[0].GetInt32();
        Y = entity.GetProperty("px")[1].GetInt32();
        Width = entity.GetProperty("width").GetInt32();
        Height = entity.GetProperty("height").GetInt32();
        if (Width <= 0 || Height <= 0 || Width > 8192 || Height > 8192 ||
            X < 0 || Y < 0 || X + Width > levelWidth || Y + Height > levelHeight)
            throw new ArgumentException(Kind + " must have valid in-level bounds: " + InstanceId);
        Bounds = new Aabb(new Vector2D(X / LdtkLevelImporter.PixelsPerWorldUnit,
                (levelHeight - Y - Height) / LdtkLevelImporter.PixelsPerWorldUnit),
            new Vector2D((X + Width) / LdtkLevelImporter.PixelsPerWorldUnit,
                (levelHeight - Y) / LdtkLevelImporter.PixelsPerWorldUnit));
        LevelHeight = levelHeight;
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement field in entity.GetProperty("fieldInstances").EnumerateArray())
        {
            string key = field.GetProperty("__identifier").GetString() ?? "";
            if (!fields.TryAdd(key, field.GetProperty("__value")))
                throw new ArgumentException("Duplicate LDtk field: " + key + " on " + InstanceId);
        }
        _fields = fields;
    }
    public string Kind { get; }
    public string InstanceId { get; }
    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public int LevelHeight { get; }
    public Aabb Bounds { get; }

    public string RequiredString(string key)
    {
        if (!_fields.TryGetValue(key, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()) ||
            value.GetString() != value.GetString()!.Trim())
            throw new ArgumentException("Missing or invalid string field: " + Kind + "." + key);
        return value.GetString()!;
    }
    public int PositiveId(string key)
    {
        if (!_fields.TryGetValue(key, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out int id) || id <= 0)
            throw new ArgumentException("Missing or invalid ID: " + Kind + "." + key);
        return id;
    }
    public double Number(string key, double min, double max)
    {
        if (!_fields.TryGetValue(key, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
            throw new ArgumentException("Missing numeric field: " + Kind + "." + key);
        double number = value.GetDouble();
        if (!double.IsFinite(number) || number < min || number > max)
            throw new ArgumentException("Out-of-range numeric field: " + Kind + "." + key);
        return number;
    }
    public WorldSpawnPoint Spawn(string id)
    {
        double width = LdtkLevelImporter.PlayerWidthWorld;
        double height = LdtkLevelImporter.PlayerHeightWorld;
        double left = (X + (Width - width * LdtkLevelImporter.PixelsPerWorldUnit) / 2.0) /
            LdtkLevelImporter.PixelsPerWorldUnit;
        double bottom = (LevelHeight - Y - Height) / LdtkLevelImporter.PixelsPerWorldUnit;
        return new WorldSpawnPoint(id,
            new Aabb(new Vector2D(left, bottom), new Vector2D(left + width, bottom + height)));
    }
}

/// <summary>Per-level staging context. Handlers describe data only; the final
/// WorldDefinition checks duplicate spawns and overlapping geometry.</summary>
public sealed class LdtkEntityImportContext
{
    internal LdtkEntityImportContext(LdtkImportedLevel imported)
    {
        Imported = imported;
        Spawns = new List<WorldSpawnPoint>(imported.World.SpawnPoints);
        Entrances.Add(GameLevelIds.DefaultEntrance, imported.StartSpawnId);
    }
    public LdtkImportedLevel Imported { get; }
    internal List<WorldSpawnPoint> Spawns { get; }
    internal List<MovingPlatformTrajectory> Platforms { get; } = new();
    internal List<WorldTriggerDefinition> Triggers { get; } = new();
    internal Dictionary<string, string> Entrances { get; } = new(StringComparer.Ordinal);
    internal Dictionary<int, LdtkExitLink> Exits { get; } = new();
    internal Dictionary<int, LdtkCheckpointLink> Checkpoints { get; } = new();
    internal Dictionary<int, LdtkWindRegion> Winds { get; } = new();
    private readonly HashSet<int> _triggerIds = new();

    public void AddEntrance(string entranceId, WorldSpawnPoint spawn)
    {
        if (entranceId == GameLevelIds.DefaultEntrance ||
            !Entrances.TryAdd(entranceId, spawn.Id))
            throw new ArgumentException("Duplicate/reserved entrance: " + entranceId);
        Spawns.Add(spawn);
    }
    public void AddExit(int triggerId, Aabb bounds, SceneDestination destination)
    {
        AddTrigger(triggerId, "LDtkExit", bounds);
        Exits.Add(triggerId, new LdtkExitLink(triggerId, destination));
    }
    public void AddCheckpoint(int triggerId, Aabb bounds, WorldSpawnPoint spawn)
    {
        AddTrigger(triggerId, "LDtkCheckpoint", bounds);
        Spawns.Add(spawn);
        Checkpoints.Add(triggerId, new LdtkCheckpointLink(triggerId, spawn.Id));
    }
    public void AddWind(int triggerId, Aabb bounds, double walkingSpeedFraction)
    {
        if (!double.IsFinite(walkingSpeedFraction) || walkingSpeedFraction < -1.5 || walkingSpeedFraction > 1.5)
            throw new ArgumentOutOfRangeException(nameof(walkingSpeedFraction));
        AddTrigger(triggerId, "LDtkWind", bounds);
        Winds.Add(triggerId, new LdtkWindRegion(triggerId, walkingSpeedFraction));
    }
    public void AddMovingPlatform(int colliderId, Aabb bounds, Vector2D velocity, double travelSeconds) =>
        Platforms.Add(new MovingPlatformTrajectory(colliderId, bounds, velocity, travelSeconds));
    private void AddTrigger(int id, string kind, Aabb bounds)
    {
        if (id <= 0 || !_triggerIds.Add(id))
            throw new ArgumentException("Missing or duplicate trigger ID: " + id);
        Triggers.Add(new WorldTriggerDefinition(id, kind, bounds));
    }
}

/// <summary>
/// Registry of typed gameplay entity importers. Add new authored entity kinds
/// by registering a handler rather than modifying the LDtk project scanner,
/// scene factory, or physics engine. Visual-only entities are handled by their
/// separate presentation importers and cannot create collision implicitly.
/// </summary>
public sealed class LdtkEntityImportRegistry
{
    private readonly Dictionary<string, Action<LdtkEntityImportContext, LdtkAuthoredEntity>> _handlers =
        new(StringComparer.Ordinal);
    private static readonly HashSet<string> VisualOrSpawnKinds = new(StringComparer.Ordinal)
        { "PlayerSpawn", "ScenerySprite", "PaintedSprite", "TerrainArtwork", "ParticleZone", "LightSource", "LightBlocker" };

    public void Register(string kind, Action<LdtkEntityImportContext, LdtkAuthoredEntity> handler)
    {
        if (string.IsNullOrWhiteSpace(kind) || VisualOrSpawnKinds.Contains(kind))
            throw new ArgumentException("Invalid or reserved gameplay entity type.", nameof(kind));
        ArgumentNullException.ThrowIfNull(handler);
        if (!_handlers.TryAdd(kind, handler))
            throw new ArgumentException("Duplicate gameplay entity handler: " + kind, nameof(kind));
    }
    internal void Import(string kind, JsonElement raw, int width, int height,
        LdtkEntityImportContext context)
    {
        if (VisualOrSpawnKinds.Contains(kind)) return;
        if (!_handlers.TryGetValue(kind, out var handler))
            throw new NotSupportedException("Unsupported LDtk entity '" + kind +
                "'. Register a gameplay handler or use a recognized visual entity.");
        handler(context, new LdtkAuthoredEntity(raw, width, height));
    }

    public static LdtkEntityImportRegistry CreateDefault()
    {
        var registry = new LdtkEntityImportRegistry();
        registry.Register("LevelEntrance", (c, e) =>
        {
            string id = e.RequiredString("EntranceId");
            c.AddEntrance(id, e.Spawn(id));
        });
        registry.Register("LevelExit", (c, e) =>
            c.AddExit(e.PositiveId("TriggerId"), e.Bounds,
                new SceneDestination(e.RequiredString("TargetLevel"),
                    e.RequiredString("TargetEntrance"))));
        registry.Register("Checkpoint", (c, e) =>
            c.AddCheckpoint(e.PositiveId("TriggerId"), e.Bounds,
                e.Spawn(e.RequiredString("SpawnId"))));
        registry.Register("WindZone", (c, e) =>
            c.AddWind(e.PositiveId("TriggerId"), e.Bounds,
                e.Number("SpeedPercent", -150, 150) / 100.0));
        registry.Register("MovingPlatform", (c, e) =>
            c.AddMovingPlatform(e.PositiveId("ColliderId"), e.Bounds,
                new Vector2D(e.Number("VelocityX", -50, 50),
                    e.Number("VelocityY", -50, 50)),
                e.Number("TravelSeconds", 0.01, 60)));
        return registry;
    }
}
