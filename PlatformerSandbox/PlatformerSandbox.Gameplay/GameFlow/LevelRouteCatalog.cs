using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Solvers;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Gameplay.GameFlow;

/// <summary>One stable entrance and its validated, player-specific spawn mapping.</summary>
public sealed class LevelEntranceDefinition
{
    private readonly IReadOnlyDictionary<int, string> _spawns;

    public LevelEntranceDefinition(string id, string defaultSpawnId,
        IReadOnlyDictionary<int, string> playerSpawns)
    {
        if (string.IsNullOrWhiteSpace(id) || id != id.Trim())
            throw new ArgumentException("Entrance ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(defaultSpawnId))
            throw new ArgumentException("Fallback spawn is required.", nameof(defaultSpawnId));
        ArgumentNullException.ThrowIfNull(playerSpawns);
        if (playerSpawns.Any(e => e.Key <= 0 || string.IsNullOrWhiteSpace(e.Value)))
            throw new ArgumentException("Invalid player entrance mapping.", nameof(playerSpawns));
        Id = id;
        DefaultSpawnId = defaultSpawnId;
        _spawns = new ReadOnlyDictionary<int, string>(
            new Dictionary<int, string>(playerSpawns));
    }

    public string Id { get; }
    public string DefaultSpawnId { get; }
    public IReadOnlyDictionary<int, string> PlayerSpawns => _spawns;
    public string SpawnFor(int playerId) => _spawns.TryGetValue(playerId, out string? spawn)
        ? spawn : DefaultSpawnId;
}

public sealed record LevelExitDefinition
{
    public int TriggerId { get; }
    public SceneDestination Destination { get; }
    public LevelExitDefinition(int triggerId, SceneDestination destination)
    {
        if (triggerId <= 0) throw new ArgumentOutOfRangeException(nameof(triggerId));
        Destination = destination ?? throw new ArgumentNullException(nameof(destination));
        TriggerId = triggerId;
    }
}

public sealed class LevelRouteDefinition
{
    private readonly Dictionary<string, LevelEntranceDefinition> _entrances =
        new(StringComparer.Ordinal);
    private readonly Dictionary<int, LevelExitDefinition> _exits = new();

    public LevelRouteDefinition(WorldDefinition world,
        IReadOnlyList<LevelEntranceDefinition> entrances,
        IReadOnlyList<LevelExitDefinition> exits)
    {
        World = world ?? throw new ArgumentNullException(nameof(world));
        ArgumentNullException.ThrowIfNull(entrances);
        ArgumentNullException.ThrowIfNull(exits);
        foreach (LevelEntranceDefinition entrance in entrances)
        {
            ArgumentNullException.ThrowIfNull(entrance);
            if (!world.HasSpawn(entrance.DefaultSpawnId) ||
                entrance.PlayerSpawns.Values.Any(s => !world.HasSpawn(s)) ||
                !_entrances.TryAdd(entrance.Id, entrance))
                throw new ArgumentException("Duplicate entrance or missing spawn.", nameof(entrances));
        }
        if (_entrances.Count == 0 || !_entrances.ContainsKey(GameLevelIds.DefaultEntrance))
            throw new ArgumentException("A route level needs its START entrance.", nameof(entrances));
        HashSet<int> triggerIds = world.Triggers.Select(t => t.Id).ToHashSet();
        foreach (LevelExitDefinition exit in exits)
        {
            ArgumentNullException.ThrowIfNull(exit);
            if (!triggerIds.Contains(exit.TriggerId) || !_exits.TryAdd(exit.TriggerId, exit))
                throw new ArgumentException("Duplicate exit or missing trigger.", nameof(exits));
        }
    }

    public WorldDefinition World { get; }
    public string LevelId => World.LevelId;
    public bool HasEntrance(string entranceId) =>
        entranceId is not null && _entrances.ContainsKey(entranceId);
    public LevelEntranceDefinition Entrance(string entranceId) =>
        _entrances.TryGetValue(entranceId, out LevelEntranceDefinition? e)
            ? e : throw new ArgumentOutOfRangeException(nameof(entranceId));
    public bool TryResolveExit(int triggerId, out SceneDestination? destination)
    {
        if (_exits.TryGetValue(triggerId, out LevelExitDefinition? exit))
        {
            destination = exit.Destination;
            return true;
        }
        destination = null;
        return false;
    }
    public IReadOnlyList<LevelExitDefinition> Exits =>
        Array.AsReadOnly(_exits.Values.ToArray());
}

/// <summary>Validated catalog of level IDs, entrance IDs and cross-level exits.</summary>
public sealed class LevelRouteCatalog
{
    private readonly Dictionary<string, LevelRouteDefinition> _levels =
        new(StringComparer.Ordinal);

    public LevelRouteCatalog(IEnumerable<LevelRouteDefinition> levels)
    {
        ArgumentNullException.ThrowIfNull(levels);
        foreach (LevelRouteDefinition level in levels)
        {
            ArgumentNullException.ThrowIfNull(level);
            if (!_levels.TryAdd(level.LevelId, level))
                throw new ArgumentException("Duplicate LevelId.", nameof(levels));
        }
        if (_levels.Count == 0)
            throw new ArgumentException("At least one level is required.", nameof(levels));
        foreach (LevelRouteDefinition level in _levels.Values)
            foreach (LevelExitDefinition exit in level.Exits)
                if (!Supports(exit.Destination))
                    throw new ArgumentException("Exit points to missing level or entrance.", nameof(levels));
    }

    public bool Supports(SceneDestination destination) =>
        destination is not null &&
        _levels.TryGetValue(destination.LevelId, out LevelRouteDefinition? level) &&
        level.HasEntrance(destination.EntranceId);

    public LevelRouteDefinition Require(SceneDestination destination) =>
        Supports(destination) ? _levels[destination.LevelId] :
        throw new ArgumentOutOfRangeException(nameof(destination),
            "Unknown level/entrance destination.");

    public bool TryResolveExit(SceneDestination from, int triggerId,
        out SceneDestination? destination)
    {
        destination = null;
        return Supports(from) &&
            _levels[from.LevelId].TryResolveExit(triggerId, out destination);
    }
}

/// <summary>
/// Two data-defined graybox rooms linked through real committed trigger-entry
/// events. This is a scene/lifecycle fixture, not a new authoritative motor.
/// </summary>
public static class RouteLevelGym
{
    public const int LeftGateId = 601;
    public const int RightGateId = 602;
    public const string WestEntrance = "WEST";
    public const string EastEntrance = "EAST";
    public const string StartOne = "CENTER_ONE";
    public const string StartTwo = "CENTER_TWO";
    public const string WestOne = "WEST_ONE";
    public const string WestTwo = "WEST_TWO";
    public const string EastOne = "EAST_ONE";
    public const string EastTwo = "EAST_TWO";
    public const string Guest = "GUEST";

    public static LevelRouteCatalog Catalog { get; } = CreateCatalog();

    private static LevelRouteCatalog CreateCatalog() => new([
        CreateRoute(GameLevelIds.RouteA, GameLevelIds.RouteB),
        CreateRoute(GameLevelIds.RouteB, GameLevelIds.RouteA)
    ]);

    private static LevelRouteDefinition CreateRoute(string levelId, string otherLevel)
    {
        bool a = levelId == GameLevelIds.RouteA;
        var world = new WorldDefinition(levelId,
        [
            new StaticAabbCollider(600, Box(-9, -1.5, 9, -1.0)),
            new StaticAabbCollider(603, a ? Box(-2.0, 2.9, 0.0, 3.2) :
                                            Box(0.5, 2.9, 2.5, 3.2))
        ],
        [
            new MovingPlatformTrajectory(a ? 610 : 620,
                a ? Box(-3.0, 1.3, -1.0, 1.6) : Box(1.0, 1.3, 3.0, 1.6),
                new Vector2D(a ? 0.50 : -0.50, 0.0), 1.3)
        ],
        [
            Spawn(StartOne, -0.8), Spawn(StartTwo, 0.6),
            Spawn(WestOne, -5.2), Spawn(WestTwo, -3.8),
            Spawn(EastOne, 3.0), Spawn(EastTwo, 4.4), Spawn(Guest, -2.8)
        ],
        [
            new WorldTriggerDefinition(LeftGateId, "EXIT_LEFT", Box(-8.0, -1.0, -6.0, 1.3)),
            new WorldTriggerDefinition(RightGateId, "EXIT_RIGHT", Box(6.0, -1.0, 8.0, 1.3))
        ]);
        return new LevelRouteDefinition(world,
        [
            new LevelEntranceDefinition(GameLevelIds.DefaultEntrance, Guest,
                new Dictionary<int, string> { [1] = StartOne, [2] = StartTwo }),
            new LevelEntranceDefinition(WestEntrance, Guest,
                new Dictionary<int, string> { [1] = WestOne, [2] = WestTwo }),
            new LevelEntranceDefinition(EastEntrance, Guest,
                new Dictionary<int, string> { [1] = EastOne, [2] = EastTwo })
        ],
        [
            new LevelExitDefinition(LeftGateId, new SceneDestination(otherLevel, EastEntrance)),
            new LevelExitDefinition(RightGateId, new SceneDestination(otherLevel, WestEntrance))
        ]);
    }

    private static WorldSpawnPoint Spawn(string id, double x) =>
        new(id, Box(x, -1.0, x + 0.8, 0.6));

    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    public static WorldSession Create(SceneDestination destination,
        IReadOnlyList<GameSessionParticipant> players,
        CollisionAssistancePolicy assistance,
        SimulationFaultSensitivity sensitivity,
        long worldInstanceId)
    {
        LevelRouteDefinition route = Catalog.Require(destination);
        ArgumentNullException.ThrowIfNull(players);
        var ground = HorizontalMotionSolver.Solve(MovementGymTuning.Baseline.Ground);
        var air = HorizontalMotionSolver.Solve(MovementGymTuning.Baseline.Air);
        var jump = JumpFlightSolver.ForHeldJump(JumpConstraintSolver.Solve(
            new JumpConstraints(3.7, 0.38, 0.34,
                ApexRegionFraction: 0.10, TimeNearApexSeconds: 0.32)));
        var world = new WorldSession(route.World, ground, air, jump,
            gravityAcceleration: -24.0,
            fixedStepSeconds: MovementGymSession.FixedStepSeconds,
            releaseConstraints: new JumpReleaseConstraints(1.10),
            assistancePolicy: assistance,
            faultPolicy: new SimulationFaultPolicy(sensitivity),
            worldInstanceId: worldInstanceId);
        LevelEntranceDefinition entrance = route.Entrance(destination.EntranceId);
        foreach (GameSessionParticipant member in players.OrderBy(p => p.PlayerId))
            world.AddPlayer(new WorldPlayerSlotRegistration(member.PlayerId,
                member.CharacterId, entrance.SpawnFor(member.PlayerId),
                member.InputSourceId));
        return world;
    }
}
