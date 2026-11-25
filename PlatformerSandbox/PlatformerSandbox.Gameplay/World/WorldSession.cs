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
    private readonly Dictionary<int, HashSet<int>> _triggerInside = new();
    private readonly Dictionary<int, HashSet<int>> _wallContact = new();

    private readonly record struct PendingWorldEvent(
        WorldGameplayEventKind Kind, int PlayerId, Vector2D Position);
    private WorldDefinition? _definition;
    private readonly StaticAabbCollider[] _staticColliders;
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

        _staticColliders = staticColliders.ToArray();
        _platforms = movingPlatforms.ToArray();
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
        long worldInstanceId = 1)
        : this((definition ?? throw new ArgumentNullException(nameof(definition))).StaticColliders,
            definition.MovingPlatformTrajectories, groundMotion, airMotion, jumpFlight,
            gravityAcceleration, fixedStepSeconds, initialWorldTick,
            releaseConstraints, descentConstraints, jumpTimingConstraints,
            assistancePolicy, faultPolicy, worldInstanceId)
    {
        _definition = definition;
    }

    public WorldDefinition? Definition => _definition;
    public long WorldInstanceId { get; }

    public double FixedStepSeconds { get; }
    public long WorldTick { get; private set; }
    public double WorldTimeSeconds => WorldTick * FixedStepSeconds;
    public int PlayerCount => _players.Count;
    private IReadOnlyList<StaticAabbCollider>? _staticView;
    private IReadOnlyList<MovingPlatformTrajectory>? _trajectoryView;
    private IReadOnlyList<MovingPlatformSample>? _platformView;
    private long _platformViewTick = -1;
    private WorldStepResult? _committedSnapshot;
    private IReadOnlyList<WorldPlayerSlotInfo>? _slotView;
    private void InvalidatePlayerViews() { _committedSnapshot = null; _slotView = null; }
    public IReadOnlyList<StaticAabbCollider> StaticColliders => EnginePerformanceOptions.Snapshots
        ? (_staticView ??= Array.AsReadOnly(_staticColliders)) : Array.AsReadOnly(_staticColliders);
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
        Aabb bounds = definition.ResolveSpawnBounds(registration.SpawnId, WorldTimeSeconds);
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
        Aabb bounds = _definition.ResolveSpawnBounds(player.SpawnId, WorldTimeSeconds);
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
            CharacterAbilityEndReason? abilityEndReason = null)
        {
            events.Add(new WorldGameplayEvent(WorldInstanceId, nextTick,
                events.Count, kind, playerId, position, colliderId, triggerId, intensity,
                stateChange, stateRequestKind, rejection,
                abilityId, abilityRejection, abilityEndReason));
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
        foreach (PlayerState player in _players.Values)
        {
            WorldPlayerCommand command = inputByPlayer.TryGetValue(player.Id, out var proposed)
                ? proposed
                : new WorldPlayerCommand(player.Id, HorizontalDirection.Neutral);
            CharacterMovementController motor = player.Motor.Fork();
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
            bool wasGrounded = motor.IsGrounded;
            JumpInputPlan plan = buffer.Plan(start, wasGrounded, motor.IsJumping,
                !blocked && command.JumpPressed, command.JumpReleased);
            CharacterControlStepResult step = motor.StepOnWorldTimeline(
                start, end, blocked ? HorizontalDirection.Neutral : command.Direction,
                plan.StartJump, command.JumpReleased || plan.ReleaseOnStart,
                plan.AuthorizeCoyote, extraHorizontalSpeed);
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
            foreach (WorldTriggerDefinition trigger in
                _definition?.Triggers ?? Array.Empty<WorldTriggerDefinition>())
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
        foreach (var accepted in staged)
        {
            accepted.Player.Motor = accepted.Motor;
            accepted.Player.JumpTiming = accepted.Buffer;
            accepted.Player.States = accepted.States;
            accepted.Player.Abilities = accepted.Abilities;
            accepted.Player.Motion = accepted.Motion;
            accepted.Player.LastJumpStarted = accepted.JumpStarted;
        }
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
        new(motor.IsGrounded, RelativeStateVelocity(motor, WorldTimeSeconds));

    private Vector2D RelativeStateVelocity(CharacterMovementController motor, double time)
    {
        Vector2D worldVelocity = motor.Velocity;
        // Wind/current/dash transport is not WALKING. The locomotion state
        // tracks player-generated velocity separately from external motion.
        if (motor.MovingSupportId is not int supportId)
            return new Vector2D(worldVelocity.X - motor.ExternalHorizontalSpeed,
                worldVelocity.Y);
        MovingPlatformTrajectory? platform = _platforms.FirstOrDefault(p => p.Id == supportId);
        if (platform is null)
            throw new InvalidOperationException("Motor refers to an unknown platform support.");
        Vector2D supportVelocity = platform.Evaluate(time).Velocity;
        return new Vector2D(worldVelocity.X - supportVelocity.X -
            motor.ExternalHorizontalSpeed, worldVelocity.Y - supportVelocity.Y);
    }

    private CharacterMovementController CreateMotor(Aabb bounds) => new(
        bounds, _staticColliders, _groundMotion, _airMotion,
        _jumpFlight, _gravityAcceleration,
        releaseConstraints: _releaseConstraints,
        descentConstraints: _descentConstraints,
        movingPlatform: _platforms.FirstOrDefault(),
        additionalMovingPlatforms: _platforms.Skip(1).ToArray(),
        faultPolicy: _faultPolicy,
        assistancePolicy: _assistancePolicy,
        initialWorldTimeSeconds: WorldTimeSeconds);

    private WorldPlayerSnapshot ToSnapshot(PlayerState player)
    {
        CharacterMovementController motor = player.Motor;
        return new WorldPlayerSnapshot(player.Id, motor.Bounds, motor.Velocity,
            motor.IsGrounded, motor.IsJumping, motor.MovingSupportId,
            motor.JumpsStarted, player.LastJumpStarted)
        {
            State = player.States.Snapshot,
            Motion = player.Motion.Snapshot(player.Abilities.HasPrimaryAction,
                _groundMotion.MaximumSpeed)
        };
    }
}
