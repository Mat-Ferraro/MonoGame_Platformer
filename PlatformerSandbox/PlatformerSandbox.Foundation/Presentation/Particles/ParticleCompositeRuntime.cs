using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Foundation.Presentation.Particles;

/// <summary>A distinct per-instance emitter clock and bounded particle pool.</summary>
public sealed class ParticleEffectPart
{
    private readonly ParticleEmitter _emitter;
    private readonly Vector2D _fixedOrigin;
    private bool _started;
    private bool _stopped;
    private double _elapsed;

    public ParticleEffectPartDefinition Definition { get; }
    public ParticleSystem Particles { get; }
    public bool HasStarted => _started;
    public bool IsEmitting => !_stopped && _started && _emitter.IsEmitting &&
        _emitter.Definition.RatePerSecond > 0;

    internal ParticleEffectPart(ParticleEffectPartDefinition part,
        ParticleEmitterDefinition emitter, Vector2D origin, ulong seed,
        ParticleFaultPolicy policy)
    {
        Definition = part;
        _fixedOrigin = origin;
        Particles = new ParticleSystem(part.Capacity, faultPolicy: policy);
        _emitter = new ParticleEmitter(Particles, emitter, seed);
        if (part.DelaySeconds == 0)
            Begin(origin);
    }

    private Vector2D EmitOrigin(Vector2D movingOrigin)
    {
        Vector2D anchor = Definition.Anchor == ParticleEmitterAnchor.Follow
            ? movingOrigin : _fixedOrigin;
        return new Vector2D(anchor.X + Definition.OffsetX,
            anchor.Y + Definition.OffsetY);
    }

    private void Begin(Vector2D movingOrigin)
    {
        if (_started || _stopped) return;
        _started = true;
        _emitter.Burst(EmitOrigin(movingOrigin));
        if (_emitter.Definition.RatePerSecond == 0)
            _emitter.Stop();
    }

    /// <summary>Advances a component's own clock, respecting delay and duration boundaries.</summary>
    internal void Update(Vector2D movingOrigin, double deltaSeconds)
    {
        if (_stopped)
        {
            Particles.Update(deltaSeconds);
            return;
        }
        double previous = _elapsed;
        _elapsed += deltaSeconds;
        double start = Definition.DelaySeconds;
        // A delayed burst happens precisely once as time crosses its start.
        if (!_started && _elapsed >= start)
            Begin(movingOrigin);
        if (_started)
        {
            double end = Definition.DurationSeconds > 0
                ? start + Definition.DurationSeconds : double.PositiveInfinity;
            double emissionDt = Math.Max(0,
                Math.Min(_elapsed, end) - Math.Max(previous, start));
            if (emissionDt > 0)
                _emitter.Update(EmitOrigin(movingOrigin), emissionDt);
            if (_elapsed >= end)
                _emitter.Stop();
        }
        // Newly started particles age only for the time since their emission began.
        double particleDt = Math.Max(0, _elapsed - Math.Max(previous, start));
        if (particleDt > 0)
            Particles.Update(particleDt);
    }

    internal void Stop()
    {
        _stopped = true;
        _emitter.Stop();
    }

    internal void Clear()
    {
        Stop();
        Particles.Clear();
    }

    internal bool IsFinished => Particles.ActiveCount == 0 &&
        (_stopped || (_started && !_emitter.IsEmitting));
}

/// <summary>
/// Independent live instance of one reusable effect definition. A moving
/// source changes future Follow emissions only, not previously spawned particles.
/// </summary>
public sealed class ParticleCompositeInstance
{
    private readonly ParticleEffectPart[] _parts;

    public ParticleCompositeDefinition Definition { get; }
    public IReadOnlyList<ParticleEffectPart> Parts => _parts;
    public Vector2D Origin { get; private set; }
    public bool IsFinished
    {
        get
        {
            foreach (ParticleEffectPart part in _parts)
                if (!part.IsFinished) return false;
            return true;
        }
    }

    internal ParticleCompositeInstance(ParticleCompositeCatalog catalog,
        ParticleCompositeDefinition definition, Vector2D origin, ulong seed,
        ParticleFaultPolicy policy)
    {
        Definition = definition;
        Origin = origin;
        _parts = new ParticleEffectPart[definition.Parts!.Length];
        for (int i = 0; i < _parts.Length; i++)
        {
            ParticleEffectPartDefinition part = definition.Parts[i];
            _parts[i] = new ParticleEffectPart(part, catalog.Emitters.Get(part.EmitterId),
                origin, unchecked(seed + (ulong)i * 0x9E3779B97F4A7C15UL), policy);
        }
    }

    public void MoveSource(Vector2D origin) => Origin = origin;

    internal void Update(double deltaSeconds, bool worldPaused)
    {
        foreach (ParticleEffectPart part in _parts)
            if (!worldPaused || part.Definition.Space == ParticleCoordinateSpace.Screen)
                part.Update(Origin, deltaSeconds);
    }

    public void Stop()
    {
        foreach (ParticleEffectPart part in _parts) part.Stop();
    }

    public void Clear()
    {
        foreach (ParticleEffectPart part in _parts) part.Clear();
    }
}

/// <summary>
/// Headless bounded manager with deterministic per-instance seeds. P03 preview
/// owns this manager; P04 will create scene-owned adapters and event mappings.
/// </summary>
public sealed class ParticleCompositeRuntime
{
    private readonly List<ParticleCompositeInstance> _instances = new();
    private readonly ulong _seed;
    private ulong _playCounter;

    public ParticleCompositeCatalog Catalog { get; }
    public int MaxInstances { get; }
    public ParticleFaultPolicy FaultPolicy { get; }
    public IReadOnlyList<ParticleCompositeInstance> Instances => _instances;
    public int ActiveInstances => _instances.Count;
    public long DroppedAtCapacity { get; private set; }

    public ParticleCompositeRuntime(ParticleCompositeCatalog catalog,
        int maxInstances = 24, ulong seed = 1,
        ParticleFaultPolicy faultPolicy = ParticleFaultPolicy.Strict)
    {
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        if (maxInstances < 1 || maxInstances > 128)
            throw new ArgumentOutOfRangeException(nameof(maxInstances));
        if (!Enum.IsDefined(faultPolicy))
            throw new ArgumentOutOfRangeException(nameof(faultPolicy));
        MaxInstances = maxInstances;
        FaultPolicy = faultPolicy;
        _seed = seed;
    }

    public bool TryPlay(string effectId, Vector2D origin,
        out ParticleCompositeInstance? instance)
    {
        ParticleCompositeDefinition definition = Catalog.Get(effectId);
        if (_instances.Count >= MaxInstances)
        {
            if (DroppedAtCapacity < long.MaxValue) DroppedAtCapacity++;
            instance = null;
            return false;
        }
        instance = new ParticleCompositeInstance(Catalog, definition, origin,
            unchecked(_seed + _playCounter++ * 0xD1B54A32D192ED03UL), FaultPolicy);
        _instances.Add(instance);
        return true;
    }

    public void Update(double deltaSeconds, bool worldPaused = false)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        // Composite clocks are explicitly bounded like the underlying particle pools.
        double dt = Math.Min(deltaSeconds, 0.1);
        for (int i = _instances.Count - 1; i >= 0; i--)
        {
            ParticleCompositeInstance instance = _instances[i];
            instance.Update(dt, worldPaused);
            if (instance.IsFinished)
                _instances.RemoveAt(i);
        }
    }

    public void Clear()
    {
        foreach (ParticleCompositeInstance instance in _instances) instance.Clear();
        _instances.Clear();
    }

    /// <summary>Remove one scene-owned instance immediately when it becomes inactive.</summary>
    public bool Remove(ParticleCompositeInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        if (!_instances.Remove(instance)) return false;
        instance.Clear();
        return true;
    }

    /// <summary>Stop generating particles but allow existing particles to expire.</summary>
    public void StopAll()
    {
        foreach (ParticleCompositeInstance instance in _instances) instance.Stop();
    }
}
