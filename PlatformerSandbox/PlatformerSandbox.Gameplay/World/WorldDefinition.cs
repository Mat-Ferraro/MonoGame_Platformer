using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Gameplay.World;

/// <summary>
/// Named spawn in authored world coordinates at world time zero. For an
/// anchored spawn, the player's box follows its named moving platform's
/// TRANSLATION when resolving a later spawn/respawn (not during normal play).
/// </summary>
public sealed record WorldSpawnPoint
{
    public WorldSpawnPoint(string id, Aabb boundsAtStart, int? movingPlatformId = null)
    {
        if (string.IsNullOrWhiteSpace(id) || id != id.Trim())
            throw new ArgumentException("A spawn requires a nonempty, trimmed ID.", nameof(id));
        if (!boundsAtStart.IsValid)
            throw new ArgumentException("Spawn bounds must be valid.", nameof(boundsAtStart));
        if (movingPlatformId is < 0)
            throw new ArgumentOutOfRangeException(nameof(movingPlatformId));
        Id = id;
        BoundsAtStart = boundsAtStart;
        MovingPlatformId = movingPlatformId;
    }

    public string Id { get; }
    public Aabb BoundsAtStart { get; }
    public int? MovingPlatformId { get; }
}

/// <summary>
/// Immutable, validated gameplay world content. A definition is not a runtime
/// world, not a scene manager and not an entity system. The WorldSession owns
/// one clock and the runtime players; this type owns stable authored IDs.
/// </summary>
public sealed class WorldDefinition
{
    private readonly StaticAabbCollider[] _staticColliders;
    private readonly MovingPlatformTrajectory[] _movingPlatforms;
    private readonly WorldSpawnPoint[] _spawns;
    private readonly WorldTriggerDefinition[] _triggers;
    private readonly Dictionary<string, WorldSpawnPoint> _spawnById;
    private readonly Dictionary<int, MovingPlatformTrajectory> _platformById;

    public WorldDefinition(string levelId,
        IReadOnlyList<StaticAabbCollider> staticColliders,
        IReadOnlyList<MovingPlatformTrajectory> movingPlatforms,
        IReadOnlyList<WorldSpawnPoint> spawnPoints,
        IReadOnlyList<WorldTriggerDefinition>? triggers = null)
    {
        if (string.IsNullOrWhiteSpace(levelId) || levelId != levelId.Trim())
            throw new ArgumentException("Level ID must be nonempty and trimmed.", nameof(levelId));
        ArgumentNullException.ThrowIfNull(staticColliders);
        ArgumentNullException.ThrowIfNull(movingPlatforms);
        ArgumentNullException.ThrowIfNull(spawnPoints);

        _staticColliders = staticColliders.ToArray();
        _movingPlatforms = movingPlatforms.ToArray();
        _spawns = spawnPoints.ToArray();
        _triggers = triggers?.ToArray() ?? Array.Empty<WorldTriggerDefinition>();
        var triggerIds = new HashSet<int>();
        foreach (WorldTriggerDefinition trigger in _triggers)
        {
            ArgumentNullException.ThrowIfNull(trigger);
            if (!triggerIds.Add(trigger.Id))
                throw new ArgumentException("Duplicate trigger ID.", nameof(triggers));
        }
        Array.Sort(_triggers, (a, b) => a.Id.CompareTo(b.Id));
        _spawnById = new Dictionary<string, WorldSpawnPoint>(StringComparer.Ordinal);
        _platformById = new Dictionary<int, MovingPlatformTrajectory>();

        var colliderIds = new HashSet<int>();
        foreach (StaticAabbCollider collider in _staticColliders)
        {
            ArgumentNullException.ThrowIfNull(collider);
            if (!colliderIds.Add(collider.Id))
                throw new ArgumentException("Duplicate collider ID.", nameof(staticColliders));
        }
        foreach (MovingPlatformTrajectory platform in _movingPlatforms)
        {
            ArgumentNullException.ThrowIfNull(platform);
            if (!colliderIds.Add(platform.Id))
                throw new ArgumentException("Duplicate static/moving collider ID.", nameof(movingPlatforms));
            _platformById.Add(platform.Id, platform);
        }
        if (_spawns.Length == 0)
            throw new ArgumentException("A playable world needs at least one spawn.", nameof(spawnPoints));
        foreach (WorldSpawnPoint spawn in _spawns)
        {
            ArgumentNullException.ThrowIfNull(spawn);
            if (!_spawnById.TryAdd(spawn.Id, spawn))
                throw new ArgumentException("Duplicate spawn ID.", nameof(spawnPoints));
            if (spawn.MovingPlatformId is int id && !_platformById.ContainsKey(id))
                throw new ArgumentException("Spawn references an unknown moving platform.", nameof(spawnPoints));

            // Author-time geometry correctness. The motor independently
            // revalidates any spawned character against the live world time.
            foreach (StaticAabbCollider collider in _staticColliders)
                if (Overlaps(spawn.BoundsAtStart, collider.Bounds))
                    throw new ArgumentException("Spawn intersects static geometry at time zero.", nameof(spawnPoints));
            foreach (MovingPlatformTrajectory platform in _movingPlatforms)
                if (Overlaps(spawn.BoundsAtStart, platform.InitialBounds))
                    throw new ArgumentException("Spawn intersects a platform at time zero.", nameof(spawnPoints));
        }
        LevelId = levelId;
    }

    public string LevelId { get; }
    public IReadOnlyList<StaticAabbCollider> StaticColliders => Array.AsReadOnly(_staticColliders);
    public IReadOnlyList<MovingPlatformTrajectory> MovingPlatformTrajectories => Array.AsReadOnly(_movingPlatforms);
    public IReadOnlyList<WorldSpawnPoint> SpawnPoints => Array.AsReadOnly(_spawns);
    public IReadOnlyList<WorldTriggerDefinition> Triggers => Array.AsReadOnly(_triggers);

    public bool HasSpawn(string spawnId) => spawnId is not null && _spawnById.ContainsKey(spawnId);

    public Aabb ResolveSpawnBounds(string spawnId, double worldTimeSeconds)
    {
        if (spawnId is null || !_spawnById.TryGetValue(spawnId, out WorldSpawnPoint? spawn))
            throw new ArgumentOutOfRangeException(nameof(spawnId), "Unknown spawn ID.");
        if (!double.IsFinite(worldTimeSeconds) || worldTimeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(worldTimeSeconds));
        if (spawn.MovingPlatformId is not int id)
            return spawn.BoundsAtStart;
        MovingPlatformTrajectory platform = _platformById[id];
        Vector2D translation = platform.Evaluate(worldTimeSeconds).Bounds.Min - platform.InitialBounds.Min;
        return spawn.BoundsAtStart.Translated(translation);
    }

    private static bool Overlaps(Aabb a, Aabb b) =>
        a.Left < b.Right && a.Right > b.Left &&
        a.Bottom < b.Top && a.Top > b.Bottom;
}
