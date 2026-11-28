using PlatformerSandbox.Simulation.Physics;
using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Foundation.Performance;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Input.Buffering;
using PlatformerSandbox.Gameplay.States;
using PlatformerSandbox.Gameplay.Abilities;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Gameplay.World;

/// <summary>
/// One normalized, device-independent player command on an authoritative tick.
/// A missing command means Neutral/no edges for that player, not a replay of
/// the previous command. PlayerId is distinct from input SourceId and ColliderId.
/// </summary>
public readonly record struct WorldPlayerCommand(
    int PlayerId, HorizontalDirection Direction,
    bool JumpPressed = false, bool JumpReleased = false);

/// <summary>Read-only, player-attributed committed world state.</summary>
public readonly record struct WorldPlayerSnapshot(
    int PlayerId, Aabb Bounds, Vector2D Velocity, bool IsGrounded,
    bool IsJumping, int? MovingSupportId, int JumpsStarted,
    bool JumpStartedThisTick)
{
    public WorldLifeStatus Life { get; init; }
    public bool IsDead => Life.IsDead;
    public CharacterStateSnapshot State { get; init; }
    public WorldMotionSnapshot Motion { get; init; }
}

public sealed record WorldStepResult(
    long WorldTick, double WorldTimeSeconds,
    IReadOnlyList<WorldPlayerSnapshot> Players)
{
    /// <summary>
    /// Nonempty ONLY on a successfully committed Step result. A Snapshot()
    /// call cannot replay events. Each result owns a defensive, read-only copy.
    /// </summary>
    public IReadOnlyList<WorldGameplayEvent> Events { get; init; } =
        Array.Empty<WorldGameplayEvent>();
}

/// <summary>
/// First shared-world runtime. One integer tick owns platform time, every
/// player's fixed interval, respawn timing and stable PlayerId update order.
/// Inputs and committed state are device/MonoGame independent.
///
/// This phase deliberately leaves out dynamic player-to-player collision,
/// level loading, session progression, a general entity framework and
/// event presentation. Each motor retains authoritative collision resolution.
/// </summary>
public sealed class WorldSession
{
    private sealed class PlayerState
    {
        public PlayerState(int id, int characterId, string? spawnId,
            int? inputSourceId, CharacterMovementController motor,
            BufferedJumpInput jumpTiming, CharacterStateMachine states,
            CharacterAbilityCoordinator abilities,
            WorldPlayerMotion motion)
        {
            Id = id;
            CharacterId = characterId;
            SpawnId = spawnId;
            InitialSpawnId = spawnId;
            InitialBounds = motor.Bounds;
            InputSourceId = inputSourceId;
            Motor = motor;
            JumpTiming = jumpTiming;
            States = states;
            Abilities = abilities;
            Motion = motion;
        }

        public int Id { get; }
        public int CharacterId { get; }
        public string? SpawnId { get; set; }
        public string? InitialSpawnId { get; }
        public Aabb InitialBounds { get; }
        public WorldLifeStatus Life { get; set; }
        public int? InputSourceId { get; set; }
        public CharacterMovementController Motor { get; set; }
        public BufferedJumpInput JumpTiming { get; set; }
        public CharacterStateMachine States { get; set; }
        public CharacterAbilityCoordinator Abilities { get; set; }
        public WorldPlayerMotion Motion { get; set; }
        public bool LastJumpStarted { get; set; }
    }

    private readonly SortedDictionary<int, PlayerState> _players = new();
    private readonly List<PendingWorldEvent> _pendingActions = new();
    private readonly HashSet<int> _pendingDeaths = new();
    private readonly Dictionary<int, HashSet<int>> _triggerInside = new();
    private readonly Dictionary<int, HashSet<int>> _wallContact = new();

    private readonly record struct PendingWorldEvent(
        WorldGameplayEventKind Kind, int PlayerId, Vector2D Position);
    private WorldDefinition? _definition;
    private readonly SpatialColliderSet _staticColliders;
    private readonly MovingPlatformTrajectory[] _platforms;
    private readonly HorizontalMotionProfile _groundMotion;
    private readonly HorizontalMotionProfile _airMotion;
    private readonly JumpFlightTrajectory _jumpFlight;
    private readonly double _gravityAcceleration;
    private readonly JumpReleaseConstraints? _releaseConstraints;
    private readonly JumpDescentConstraints _descentConstraints;
    private readonly JumpTimingConstraints _jumpTimingConstraints;
    private readonly CollisionAssistancePolicy _assistancePolicy;
    private SimulationFaultPolicy _faultPolicy;

    public WorldSession(
        IReadOnlyList<StaticAabbCollider> staticColliders,
        IReadOnlyList<MovingPlatformTrajectory> movingPlatforms,
        HorizontalMotionProfile groundMotion,
        HorizontalMotionProfile airMotion,
        JumpFlightTrajectory jumpFlight,
        double gravityAcceleration,
        double fixedStepSeconds = 1.0 / 60.0,
        long initialWorldTick = 0,
        JumpReleaseConstraints? releaseConstraints = null,
        JumpDescentConstraints descentConstraints = default,
        JumpTimingConstraints? jumpTimingConstraints = null,
        CollisionAssistancePolicy? assistancePolicy = null,
        SimulationFaultPolicy? faultPolicy = null,
        long worldInstanceId = 1)
    {
        ArgumentNullException.ThrowIfNull(staticColliders);
        ArgumentNullException.ThrowIfNull(movingPlatforms);
        ArgumentNullException.ThrowIfNull(groundMotion);
        ArgumentNullException.ThrowIfNull(airMotion);
        ArgumentNullException.ThrowIfNull(jumpFlight);
        if (!double.IsFinite(fixedStepSeconds) || fixedStepSeconds <= 0.0)
            throw new ArgumentOutOfRangeException(nameof(fixedStepSeconds));
        if (initialWorldTick < 0 ||
            !double.IsFinite(initialWorldTick * fixedStepSeconds))
            throw new ArgumentOutOfRangeException(nameof(initialWorldTick));
        if (!double.IsFinite(gravityAcceleration) || gravityAcceleration >= 0.0)
            throw new ArgumentOutOfRangeException(nameof(gravityAcceleration));
        if (worldInstanceId <= 0)
            throw new ArgumentOutOfRangeException(nameof(worldInstanceId));
        WorldInstanceId = worldInstanceId;

        _staticColliders = SpatialColliderSet.Wrap(staticColliders);
        _platforms = movingPlatforms.Select(p => (p ?? throw new ArgumentException("Null platform.", nameof(movingPlatforms))).CreateRuntimeCopy()).ToArray();
        var colliderIds = new HashSet<int>();
        foreach (StaticAabbCollider collider in _staticColliders)
        {
            ArgumentNullException.ThrowIfNull(collider);
            if (!colliderIds.Add(collider.Id))
                throw new ArgumentException("Collider IDs must be unique.", nameof(staticColliders));
        }
        foreach (MovingPlatformTrajectory platform in _platforms)
        {
            ArgumentNullException.ThrowIfNull(platform);
            if (!colliderIds.Add(platform.Id))
                throw new ArgumentException("Collider IDs must be unique across the world.", nameof(movingPlatforms));
        }
        _groundMotion = groundMotion;
        _airMotion = airMotion;
        _jumpFlight = jumpFlight;
        _gravityAcceleration = gravityAcceleration;
        _releaseConstraints = releaseConstraints;
        _descentConstraints = descentConstraints;
        _jumpTimingConstraints = jumpTimingConstraints ?? new JumpTimingConstraints(0.10, 0.10);
        _assistancePolicy = assistancePolicy ?? CollisionAssistancePolicy.Disabled;
        _faultPolicy = faultPolicy ?? SimulationFaultPolicy.Development;
        FixedStepSeconds = fixedStepSeconds;
        WorldTick = initialWorldTick;
    }

    /// <summary>
    /// Data-defined construction path. Existing raw-geometry callers retain
    /// their earlier API and behavior; only this path enables named spawns.
    /// Authored worlds respawn named players after a classified crush by default.
    /// Tests may explicitly request Throw, independent of numerical fault policy.
    /// </summary>
    public WorldSession(
        WorldDefinition definition,
        HorizontalMotionProfile groundMotion,
        HorizontalMotionProfile airMotion,
        JumpFlightTrajectory jumpFlight,
        double gravityAcceleration,
        double fixedStepSeconds = 1.0 / 60.0,
        long initialWorldTick = 0,
        JumpReleaseConstraints? releaseConstraints = null,
        JumpDescentConstraints descentConstraints = default,
        JumpTimingConstraints? jumpTimingConstraints = null,
        CollisionAssistancePolicy? assistancePolicy = null,
        SimulationFaultPolicy? faultPolicy = null,
        long worldInstanceId = 1,
        WorldCrushPolicy crushPolicy = WorldCrushPolicy.RespawnAtNamedSpawn)
        : this((definition ?? throw new ArgumentNullException(nameof(definition))).StaticColliders,
            definition.MovingPlatformTrajectories, groundMotion, airMotion, jumpFlight,
            gravityAcceleration, fixedStepSeconds, initialWorldTick,
            releaseConstraints, descentConstraints, jumpTimingConstraints,
            assistancePolicy, faultPolicy, worldInstanceId)
    {
        if (!Enum.IsDefined(crushPolicy))
            throw new ArgumentOutOfRangeException(nameof(crushPolicy));
        _definition = definition;
        CrushPolicy = crushPolicy;
    }

    public WorldDefinition? Definition => _definition;
    public WorldCrushPolicy CrushPolicy { get; private set; } = WorldCrushPolicy.Throw;
    public WorldRespawnSettings RespawnSettings { get; private set; } = new();
    public void ConfigureCrushLifecycle(WorldCrushPolicy policy,WorldRespawnSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);settings.Validate();
        if(!Enum.IsDefined(policy)) throw new ArgumentOutOfRangeException(nameof(policy));
        CrushPolicy=policy;RespawnSettings=settings;
    }
    /// <summary>Queue a non-crush death for the next successful tick (future hazards/combat adapter).</summary>
    public void RequestPlayerDeath(int playerId)
    {
        if(!_players.TryGetValue(playerId,out var player))throw new ArgumentOutOfRangeException(nameof(playerId));
        if(!player.Life.IsDead)_pendingDeaths.Add(playerId);
    }
    /// <summary>Manual respawn requests wait for a safe location and never bypass the life budget.</summary>
    public void RequestPlayerRespawn(int playerId)
    {
        if(!_players.TryGetValue(playerId,out var player))throw new ArgumentOutOfRangeException(nameof(playerId));
        if(!player.Life.IsDead||player.Life.Mode!=WorldRespawnMode.Manual||!player.Life.RespawnAllowed)
            throw new InvalidOperationException("Player has no available manual respawn.");
        player.Life=player.Life with {ManualRequested=true};InvalidatePlayerViews();
    }
    public long WorldInstanceId { get; }

    public double FixedStepSeconds { get; }
    public long WorldTick { get; private set; }
    public double WorldTimeSeconds => WorldTick * FixedStepSeconds;
    public int PlayerCount => _players.Count;
    public DynamicBodyWorld? DynamicBodies { get; private set; }
    public double CharacterPushForce { get; private set; }=120;
    public void SetCharacterPushForce(double force)
    {
        if(!double.IsFinite(force)||force<0||force>10000)throw new ArgumentOutOfRangeException(nameof(force));
        CharacterPushForce=force;
    }
    private IReadOnlyList<PhysicalBodySnapshot> LiveCharacterBounds()=>_players.Values.Where(p=>!p.Life.IsDead)
        .Select(p=>new PhysicalBodySnapshot(new(PhysicalBodyKind.KinematicCharacter,p.CharacterId),p.Motor.Bounds,p.Motor.Velocity)).ToArray();
    private int PropColliderId(int id)
    {
        int mapped=int.MaxValue-id;
        if(mapped<0||_staticColliders.Any(c=>c.Id==mapped)||_platforms.Any(p=>p.Id==mapped))
            throw new InvalidOperationException("Prop adapter ID conflicts with authored collider ID.");
        return mapped;
    }
    private IReadOnlyList<StaticAabbCollider> CharacterGeometry(DynamicBodyState[] props)=>props.Length==0?_staticColliders:
        SpatialColliderSet.Wrap(_staticColliders.Concat(props.Select(b=>new StaticAabbCollider(PropColliderId(b.Id.LocalId),b.Bounds))).ToArray());
    private DynamicBodyState[] CharacterSolidProps()=>DynamicBodies?.Bodies.Where(DynamicBodyWorld.CharacterSolid).ToArray()??Array.Empty<DynamicBodyState>();
    public void InstallDynamicBodies(PhysicsMaterialCatalog materials,IEnumerable<WorldDynamicBodyFixtures.Fixture> fixtures, IReadOnlyDictionary<int,string>? terrainMaterials=null)
    {
        var candidate=new DynamicBodyWorld(_staticColliders,materials,terrainMaterials,LiveCharacterBounds,()=>MovingPlatforms);
        foreach(var fixture in fixtures) { _=PropColliderId(fixture.Id);fixture.AddTo(candidate); }
        DynamicBodies=candidate;
    }
    private IReadOnlyList<MovingPlatformTrajectory>? _trajectoryView;
    private IReadOnlyList<MovingPlatformSample>? _platformView;
    private long _platformViewTick = -1;
    private WorldStepResult? _committedSnapshot;
    private IReadOnlyList<WorldPlayerSlotInfo>? _slotView;
    private void InvalidatePlayerViews() { _committedSnapshot = null; _slotView = null; }
    public IReadOnlyList<StaticAabbCollider> StaticColliders => _staticColliders;
    private SpatialIndex<WorldSpatialObject>? _spatialStatic;
    public WorldSpatialSnapshot CaptureSpatialSnapshot()
    {
        _spatialStatic ??= WorldSpatialSnapshot.Build(_staticColliders.Select(c=>new WorldSpatialObject(WorldSpatialKind.StaticCollider,c.Id,c.Bounds))
            .Concat((_definition?.Triggers??Array.Empty<WorldTriggerDefinition>()).Select(t=>new WorldSpatialObject(WorldSpatialKind.Trigger,t.Id,t.Bounds))),false);
        return new WorldSpatialSnapshot(WorldTick,_spatialStatic,MovingPlatforms.Select(p=>new WorldSpatialObject(WorldSpatialKind.MovingPlatform,p.PlatformId,p.Bounds))
            .Concat(Snapshot().Players.Where(p=>!p.IsDead).Select(p=>new WorldSpatialObject(WorldSpatialKind.Player,p.PlayerId,p.Bounds)))
            .Concat(DynamicBodies?.Bodies.Select(b=>new WorldSpatialObject(WorldSpatialKind.DynamicBody,b.Id.LocalId,b.Bounds))??Enumerable.Empty<WorldSpatialObject>()));
    }
    private SpatialIndex<WorldTriggerDefinition>? _spatialTriggers;
    private IEnumerable<WorldTriggerDefinition> TriggerCandidates(Aabb bounds,int playerId)
    {
        var triggers=_definition?.Triggers??Array.Empty<WorldTriggerDefinition>();
        if(SpatialQueryPolicy.Triggers==SpatialQueryMode.Reference||triggers.Count==0) return triggers;
        if(_spatialTriggers is null)
        {
            _spatialTriggers=new(); foreach(var trigger in triggers) _spatialTriggers.Add(new(trigger.Id,SpatialBounds.From(trigger.Bounds),trigger));
        }
        var output=new List<SpatialEntry<WorldTriggerDefinition>>();
        _spatialTriggers.Query(SpatialBounds.From(bounds),output,new HashSet<int>(),SpatialQueryPolicy.Triggers);
        var selected=output.ToDictionary(e=>e.Id,e=>e.Value);
        // Exit events must be evaluated even after a player leaves the query region.
        if(_triggerInside.TryGetValue(playerId,out var previous))
            foreach(var trigger in triggers) if(previous.Contains(trigger.Id)) selected.TryAdd(trigger.Id,trigger);
        return selected.Values.OrderBy(t=>t.Id);
    }
    public IReadOnlyList<MovingPlatformTrajectory> MovingPlatformTrajectories =>
        EnginePerformanceOptions.Snapshots ? (_trajectoryView ??= Array.AsReadOnly(_platforms)) : Array.AsReadOnly(_platforms);
    public IReadOnlyList<MovingPlatformSample> MovingPlatforms
    {
        get
        {
            if (!EnginePerformanceOptions.Snapshots) return BuildPlatformView();
            if (_platformView is null || _platformViewTick != WorldTick)
            {
                _platformView = BuildPlatformView();
                _platformViewTick = WorldTick;
            }
            return _platformView;
        }
    }
    private IReadOnlyList<MovingPlatformSample> BuildPlatformView() =>
        Array.AsReadOnly(_platforms.Select(p => p.Evaluate(WorldTimeSeconds)).ToArray());

    // Obstruction pauses are distinct from manual playback pauses. A blocked
    // path retries its return leg after a deterministic simulation-time delay.
    public const double DefaultPlatformObstructionRecoverySeconds = .25;
    private readonly Dictionary<int,long> _propObstructedPlatforms=new();
    private readonly Dictionary<int,double?> _platformRecoveryOverrides=new();
    public long PropPlatformStops { get; private set; }
    public long PropPlatformRecoveryAttempts { get; private set; }
    public bool IsPropObstructed(int platformId)=>_propObstructedPlatforms.ContainsKey(platformId);

    // Null means hold until a manual command. Zero is intentionally not allowed:
    // a doubly blocked platform must not enter a same-tick reversal loop.
    public double? GetPlatformObstructionRecoverySeconds(int platformId)
    {
        if(!_platforms.Any(p=>p.Id==platformId))throw new ArgumentOutOfRangeException(nameof(platformId));
        return _platformRecoveryOverrides.TryGetValue(platformId,out var seconds)
            ?seconds:DefaultPlatformObstructionRecoverySeconds;
    }
    public void ConfigurePlatformObstructionRecovery(int platformId,double? seconds)
    {
        _=GetPlatformObstructionRecoverySeconds(platformId);
        if(seconds is double value&&(!double.IsFinite(value)||value<=0))
            throw new ArgumentOutOfRangeException(nameof(seconds));
        _platformRecoveryOverrides[platformId]=seconds;
    }
    public double? GetPlatformObstructionRemainingSeconds(int platformId)
    {
        var delay=GetPlatformObstructionRecoverySeconds(platformId);
        return delay is double seconds&&_propObstructedPlatforms.TryGetValue(platformId,out long since)
            ?Math.Max(0,seconds-(WorldTick-since)*FixedStepSeconds):null;
    }
    /// <summary>Manual playback commands override automatic obstruction recovery.
    /// A reverse releases an obstruction pause, but preserves a manual pause.</summary>
    public void ControlPlatform(int platformId, PlatformPlaybackCommand command)
    {
        var platform = _platforms.FirstOrDefault(p => p.Id == platformId)
            ?? throw new ArgumentOutOfRangeException(nameof(platformId));
        platform.ApplyPlayback(command, WorldTimeSeconds);
        if(_propObstructedPlatforms.Remove(platformId)&&command==PlatformPlaybackCommand.Reverse)
            platform.ApplyPlayback(PlatformPlaybackCommand.Resume,WorldTimeSeconds);
        _platformView = null;
        _committedSnapshot = null;
    }

    private Aabb ResolveRuntimeSpawn(string spawnId, double time)
    {
        var definition = _definition ?? throw new InvalidOperationException("Named spawns require a WorldDefinition.");
        var spawn = definition.SpawnPoints.FirstOrDefault(s => s.Id == spawnId)
            ?? throw new ArgumentOutOfRangeException(nameof(spawnId));
        if (spawn.MovingPlatformId is not int id) return spawn.BoundsAtStart;
        var platform = _platforms.Single(p => p.Id == id);
        return spawn.BoundsAtStart.Translated(platform.Evaluate(time).Bounds.Min - platform.InitialBounds.Min);
    }

    public bool HasPlayer(int playerId) => _players.ContainsKey(playerId);

    /// <summary>Join at the CURRENT world time, not at the beginning of the level.</summary>
    public void AddPlayer(int playerId, Aabb initialBounds)
    {
        if (playerId <= 0 || _players.ContainsKey(playerId))
            throw new ArgumentOutOfRangeException(nameof(playerId),
                "Player IDs must be positive and unique.");
        if (_players.Values.Any(p => p.CharacterId == playerId))
            throw new ArgumentException("Character IDs must be unique.", nameof(playerId));
        // Construct first: an invalid spawn never enters the registry.
        CharacterMovementController motor = CreateMotor(initialBounds);
        var input = new BufferedJumpInput(_jumpTimingConstraints);
        _players.Add(playerId, new PlayerState(playerId, playerId, null, null, motor, input,
            CreateCharacterStates(motor), new CharacterAbilityCoordinator(),
            new WorldPlayerMotion()));
        InvalidatePlayerViews();
        _pendingActions.Add(new PendingWorldEvent(
            WorldGameplayEventKind.PlayerJoined, playerId, motor.Bounds.Center));
    }

    /// <summary>
    /// Join using a validated spawn and independent player/character/source
    /// identities. Do not modify the registry unless motor creation succeeds.
    /// </summary>
    public void AddPlayer(WorldPlayerSlotRegistration registration)
    {
        WorldDefinition definition = _definition ??
            throw new InvalidOperationException("Named slots need a WorldDefinition.");
        if (registration.PlayerId <= 0 || _players.ContainsKey(registration.PlayerId))
            throw new ArgumentOutOfRangeException(nameof(registration), "Duplicate/invalid player ID.");
        if (registration.CharacterId <= 0 ||
            _players.Values.Any(p => p.CharacterId == registration.CharacterId))
            throw new ArgumentException("Duplicate/invalid character ID.", nameof(registration));
        if (registration.InputSourceId is int source &&
            (source <= 0 || _players.Values.Any(p => p.InputSourceId == source)))
            throw new ArgumentException("Input source already assigned or invalid.", nameof(registration));
        Aabb bounds = ResolveRuntimeSpawn(registration.SpawnId, WorldTimeSeconds);
        CharacterMovementController motor = CreateMotor(bounds);
        var input = new BufferedJumpInput(_jumpTimingConstraints);
        _players.Add(registration.PlayerId, new PlayerState(registration.PlayerId,
            registration.CharacterId, registration.SpawnId, registration.InputSourceId,
            motor, input, CreateCharacterStates(motor), new CharacterAbilityCoordinator(),
            new WorldPlayerMotion()));
        InvalidatePlayerViews();
        _pendingActions.Add(new PendingWorldEvent(
            WorldGameplayEventKind.PlayerJoined, registration.PlayerId, motor.Bounds.Center));
    }

    public WorldPlayerSlotInfo GetPlayerSlot(int playerId)
    {
        if (!_players.TryGetValue(playerId, out PlayerState? player))
            throw new ArgumentOutOfRangeException(nameof(playerId));
        return new WorldPlayerSlotInfo(player.Id, player.CharacterId,
            player.SpawnId, player.InputSourceId);
    }

    public IReadOnlyList<WorldPlayerSlotInfo> PlayerSlots => EnginePerformanceOptions.Snapshots
        ? (_slotView ??= BuildSlotView()) : BuildSlotView();
    private IReadOnlyList<WorldPlayerSlotInfo> BuildSlotView() =>
        Array.AsReadOnly(_players.Values.Select(p => GetPlayerSlot(p.Id)).ToArray());

    /// <summary>Rebind a device/source without resetting a motor or the world clock.</summary>
    public void AssignInputSource(int playerId, int? inputSourceId)
    {
        if (!_players.TryGetValue(playerId, out PlayerState? player))
            throw new ArgumentOutOfRangeException(nameof(playerId));
        if (inputSourceId is int source &&
            (source <= 0 || _players.Values.Any(p => p.Id != playerId && p.InputSourceId == source)))
            throw new ArgumentException("Input source already assigned or invalid.", nameof(inputSourceId));
        player.InputSourceId = inputSourceId;
        InvalidatePlayerViews();
    }

    /// <summary>Change a future respawn destination without moving the player.</summary>
    public void SetPlayerSpawn(int playerId, string spawnId)
    {
        if (!_players.TryGetValue(playerId, out PlayerState? player))
            throw new ArgumentOutOfRangeException(nameof(playerId));
        WorldDefinition definition = _definition ??
            throw new InvalidOperationException("Named spawns require a WorldDefinition.");
        if (!definition.HasSpawn(spawnId))
            throw new ArgumentOutOfRangeException(nameof(spawnId));
        player.SpawnId = spawnId;
        InvalidatePlayerViews();
    }

    public void RemovePlayer(int playerId)
    {
        if (!_players.TryGetValue(playerId, out PlayerState? player))
            throw new ArgumentOutOfRangeException(nameof(playerId));
        Vector2D position = player.Motor.Bounds.Center;
        _players.Remove(playerId);
        _pendingDeaths.Remove(playerId);
        _triggerInside.Remove(playerId);
        _wallContact.Remove(playerId);
        InvalidatePlayerViews();
        _pendingActions.Add(new PendingWorldEvent(
            WorldGameplayEventKind.PlayerRemoved, playerId, position));
    }

    /// <summary>
    /// Reset only this character's motor and buffered input at the current
    /// shared world time. No platform, other character or world tick resets.
    /// </summary>
    public void RespawnPlayer(int playerId, Aabb boundsAtCurrentWorldTime)
    {
        if (!_players.TryGetValue(playerId, out PlayerState? state))
            throw new ArgumentOutOfRangeException(nameof(playerId));
        CharacterMovementController motor = CreateMotor(boundsAtCurrentWorldTime);
        state.Motor = motor;
        state.JumpTiming = new BufferedJumpInput(_jumpTimingConstraints);
        state.States = CreateCharacterStates(motor);
        state.Abilities = new CharacterAbilityCoordinator();
        state.Motion = new WorldPlayerMotion();
        state.LastJumpStarted = false;
        state.Life=state.Life.Revive(false);
        _pendingDeaths.Remove(playerId);
        _triggerInside.Remove(playerId);
        _wallContact.Remove(playerId);
        InvalidatePlayerViews();
        _pendingActions.Add(new PendingWorldEvent(
            WorldGameplayEventKind.PlayerRespawned, playerId, motor.Bounds.Center));
    }

    /// <summary>
    /// Spawn from this player's named definition at the CURRENT world tick.
    /// The original bounds-based respawn overload remains available.
    /// </summary>
    public void RespawnPlayer(int playerId)
    {
        if (!_players.TryGetValue(playerId, out PlayerState? player))
            throw new ArgumentOutOfRangeException(nameof(playerId));
        if (player.SpawnId is null || _definition is null)
            throw new InvalidOperationException("Player has no authored spawn.");
        Aabb bounds = ResolveRuntimeSpawn(player.SpawnId, WorldTimeSeconds);
        RespawnPlayer(playerId, bounds);
    }

    public void SetFaultSensitivity(SimulationFaultSensitivity sensitivity)
    {
        var policy = new SimulationFaultPolicy(sensitivity);
        foreach (PlayerState state in _players.Values)
            state.Motor.SetFaultPolicy(policy);
        _faultPolicy = policy;
    }

    /// <summary>Immutable per-player resource and ability timers.</summary>
    public CharacterAbilitySnapshot GetPlayerAbilities(int playerId)
    {
        if (!_players.TryGetValue(playerId, out PlayerState? state))
            throw new ArgumentOutOfRangeException(nameof(playerId));
        return state.Abilities.Snapshot;
    }

    public WorldMotionSnapshot GetPlayerMotion(int playerId)
    {
        if (!_players.TryGetValue(playerId, out PlayerState? state))
            throw new ArgumentOutOfRangeException(nameof(playerId));
        return state.Motion.Snapshot(state.Abilities.HasPrimaryAction,
            _groundMotion.MaximumSpeed);
    }

    public WorldPlayerSnapshot GetPlayer(int playerId)
    {
        if (!_players.TryGetValue(playerId, out PlayerState? state))
            throw new ArgumentOutOfRangeException(nameof(playerId));
        return ToSnapshot(state);
    }

    public WorldStepResult Snapshot() => EnginePerformanceOptions.Snapshots
        ? (_committedSnapshot ??= BuildSnapshot()) : BuildSnapshot();
    private WorldStepResult BuildSnapshot() => new(WorldTick, WorldTimeSeconds,
        Array.AsReadOnly(_players.Values.Select(ToSnapshot).ToArray()));

    /// <summary>
    /// One world update, in stable PlayerId order. Stage each motor and jump
    /// buffer; publish none of the players or clock until EVERY player step
    /// succeeds. Invalid commands are rejected before any simulation work.
    /// </summary>
    public WorldStepResult Step(IReadOnlyList<WorldPlayerCommand> commands) =>
        Step(commands, Array.Empty<CharacterStateRequest>(),
            Array.Empty<CharacterAbilityRequest>());

    /// <summary>
    /// Stage character-state requests, input gating, motors, and events together.
    /// Invalid requests fail BEFORE staging; a motor failure publishes no
    /// state changes, events, or world tick. Requests are processed by player
    /// ID with stun before action, irrespective of input list ordering.
    /// </summary>
    public WorldStepResult Step(IReadOnlyList<WorldPlayerCommand> commands,
        IReadOnlyList<CharacterStateRequest> stateRequests) =>
        Step(commands, stateRequests, Array.Empty<CharacterAbilityRequest>());

    /// <summary>
    /// Stage A09 state requests, A10 ability lifecycle, movement and event
    /// delivery in one atomic world tick. No live ability state is committed
    /// until every player motor succeeds.
    /// </summary>
    public WorldStepResult Step(IReadOnlyList<WorldPlayerCommand> commands,
        IReadOnlyList<CharacterStateRequest> stateRequests,
        IReadOnlyList<CharacterAbilityRequest> abilityRequests) =>
        Step(commands, stateRequests, abilityRequests,
            Array.Empty<WorldMotionRequest>());

    /// <summary>
    /// Stage environmental transport and one-shot impulses alongside A09/A10
    /// and every collision. Rejection of any player step rolls back ALL
    /// motion-source changes and their committed events.
    /// </summary>
    public WorldStepResult Step(IReadOnlyList<WorldPlayerCommand> commands,
        IReadOnlyList<CharacterStateRequest> stateRequests,
        IReadOnlyList<CharacterAbilityRequest> abilityRequests,
        IReadOnlyList<WorldMotionRequest> motionRequests)
    {
        // The character motor and prop solver see the same staged playback.
        // Recovery is attempted only at a fixed tick boundary. If the return
        // leg is also blocked, pause again and retry after a fresh delay.
        bool canCompress=DynamicBodies is { } dynamics&&dynamics.Bodies.Any(DynamicBodyWorld.PlatformSolid)&&_platforms.Length>0;
        if(!canCompress&&_propObstructedPlatforms.Count==0)
            return StepAttempt(commands,stateRequests,abilityRequests,motionRequests);
        long tick=WorldTick;
        var restore=new List<Action>();var stopped=new HashSet<int>();var recovering=new List<int>();
        try
        {
            foreach(int id in _propObstructedPlatforms.Keys.OrderBy(id=>id))
            {
                if(GetPlatformObstructionRemainingSeconds(id)!=0.0)continue;
                var path=_platforms.Single(p=>p.Id==id);
                restore.Add(path.CapturePlaybackRollback());
                path.ApplyPlayback(PlatformPlaybackCommand.Reverse,WorldTimeSeconds);
                path.ApplyPlayback(PlatformPlaybackCommand.Resume,WorldTimeSeconds);
                recovering.Add(id);
            }
            if(recovering.Count>0){_platformView=null;_committedSnapshot=null;}
            while(true)
            {
                try
                {
                    var result=StepAttempt(commands,stateRequests,abilityRequests,motionRequests);
                    foreach(int id in recovering)_propObstructedPlatforms.Remove(id);
                    foreach(int id in stopped)_propObstructedPlatforms[id]=tick;
                    PropPlatformStops+=stopped.Count;
                    PropPlatformRecoveryAttempts+=recovering.Count;
                    return result;
                }
                catch(PlatformPropCompressionException ex) when(WorldTick==tick)
                {
                    bool progress=false;
                    foreach(int id in ex.PlatformIds)
                    {
                        var path=_platforms.Single(p=>p.Id==id);
                        if(path.IsPaused(WorldTimeSeconds)||!stopped.Add(id))continue;
                        restore.Add(path.CapturePlaybackRollback());
                        path.ApplyPlayback(PlatformPlaybackCommand.Pause,WorldTimeSeconds);progress=true;
                    }
                    if(!progress)throw;
                    _platformView=null;_committedSnapshot=null;
                }
            }
        }
        catch
        {
            if(WorldTick==tick){for(int i=restore.Count-1;i>=0;i--)restore[i]();_platformView=null;_committedSnapshot=null;}
            throw;
        }
    }
    private WorldStepResult StepAttempt(IReadOnlyList<WorldPlayerCommand> commands,
        IReadOnlyList<CharacterStateRequest> stateRequests,
        IReadOnlyList<CharacterAbilityRequest> abilityRequests,
        IReadOnlyList<WorldMotionRequest> motionRequests)
    {
        ArgumentNullException.ThrowIfNull(motionRequests);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(stateRequests);
        ArgumentNullException.ThrowIfNull(abilityRequests);
        var inputByPlayer = new Dictionary<int, WorldPlayerCommand>();
        foreach (WorldPlayerCommand command in commands)
        {
            if (!_players.ContainsKey(command.PlayerId))
                throw new ArgumentOutOfRangeException(nameof(commands),
                    $"Unknown player: {command.PlayerId}.");
            if (!Enum.IsDefined(command.Direction))
                throw new ArgumentOutOfRangeException(nameof(commands),
                    "Invalid horizontal direction.");
            if (!inputByPlayer.TryAdd(command.PlayerId, command))
                throw new ArgumentException("Duplicate player command.", nameof(commands));
        }

        // Validate source motion before forking anything. One operation of
        // each kind per player per tick; sorted so caller enumeration order
        // cannot change the committed simulation or event stream.
        var motionByPlayer = new Dictionary<int, List<WorldMotionRequest>>();
        foreach (WorldMotionRequest request in motionRequests)
        {
            if (!_players.ContainsKey(request.PlayerId) ||
                !Enum.IsDefined(request.Kind) ||
                !double.IsFinite(request.HorizontalSpeed) ||
                Math.Abs(request.HorizontalSpeed) > 10000.0)
                throw new ArgumentOutOfRangeException(nameof(motionRequests));
            if (!motionByPlayer.TryGetValue(request.PlayerId, out var list))
            {
                list = new List<WorldMotionRequest>();
                motionByPlayer.Add(request.PlayerId, list);
            }
            if (list.Any(r => r.Kind == request.Kind))
                throw new ArgumentException("Duplicate motion source update.",
                    nameof(motionRequests));
            list.Add(request);
        }
        foreach (List<WorldMotionRequest> list in motionByPlayer.Values)
            list.Sort((a, b) => a.Kind.CompareTo(b.Kind));

        // Prevalidate all state operations before any motor or clock work.
        var requestedByPlayer = new Dictionary<int, List<CharacterStateRequest>>();
        foreach (CharacterStateRequest request in stateRequests)
        {
            if (!_players.ContainsKey(request.PlayerId))
                throw new ArgumentOutOfRangeException(nameof(stateRequests),
                    $"Unknown state-request player: {request.PlayerId}.");
            if (!Enum.IsDefined(request.Kind))
                throw new ArgumentOutOfRangeException(nameof(stateRequests), "Invalid state operation.");
            if ((request.Kind == CharacterStateRequestKind.ApplyStun) != (request.DurationTicks > 0) ||
                (request.Kind != CharacterStateRequestKind.ApplyStun && request.DurationTicks != 0))
                throw new ArgumentOutOfRangeException(nameof(stateRequests),
                    "Stun duration must be positive only for ApplyStun.");
            if (!requestedByPlayer.TryGetValue(request.PlayerId, out var list))
            {
                list = new List<CharacterStateRequest>();
                requestedByPlayer.Add(request.PlayerId, list);
            }
            if (list.Any(r => r.Kind == request.Kind))
                throw new ArgumentException("Duplicate state operation for one player.", nameof(stateRequests));
            list.Add(request);
        }
        foreach (var list in requestedByPlayer.Values)
        {
            if ((list.Any(r => r.Kind == CharacterStateRequestKind.BeginAction) &&
                 list.Any(r => r.Kind == CharacterStateRequestKind.EndAction)) ||
                (list.Any(r => r.Kind == CharacterStateRequestKind.ApplyStun) &&
                 list.Any(r => r.Kind == CharacterStateRequestKind.ClearStun)))
                throw new ArgumentException("Contradictory state operations in one tick.", nameof(stateRequests));
            list.Sort((a, b) => StateOperationOrder(a.Kind).CompareTo(StateOperationOrder(b.Kind)));
        }

        // Deterministically resolve simultaneous ability requests by player,
        // then cancellations before activations, then stable ability ID.
        // Invalid input fails BEFORE any fork is stepped or mutated.
        var abilityByPlayer = new Dictionary<int, List<CharacterAbilityRequest>>();
        foreach (CharacterAbilityRequest request in abilityRequests)
        {
            if (!_players.ContainsKey(request.PlayerId))
                throw new ArgumentOutOfRangeException(nameof(abilityRequests),
                    $"Unknown ability-request player: {request.PlayerId}.");
            if (!Enum.IsDefined(request.AbilityId) || !Enum.IsDefined(request.Kind))
                throw new ArgumentOutOfRangeException(nameof(abilityRequests),
                    "Unknown ability or operation.");
            if (!abilityByPlayer.TryGetValue(request.PlayerId, out var list))
            {
                list = new List<CharacterAbilityRequest>();
                abilityByPlayer.Add(request.PlayerId, list);
            }
            if (list.Any(r => r.AbilityId == request.AbilityId))
                throw new ArgumentException("Duplicate/contradictory ability request for player and ability.",
                    nameof(abilityRequests));
            list.Add(request);
        }
        foreach (List<CharacterAbilityRequest> list in abilityByPlayer.Values)
            list.Sort((a, b) =>
            {
                int aOrder = a.Kind == CharacterAbilityRequestKind.Cancel ? 0 : 1;
                int bOrder = b.Kind == CharacterAbilityRequestKind.Cancel ? 0 : 1;
                int order = aOrder.CompareTo(bOrder);
                return order != 0 ? order : a.AbilityId.CompareTo(b.AbilityId);
            });

        if (WorldTick == long.MaxValue)
            throw new OverflowException("World tick counter exhausted.");
        long nextTick = WorldTick + 1;
        double start = WorldTimeSeconds;
        double end = nextTick * FixedStepSeconds;
        if (!double.IsFinite(end) || end <= start ||
            start + (end - start) != end)
            throw new OverflowException("World time cannot advance at finite precision.");

        var staged = new List<(PlayerState Player, CharacterMovementController Motor,
            BufferedJumpInput Buffer, CharacterStateMachine States,
            CharacterAbilityCoordinator Abilities, WorldPlayerMotion Motion,
            bool JumpStarted)>(_players.Count);
        var events = new List<WorldGameplayEvent>(_pendingActions.Count + _players.Count * 3);
        void Emit(WorldGameplayEventKind kind, int playerId, Vector2D position,
            int? colliderId = null, int? triggerId = null, double intensity = 0.0,
            WorldCharacterStateChange? stateChange = null,
            CharacterStateRequestKind? stateRequestKind = null,
            CharacterStateRejection? rejection = null,
            CharacterAbilityId? abilityId = null,
            CharacterAbilityRejection? abilityRejection = null,
            CharacterAbilityEndReason? abilityEndReason = null,
            CharacterCrushEvent? crush = null)
        {
            events.Add(new WorldGameplayEvent(WorldInstanceId, nextTick,
                events.Count, kind, playerId, position, colliderId, triggerId, intensity,
                stateChange, stateRequestKind, rejection,
                abilityId, abilityRejection, abilityEndReason) { Crush = crush });
        }
        void EmitStateDiff(int playerId, Vector2D position,
            CharacterStateSnapshot before, CharacterStateSnapshot after)
        {
            if (before.Locomotion != after.Locomotion)
                Emit(WorldGameplayEventKind.CharacterStateChanged, playerId, position,
                    stateChange: new WorldCharacterStateChange(CharacterStateLane.Locomotion,
                        before.Locomotion.ToString(), after.Locomotion.ToString()));
            if (before.Action != after.Action)
                Emit(WorldGameplayEventKind.CharacterStateChanged, playerId, position,
                    stateChange: new WorldCharacterStateChange(CharacterStateLane.Action,
                        before.Action.ToString(), after.Action.ToString()));
            if (before.Condition != after.Condition)
                Emit(WorldGameplayEventKind.CharacterStateChanged, playerId, position,
                    stateChange: new WorldCharacterStateChange(CharacterStateLane.Condition,
                        before.Condition.ToString(), after.Condition.ToString()));
        }
        // Out-of-step world operations (join/remove/respawn) have already
        // committed. Report them only with the NEXT successful world tick.
        foreach (PendingWorldEvent action in _pendingActions)
            Emit(action.Kind, action.PlayerId, action.Position);
        var nextInside = new Dictionary<int, HashSet<int>>();
        var nextWalls = new Dictionary<int, HashSet<int>>();
        var stagedLife=new Dictionary<int,WorldLifeStatus>();
        var props=CharacterSolidProps();
        var tickGeometry=CharacterGeometry(props);
        // Trace established supports AND potential top landings. An airborne
        // character must sweep against the moving top, not last tick's box.
        // Preview exclusions are provisional; only a verified top interaction
        // removes a pair from the final fixed character-boundary pass.
        var riderPairs=new HashSet<PhysicalBodyPair>();
        var previewPairs=new HashSet<PhysicalBodyPair>();
        var riderSupport=new Dictionary<int,int>();
        var landingCandidates=new Dictionary<int,List<int>>();
        var supportPaths=new Dictionary<int,MovingPlatformTrajectory>();
        var expectedSupports=new Dictionary<int,Aabb>();
        if(DynamicBodies is { } propWorld&&props.Length>0)
        {
            foreach(var player in _players.Values)
            {
                if(player.Life.IsDead||_pendingDeaths.Contains(player.Id))continue;
                var motor=player.Motor;
                var characterId=new PhysicalBodyId(PhysicalBodyKind.KinematicCharacter,player.CharacterId);
                foreach(var prop in props)
                {
                    if(motor.IsGrounded&&!motor.IsJumping&&
                        MovingPlatformRideSolver.IsSupported(motor.Bounds,prop.Bounds))
                    {
                        riderSupport.Add(player.Id,prop.Id.LocalId);
                        previewPairs.Add(new PhysicalBodyPair(prop.Id,characterId));
                        break;
                    }
                    // A body below an airborne character is a possible landing
                    // surface, including when the character steers onto it.
                    // Do not reinterpret grounded side contacts as support.
                    if(!motor.IsGrounded&&motor.Bounds.Bottom>=prop.Bounds.Top)
                    {
                        if(!landingCandidates.TryGetValue(player.Id,out var candidates))
                            landingCandidates.Add(player.Id,candidates=new List<int>());
                        candidates.Add(prop.Id.LocalId);
                        previewPairs.Add(new PhysicalBodyPair(prop.Id,characterId));
                    }
                }
            }
            if(previewPairs.Count>0)
            {
                var ids=riderSupport.Values.Concat(landingCandidates.Values.SelectMany(c=>c));
                var traces=propWorld.PreviewSupportMotion(end-start,_gravityAcceleration,LiveCharacterBounds(),_platforms,start,previewPairs,ids);
                foreach(var entry in traces)
                {
                    if(entry.Value.Count<2)continue; // Destroyed during the preview.
                    // Established supports retain the moving view even when
                    // stationary. Unmoving prospective surfaces can use the
                    // original static collision path without a new rider pair.
                    if(!riderSupport.ContainsValue(entry.Key)&&
                        entry.Value.All(point=>point.Bounds==entry.Value[0].Bounds))continue;
                    supportPaths.Add(entry.Key,MovingPlatformTrajectory.FromTickMotion(PropColliderId(entry.Key),entry.Value));
                }
            }
        }
        var pushes=new Dictionary<int,Vector2D>();
        void ClearPlayerContacts(PlayerState player,Vector2D position)
        {
            if (_triggerInside.TryGetValue(player.Id,out var previous))
                foreach(int triggerId in previous.OrderBy(id=>id))
                    Emit(WorldGameplayEventKind.TriggerExited,player.Id,position,triggerId:triggerId);
            nextInside.Add(player.Id,new HashSet<int>());nextWalls.Add(player.Id,new HashSet<int>());
        }
        void StageLife(PlayerState player,WorldLifeStatus life,bool newlyDead,Vector2D position)
        {
            if(newlyDead) Emit(WorldGameplayEventKind.PlayerDied,player.Id,position);
            ClearPlayerContacts(player,position);
            CharacterMovementController? respawn=life.CanAttempt(nextTick)?TryCreateSafeRespawn(player,end):null;
            if(respawn is not null)
            {
                Emit(WorldGameplayEventKind.PlayerRespawned,player.Id,respawn.Bounds.Center);
                stagedLife.Add(player.Id,life.Revive(true));
                staged.Add((player,respawn,new BufferedJumpInput(_jumpTimingConstraints),CreateCharacterStates(respawn,end),
                    new CharacterAbilityCoordinator(),new WorldPlayerMotion(),false));
            }
            else
            {
                stagedLife.Add(player.Id,life);
                // Retain the last valid motor as a diagnostic/camera anchor, but do not step it again.
                // Dead bodies are excluded from physical/spatial snapshots and authored rendering.
                if(newlyDead) staged.Add((player,player.Motor,new BufferedJumpInput(_jumpTimingConstraints),
                    CreateCharacterStates(player.Motor,start),new CharacterAbilityCoordinator(),new WorldPlayerMotion(),false));
            }
        }
        foreach (PlayerState player in _players.Values)
        {
            if(player.Life.IsDead) { StageLife(player,player.Life,false,player.Motor.Bounds.Center);continue; }
            if(_pendingDeaths.Contains(player.Id)) { StageLife(player,player.Life.Die(nextTick,RespawnSettings),true,player.Motor.Bounds.Center);continue; }
            // Keep staged intent events separate from the accepted outcome.
            int firstPlayerEvent = events.Count;
            WorldPlayerCommand command = inputByPlayer.TryGetValue(player.Id, out var proposed)
                ? proposed
                : new WorldPlayerCommand(player.Id, HorizontalDirection.Neutral);
            CharacterMovementController motor = player.Motor.Fork();
            // A top-landing candidate uses the same continuous sweep and
            // remaining-tick ride as an authored elevator. All other props
            // remain ordinary solid character colliders.
            int? establishedSupport=riderSupport.TryGetValue(player.Id,out int supportBody)&&
                supportPaths.ContainsKey(supportBody)?supportBody:null;
            var movingPropIds=landingCandidates.TryGetValue(player.Id,out var candidates)
                ?candidates.Where(supportPaths.ContainsKey).ToHashSet():new HashSet<int>();
            if(establishedSupport is int establishedId)movingPropIds.Add(establishedId);
            if(movingPropIds.Count>0)
                motor.UseTickMovingGeometry(CharacterGeometry(props.Where(p=>!movingPropIds.Contains(p.Id.LocalId)).ToArray()),
                    _platforms.Concat(movingPropIds.OrderBy(id=>id).Select(id=>supportPaths[id])).ToArray(),
                    establishedSupport is int supportId?supportPaths[supportId].Id:null);
            else if(motor.HasTransientMovingGeometry)
                motor.UseTickMovingGeometry(tickGeometry,_platforms,null);
            else
                motor.UseTickGeometry(tickGeometry);
            BufferedJumpInput buffer = player.JumpTiming.Fork();
            CharacterStateMachine states = player.States.Fork();
            CharacterAbilityCoordinator abilities = player.Abilities.Fork();
            WorldPlayerMotion motion = player.Motion.Fork();
            if (motionByPlayer.TryGetValue(player.Id, out var motionUpdates))
            {
                foreach (WorldMotionRequest update in motionUpdates)
                {
                    motion.Apply(update);
                    Emit(update.Kind == WorldMotionRequestKind.AddHorizontalImpulse
                        ? WorldGameplayEventKind.HorizontalImpulseApplied
                        : WorldGameplayEventKind.EnvironmentMotionChanged,
                        player.Id, motor.Bounds.Center, intensity: update.HorizontalSpeed);
                }
            }
            if (requestedByPlayer.TryGetValue(player.Id, out var requests))
            {
                foreach (CharacterStateRequest request in requests)
                {
                    CharacterStateSnapshot before = states.Snapshot;
                    CharacterStateRejection rejection = states.Apply(request);
                    if (rejection != CharacterStateRejection.None)
                        Emit(WorldGameplayEventKind.CharacterStateRequestDenied, player.Id,
                            motor.Bounds.Center, stateRequestKind: request.Kind, rejection: rejection);
                    else
                        EmitStateDiff(player.Id, motor.Bounds.Center, before, states.Snapshot);
                }
            }
            // A09 condition/state overrides active abilities before any new
            // ability activation is considered. Preserve prior action diffs;
            // events are still staged and unpublished until final commit.
            foreach (CharacterAbilityId interrupted in abilities.InterruptForState(states))
                Emit(WorldGameplayEventKind.AbilityInterrupted, player.Id,
                    motor.Bounds.Center, abilityId: interrupted,
                    abilityEndReason: CharacterAbilityEndReason.Interrupted);
            if (abilityByPlayer.TryGetValue(player.Id, out var abilityRequestsForPlayer))
            {
                foreach (CharacterAbilityRequest request in abilityRequestsForPlayer)
                {
                    CharacterStateSnapshot beforeAbility = states.Snapshot;
                    CharacterAbilityRejection abilityRejection =
                        request.Kind == CharacterAbilityRequestKind.Activate
                            ? abilities.Activate(request.AbilityId, states)
                            : abilities.Cancel(request.AbilityId, states);
                    if (abilityRejection != CharacterAbilityRejection.None)
                        Emit(WorldGameplayEventKind.AbilityRejected, player.Id,
                            motor.Bounds.Center, abilityId: request.AbilityId,
                            abilityRejection: abilityRejection);
                    else
                    {
                        // Pulse's existing action duration is now a real
                        // collision-swept dash. Its direction is captured at
                        // successful activation, not on rejected requests.
                        if (request.AbilityId == CharacterAbilityId.Pulse &&
                            request.Kind == CharacterAbilityRequestKind.Activate)
                        {
                            if (command.Direction != HorizontalDirection.Neutral)
                                motion.Face((int)command.Direction);
                            motion.BeginDash();
                        }
                        Emit(request.Kind == CharacterAbilityRequestKind.Activate
                            ? WorldGameplayEventKind.AbilityActivated
                            : WorldGameplayEventKind.AbilityCancelled,
                            player.Id, motor.Bounds.Center, abilityId: request.AbilityId,
                            abilityEndReason: request.Kind == CharacterAbilityRequestKind.Cancel
                                ? CharacterAbilityEndReason.Cancelled : null);
                    }
                    EmitStateDiff(player.Id, motor.Bounds.Center,
                        beforeAbility, states.Snapshot);
                }
            }

            // Stun gates intent but does NOT erase velocity, teleport, freeze
            // platform motion, or bypass collision integration.
            bool blocked = states.Snapshot.BlocksPlayerInput;
            if (!blocked && command.Direction != HorizontalDirection.Neutral)
                motion.Face((int)command.Direction);
            double extraHorizontalSpeed = motion.ExternalSpeed(
                abilities.HasPrimaryAction, _groundMotion.MaximumSpeed);
            if (!double.IsFinite(extraHorizontalSpeed) ||
                Math.Abs(extraHorizontalSpeed) > 10000.0)
                throw new InvalidOperationException("Combined external motion is out of bounds.");
            // A pre-stun buffered jump must never fire after the input gate
            // closes (including on a landing later in the stun period).
            if (blocked) buffer.Reset();
            // Preserve a real support-loss edge if a previously ridden prop was removed.
            bool wasGrounded = motor.IsGrounded || (player.Motor.HasTransientMovingGeometry && player.Motor.IsGrounded);
            JumpInputPlan plan = buffer.Plan(start, motor.IsGrounded, motor.IsJumping,
                !blocked && command.JumpPressed, command.JumpReleased);
            CharacterControlStepResult step;
            try
            {
                step = motor.StepOnWorldTimeline(
                    start, end, blocked ? HorizontalDirection.Neutral : command.Direction,
                    plan.StartJump, command.JumpReleased || plan.ReleaseOnStart,
                    plan.AuthorizeCoyote, extraHorizontalSpeed);
            }
            catch (CharacterCrushException failure) when (
                failure.Crush.Axis == CharacterCrushAxis.Horizontal &&
                _platforms.Any(p => p.Id == failure.Crush.PushingColliderId && !p.IsPaused(start)) &&
                props.Any(p => PropColliderId(p.Id.LocalId) == failure.Crush.OpposingColliderId &&
                    DynamicBodyWorld.PlatformSolid(p) && p.Settings.CrushResponse == RigidBodyCrushResponse.StopPlatform))
            {
                // A character between the authored wall and a StopPlatform
                // prop is the same obstructed corridor as a prop/character
                // chain in the other order. Retry the whole uncommitted tick
                // with the offending wall paused at its last valid position.
                var prop = props.Single(p => PropColliderId(p.Id.LocalId) == failure.Crush.OpposingColliderId);
                throw new PlatformPropCompressionException(
                    new[] { failure.Crush.PushingColliderId }, new[] { prop.Id.LocalId });
            }
            catch (CharacterCrushException failure) when (
                CrushPolicy == WorldCrushPolicy.Death || (CrushPolicy == WorldCrushPolicy.RespawnAtNamedSpawn &&
                player.SpawnId is not null && _definition is not null))
            {
                // A physical crush is an expected gameplay outcome. Resolve a
                // validated spawn at END time, not at the old world tick.
                // No other player, platform or pending event is committed yet.
                events.RemoveRange(firstPlayerEvent, events.Count - firstPlayerEvent);
                CharacterCrushEvent crush = failure.Crush;
                Emit(WorldGameplayEventKind.PlayerCrushed, player.Id,
                    crush.BodyBounds.Center, colliderId: crush.PushingColliderId, crush: crush);
                if(CrushPolicy==WorldCrushPolicy.Death)
                {
                    StageLife(player,player.Life.Die(nextTick,RespawnSettings),true,crush.BodyBounds.Center);
                    continue;
                }
                CharacterMovementController respawn = CreateCrushRespawn(player, end, failure);
                // Clear prior trigger membership (including wind regions) and
                // wall edges. Destination triggers are evaluated next tick,
                // consistently with the existing manual respawn path.
                if (_triggerInside.TryGetValue(player.Id, out var previousInside))
                    foreach (int triggerId in previousInside.OrderBy(id => id))
                        Emit(WorldGameplayEventKind.TriggerExited, player.Id,
                            respawn.Bounds.Center, triggerId: triggerId);
                Emit(WorldGameplayEventKind.PlayerRespawned, player.Id, respawn.Bounds.Center);
                nextInside.Add(player.Id, new HashSet<int>());
                nextWalls.Add(player.Id, new HashSet<int>());
                staged.Add((player, respawn, new BufferedJumpInput(_jumpTimingConstraints),
                    CreateCharacterStates(respawn, end), new CharacterAbilityCoordinator(),
                    new WorldPlayerMotion(), false));
                continue;
            }
            // The motor swept these props against their chronological motion.
            // A descending prop can vacate space that an airborne character
            // enters BEFORE making top contact. Treating the character's end
            // box as a fixed boundary at the start of the prop pass would
            // manufacture an initial overlap in that case.
            foreach(int id in movingPropIds.OrderBy(id=>id))
            {
                var path=supportPaths[id];
                var initialProp=path.Evaluate(start).Bounds;
                var finalProp=path.Evaluate(end).Bounds;
                bool topContact=establishedSupport==id||motor.MovingSupportId==path.Id||
                    step.Contacts.Any(c=>c.ColliderId==path.Id&&c.Normal.Y>0.5);
                bool descendingClearance=landingCandidates.TryGetValue(player.Id,out var potentialLandings)&&
                    potentialLandings.Contains(id)&&
                    player.Motor.Bounds.Bottom>=initialProp.Top&&
                    finalProp.Top<initialProp.Top&&step.Bounds.Bottom>=finalProp.Top&&
                    AabbCollisionDetector.Overlaps(step.Bounds,initialProp)&&
                    !step.Contacts.Any(c=>c.ColliderId==path.Id&&c.Normal.Y<=0.5);
                if(!topContact&&!descendingClearance)continue;
                // This is a solver-boundary exemption for this tick, NOT a
                // grounded-state change. The prop pass still verifies the
                // predicted endpoint and the actual final pair separation.
                riderPairs.Add(new PhysicalBodyPair(new PhysicalBodyId(PhysicalBodyKind.Dynamic,id),
                    new PhysicalBodyId(PhysicalBodyKind.KinematicCharacter,player.CharacterId)));
                expectedSupports[id]=finalProp;
            }
            // Push only through a real horizontal face in the accepted motor outcome.
            // Sum character intent in deterministic player order; clamp per prop to the
            // configured force per character. No force reaches a body across empty space.
            if(!blocked&&command.Direction!=HorizontalDirection.Neutral&&CharacterPushForce>0)
            {
                int direction=(int)command.Direction;
                foreach(var prop in props)
                {
                    var a=step.Bounds;var b=prop.Bounds;
                    if(a.Bottom>=b.Top||a.Top<=b.Bottom)continue;
                    double gap=direction>0?b.Left-a.Right:a.Left-b.Right;
                    if(gap<0||gap>1e-8)continue;
                    pushes.TryGetValue(prop.Id.LocalId,out var prior);
                    pushes[prop.Id.LocalId]=prior+new Vector2D(direction*CharacterPushForce,0);
                }
            }
            buffer.Commit(plan, end, wasGrounded, step.IsGrounded, step.JumpStarted);
            CharacterStateSnapshot beforeCompletion = states.Snapshot;
            states.CompleteTick(step.IsGrounded, RelativeStateVelocity(motor, end));
            motion.CompleteTick();
            foreach (CharacterAbilityId completed in abilities.CompleteTick(states))
                Emit(WorldGameplayEventKind.AbilityCompleted, player.Id,
                    step.Bounds.Center, abilityId: completed,
                    abilityEndReason: CharacterAbilityEndReason.Completed);
            EmitStateDiff(player.Id, step.Bounds.Center, beforeCompletion, states.Snapshot);
            // Derive semantic events from ACCEPTED motor outcomes only. In
            // particular, pressing Jump does not imply JumpStarted.
            if (step.JumpStarted)
                Emit(WorldGameplayEventKind.JumpStarted, player.Id, step.Bounds.Center);
            if (wasGrounded && !step.IsGrounded)
                Emit(WorldGameplayEventKind.LeftGround, player.Id, step.Bounds.Center);
            if (!wasGrounded && step.IsGrounded)
            {
                CharacterControlContact? floorContact = step.Contacts
                    .Where(c => c.Normal.Y > 0.5)
                    .OrderBy(c => c.TimeWithinStepSeconds)
                    .ThenBy(c => c.ColliderId)
                    .Select(c => (CharacterControlContact?)c).FirstOrDefault();
                double speed = floorContact is CharacterControlContact floor
                    ? Math.Max(0.0, -floor.IncomingVelocity.Y) : 0.0;
                Emit(WorldGameplayEventKind.Landed, player.Id,
                    step.Bounds.Center, floorContact?.ColliderId, intensity: speed);
            }
            // WallHit is an edge event per player/collider, not a sound on
            // every fixed tick while the player continuously pushes a wall.
            var touchingWalls = new HashSet<int>();
            foreach (CharacterControlContact contact in step.Contacts
                .Where(c => Math.Abs(c.Normal.X) > 0.5)
                .OrderBy(c => c.TimeWithinStepSeconds)
                .ThenBy(c => c.ColliderId))
            {
                touchingWalls.Add(contact.ColliderId);
                if ((!_wallContact.TryGetValue(player.Id, out var previousWalls) ||
                     !previousWalls.Contains(contact.ColliderId)) &&
                    !events.Any(e => e.Kind == WorldGameplayEventKind.WallHit &&
                        e.PlayerId == player.Id && e.ColliderId == contact.ColliderId))
                    Emit(WorldGameplayEventKind.WallHit, player.Id, step.Bounds.Center,
                        contact.ColliderId, intensity: Math.Abs(contact.IncomingVelocity.X));
            }
            nextWalls.Add(player.Id, touchingWalls);
            // Static authored triggers use end-of-tick AABB overlap. They are
            // entry/exit signals, not physical collision or per-tick polling.
            var inside = new HashSet<int>();
            foreach (WorldTriggerDefinition trigger in TriggerCandidates(step.Bounds,player.Id))
            {
                if (Intersects(step.Bounds, trigger.Bounds))
                    inside.Add(trigger.Id);
                bool before = _triggerInside.TryGetValue(player.Id, out var previous) &&
                    previous.Contains(trigger.Id);
                bool after = inside.Contains(trigger.Id);
                if (before != after)
                    Emit(after ? WorldGameplayEventKind.TriggerEntered :
                        WorldGameplayEventKind.TriggerExited, player.Id,
                        step.Bounds.Center, triggerId: trigger.Id);
            }
            nextInside.Add(player.Id, inside);
            staged.Add((player, motor, buffer, states, abilities,
                motion, step.JumpStarted));
        }
        // Integrator stages all rigid bodies before committing. Failure leaves player forks and world tick uncommitted.
        if(DynamicBodies is { } dynamics)
        {
            var boundaries=staged.Where(p=>!(stagedLife.TryGetValue(p.Player.Id,out var life)?life:p.Player.Life).IsDead)
                .Select(p=>new PhysicalBodySnapshot(new(PhysicalBodyKind.KinematicCharacter,p.Player.CharacterId),p.Motor.Bounds,p.Motor.Velocity)).ToArray();
            dynamics.Advance(end-start,_gravityAcceleration,boundaries,pushes,_platforms,start,
                riderPairs.Count>0?riderPairs:null,expectedSupports.Count>0?expectedSupports:null);
        }
        foreach (var accepted in staged)
        {
            accepted.Player.Motor = accepted.Motor;
            accepted.Player.JumpTiming = accepted.Buffer;
            accepted.Player.States = accepted.States;
            accepted.Player.Abilities = accepted.Abilities;
            accepted.Player.Motion = accepted.Motion;
            accepted.Player.LastJumpStarted = accepted.JumpStarted;
        }
        foreach(var entry in stagedLife) _players[entry.Key].Life=entry.Value;
        _pendingDeaths.Clear();
        WorldTick = nextTick;
        _committedSnapshot = null;
        _triggerInside.Clear();
        foreach (var pair in nextInside) _triggerInside.Add(pair.Key, pair.Value);
        _wallContact.Clear();
        foreach (var pair in nextWalls) _wallContact.Add(pair.Key, pair.Value);
        _pendingActions.Clear();
        return Snapshot() with { Events = Array.AsReadOnly(events.ToArray()) };
    }

    private static int StateOperationOrder(CharacterStateRequestKind kind) => kind switch
    {
        CharacterStateRequestKind.ApplyStun => 0,
        CharacterStateRequestKind.ClearStun => 1,
        CharacterStateRequestKind.EndAction => 2,
        CharacterStateRequestKind.BeginAction => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    private static bool Intersects(Aabb a, Aabb b) =>
        a.Left < b.Right && a.Right > b.Left &&
        a.Bottom < b.Top && a.Top > b.Bottom;

    // A player riding a moving platform is GroundedIdle when standing still
    // relative to that platform; its world-space transport is not walking.
    private CharacterStateMachine CreateCharacterStates(CharacterMovementController motor) =>
        CreateCharacterStates(motor, WorldTimeSeconds);

    private CharacterStateMachine CreateCharacterStates(CharacterMovementController motor, double time) =>
        new(motor.IsGrounded, RelativeStateVelocity(motor, time));

    private Vector2D RelativeStateVelocity(CharacterMovementController motor, double time)
    {
        Vector2D worldVelocity = motor.Velocity;
        // Wind/current/dash transport is not WALKING. The locomotion state
        // tracks player-generated velocity separately from external motion.
        if (motor.MovingSupportId is null)
            return new Vector2D(worldVelocity.X - motor.ExternalHorizontalSpeed,
                worldVelocity.Y);
        Vector2D supportVelocity = motor.MovingSupportVelocity(time);
        return new Vector2D(worldVelocity.X - supportVelocity.X -
            motor.ExternalHorizontalSpeed, worldVelocity.Y - supportVelocity.Y);
    }

    private CharacterMovementController? TryCreateSafeRespawn(PlayerState player,double end)
    {
        IEnumerable<Aabb> candidates = _definition is not null&&player.SpawnId is not null
            ? new[]{player.SpawnId,player.InitialSpawnId}.OfType<string>().Distinct(StringComparer.Ordinal).Select(id=>ResolveRuntimeSpawn(id,end))
            : new[]{player.InitialBounds};
        foreach(var bounds in candidates)
        {
            if(_staticColliders.Any(c=>AabbCollisionDetector.Overlaps(bounds,c.Bounds))||
               _platforms.Any(p=>AabbCollisionDetector.Overlaps(bounds,p.Evaluate(end).Bounds))||
               CharacterSolidProps().Any(p=>AabbCollisionDetector.Overlaps(bounds,p.Bounds)))continue;
            return CreateMotor(bounds,end);
        }
        // Character/character collisions remain deferred.
        return null;
    }

    private CharacterMovementController CreateCrushRespawn(PlayerState player,double end,CharacterCrushException failure)
        => TryCreateSafeRespawn(player,end)??throw new InvalidOperationException(
            $"No safe authored respawn for player {player.Id} at world time {end:R} " +
            $"(checkpoint '{player.SpawnId}', initial spawn '{player.InitialSpawnId}').",failure);

    private CharacterMovementController CreateMotor(Aabb bounds) =>
        CreateMotor(bounds, WorldTimeSeconds);

    private CharacterMovementController CreateMotor(Aabb bounds, double time) => new(
        bounds, CharacterGeometry(CharacterSolidProps()), _groundMotion, _airMotion,
        _jumpFlight, _gravityAcceleration,
        releaseConstraints: _releaseConstraints,
        descentConstraints: _descentConstraints,
        movingPlatform: _platforms.FirstOrDefault(),
        additionalMovingPlatforms: _platforms.Skip(1).ToArray(),
        faultPolicy: _faultPolicy,
        assistancePolicy: _assistancePolicy,
        initialWorldTimeSeconds: time);

    private WorldPlayerSnapshot ToSnapshot(PlayerState player)
    {
        CharacterMovementController motor = player.Motor;
        bool dead=player.Life.IsDead;
        return new WorldPlayerSnapshot(player.Id, motor.Bounds, dead?Vector2D.Zero:motor.Velocity,
            !dead&&motor.IsGrounded, !dead&&motor.IsJumping, dead?null:motor.MovingSupportId,
            motor.JumpsStarted, !dead&&player.LastJumpStarted)
        {
            Life=player.Life,
            State = player.States.Snapshot,
            Motion = player.Motion.Snapshot(player.Abilities.HasPrimaryAction,
                _groundMotion.MaximumSpeed)
        };
    }
}
