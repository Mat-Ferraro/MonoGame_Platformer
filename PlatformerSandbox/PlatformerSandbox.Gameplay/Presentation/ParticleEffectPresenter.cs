using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation.Particles;
using PlatformerSandbox.Gameplay.Abilities;
using PlatformerSandbox.Gameplay.World;

namespace PlatformerSandbox.Gameplay.Presentation;

/// <summary>Semantic UI feedback from an accepted scene-navigation action.</summary>
public enum ParticleUiFeedback { Confirm, Back, Navigate }

/// <summary>
/// P04 one-way, headless integration boundary. Only consumes successful,
/// committed WorldGameplayEvent/WorldStepResult data and owns cosmetic runtimes.
/// It never reads inputs or writes to WorldSession, collision or movement.
/// </summary>
public sealed class ParticleEffectPresenter
{
    private readonly ParticleCompositeCatalog _catalog;
    private readonly ParticleGameplayBindings _bindings;
    private readonly SortedDictionary<int, ParticleCompositeRuntime> _perPlayer = new();
    private readonly Dictionary<int, ParticleCompositeInstance> _pulseTrails = new();
    private readonly ParticleCompositeRuntime _scene;
    private ParticleCompositeInstance? _ambient;
    private readonly ParticleCompositeRuntime _ui;
    private readonly HashSet<(long World, long Tick, int Sequence)> _seen = new();
    private readonly Queue<(long World, long Tick, int Sequence)> _order = new();
    private const int DedupCapacity = 1024;
    private long _activeWorld;
    private long _seedEpoch;
    private bool _enabled = true;

    public bool Enabled => _enabled;
    public long ActiveWorldInstanceId => _activeWorld;
    public int ActiveWorldEffects => _scene.ActiveInstances + _perPlayer.Values.Sum(r => r.ActiveInstances);
    public int ActiveUiEffects => _ui.ActiveInstances;
    public long DroppedWorldEffects => _scene.DroppedAtCapacity +
        _perPlayer.Values.Sum(r => r.DroppedAtCapacity);
    public IReadOnlyList<ParticleCompositeInstance> SceneInstances => _scene.Instances;
    public IReadOnlyList<ParticleCompositeInstance> UiInstances => _ui.Instances;

    public IEnumerable<ParticleCompositeInstance> WorldInstances
    {
        get
        {
            foreach (ParticleCompositeInstance effect in _scene.Instances)
                yield return effect;
            foreach (ParticleCompositeRuntime runtime in _perPlayer.Values)
                foreach (ParticleCompositeInstance effect in runtime.Instances)
                    yield return effect;
        }
    }

    public ParticleEffectPresenter(ParticleCompositeCatalog catalog,
        ParticleGameplayBindings bindings, ParticleFaultPolicy policy = ParticleFaultPolicy.Strict)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        bindings.Validate(catalog);
        _scene = new ParticleCompositeRuntime(catalog, maxInstances: 4,
            seed: 0x82A53UL, faultPolicy: policy);
        _ui = new ParticleCompositeRuntime(catalog, maxInstances: 12,
            seed: 0xA91BCUL, faultPolicy: policy);
        FaultPolicy = policy;
    }

    public ParticleFaultPolicy FaultPolicy { get; }

    /// <summary>Begin a newly committed world. UI belongs to application scope.</summary>
    public void EnterWorld(long worldInstanceId, Vector2D? ambientOrigin = null)
    {
        if (worldInstanceId <= 0)
            throw new ArgumentOutOfRangeException(nameof(worldInstanceId));
        ResetWorld();
        _activeWorld = worldInstanceId;
        if (_enabled && ambientOrigin is Vector2D origin &&
            !string.IsNullOrEmpty(_bindings.SceneAmbient))
            _scene.TryPlay(_bindings.SceneAmbient, origin, out _ambient);
    }

    /// <summary>Leave a level or return to title, preserving UI cosmetics.</summary>
    public void LeaveWorld()
    {
        ResetWorld();
        _activeWorld = 0;
    }

    private void ResetWorld()
    {
        _scene.Clear();
        _ambient = null;
        foreach (ParticleCompositeRuntime player in _perPlayer.Values) player.Clear();
        _perPlayer.Clear();
        _pulseTrails.Clear();
        _seen.Clear();
        _order.Clear();
        _seedEpoch++;
    }

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        if (!enabled)
        {
            ResetWorld();
            _ui.Clear();
        }
    }

    /// <summary>Consume one committed event; duplicate, old-world and denied events are inert.</summary>
    public void Present(WorldGameplayEvent e)
    {
        if (!_enabled || _activeWorld == 0 || e.WorldInstanceId != _activeWorld ||
            e.PlayerId <= 0)
            return;
        var key = (e.WorldInstanceId, e.WorldTick, e.Sequence);
        if (!_seen.Add(key)) return;
        _order.Enqueue(key);
        if (_order.Count > DedupCapacity)
            _seen.Remove(_order.Dequeue());

        switch (e.Kind)
        {
            case WorldGameplayEventKind.PlayerRemoved:
            case WorldGameplayEventKind.PlayerDied:
                ClearPlayer(e.PlayerId);
                break;
            case WorldGameplayEventKind.PlayerRespawned:
                ClearPlayer(e.PlayerId);
                Play(e.PlayerId, _bindings.PlayerRespawned, e.Position);
                break;
            case WorldGameplayEventKind.JumpStarted:
                Play(e.PlayerId, _bindings.JumpStarted, e.Position);
                break;
            case WorldGameplayEventKind.Landed:
                Play(e.PlayerId, _bindings.Landed, e.Position);
                break;
            case WorldGameplayEventKind.WallHit:
                Play(e.PlayerId, _bindings.WallHit, e.Position);
                break;
            case WorldGameplayEventKind.AbilityActivated when e.AbilityId == CharacterAbilityId.Pulse:
                if (_pulseTrails.Remove(e.PlayerId, out ParticleCompositeInstance? old))
                    old.Stop();
                ParticleCompositeInstance? trail = Play(e.PlayerId, _bindings.PulseActivated, e.Position);
                if (trail is not null) _pulseTrails[e.PlayerId] = trail;
                break;
            case WorldGameplayEventKind.AbilityCompleted or
                 WorldGameplayEventKind.AbilityCancelled or
                 WorldGameplayEventKind.AbilityInterrupted when e.AbilityId == CharacterAbilityId.Pulse:
                if (_pulseTrails.Remove(e.PlayerId, out ParticleCompositeInstance? active))
                    active.Stop();
                break;
        }
    }

    /// <summary>
    /// Call only after WorldSession.Step commits. Moving a trail changes future
    /// emissions; already-spawned particles remain at their old world positions.
    /// </summary>
    public void PresentCommitted(WorldStepResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        foreach (WorldGameplayEvent e in result.Events) Present(e);
        foreach (WorldPlayerSnapshot player in result.Players)
        {
            if (player.PlayerId == 1)
                _ambient?.MoveSource(player.Bounds.Center);
            if (_pulseTrails.TryGetValue(player.PlayerId, out ParticleCompositeInstance? trail))
                trail.MoveSource(player.Bounds.Center);
        }
        // A removed player has already been cleared by its PlayerRemoved event.
    }

    public void PresentUi(ParticleUiFeedback kind, Vector2D screenPosition)
    {
        if (!_enabled) return;
        string id = kind switch
        {
            ParticleUiFeedback.Confirm => _bindings.UiConfirm,
            ParticleUiFeedback.Back => _bindings.UiBack,
            ParticleUiFeedback.Navigate => _bindings.UiNavigate,
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        if (!string.IsNullOrEmpty(id)) _ui.TryPlay(id, screenPosition, out _);
    }

    public void Update(double deltaSeconds, bool worldPaused)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        _ui.Update(deltaSeconds); // UI never freezes with game world.
        if (!_enabled) return;
        _scene.Update(deltaSeconds, worldPaused);
        foreach (ParticleCompositeRuntime player in _perPlayer.Values)
            player.Update(deltaSeconds, worldPaused);
    }

    private ParticleCompositeInstance? Play(int playerId, string id, Vector2D origin)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (!_perPlayer.TryGetValue(playerId, out ParticleCompositeRuntime? runtime))
        {
            // Scene resets create independent cosmetic seeds; no gameplay RNG.
            runtime = new ParticleCompositeRuntime(_catalog, maxInstances: 12,
                seed: unchecked(0x9B901UL + (ulong)playerId * 7919UL +
                    (ulong)_activeWorld * 104729UL + (ulong)_seedEpoch),
                faultPolicy: FaultPolicy);
            _perPlayer.Add(playerId, runtime);
        }
        return runtime.TryPlay(id, origin, out ParticleCompositeInstance? effect) ? effect : null;
    }

    private void ClearPlayer(int playerId)
    {
        if (_perPlayer.TryGetValue(playerId, out ParticleCompositeRuntime? runtime))
        {
            runtime.Clear();
            _perPlayer.Remove(playerId);
        }
        _pulseTrails.Remove(playerId);
    }
}
