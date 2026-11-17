using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Foundation.Presentation.Particles;

/// <summary>Independent deterministic PRNG; never reads gameplay or global Random.</summary>
public struct ParticleRandom
{
    private ulong _state;
    public ParticleRandom(ulong seed) => _state = seed;

    public double NextUnit()
    {
        // SplitMix64: well-defined unsigned overflow, repeatable across runtimes.
        unchecked
        {
            ulong z = (_state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return ((z ^ (z >> 31)) >> 11) * (1.0 / 9007199254740992.0);
        }
    }

    public double Sample(ParticleRange range) => range.Lerp(NextUnit());
}

/// <summary>
/// Per-instance burst and continuous emission. An emitter only schedules new
/// particles; ParticleSystem.Update advances all existing particles separately.
/// Call from presentation Update: emitter.Update(origin, dt), then system.Update(dt).
/// </summary>
public sealed class ParticleEmitter
{
    private readonly ParticleSystem _system;
    private readonly ulong _seed;
    private ParticleRandom _random;
    private double _remainder;

    public ParticleEmitterDefinition Definition { get; }
    public bool IsEmitting { get; private set; } = true;
    public double FractionalEmissionCarry => _remainder;

    public ParticleEmitter(ParticleSystem system, ParticleEmitterDefinition definition,
        ulong seed = 1)
    {
        _system = system ?? throw new ArgumentNullException(nameof(system));
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Definition.Validate();
        _seed = seed;
        _random = new ParticleRandom(seed);
    }

    /// <summary>Request a one-shot burst (bounded by the per-request emission cap).</summary>
    public int Burst(Vector2D origin)
    {
        if (!IsEmitting || Definition.BurstCount == 0) return 0;
        int count = Math.Min(Definition.BurstCount, Definition.MaxSpawnsPerUpdate);
        _system.RecordSkippedEmissions(Definition.BurstCount - count);
        return SpawnMany(origin, count);
    }

    /// <summary>
    /// Fractional time emission with bounded catch-up. Excess scheduled work is
    /// deliberately dropped (counted), not carried into later frames.
    /// The normal fractional remainder is retained for consistent small timesteps.
    /// </summary>
    public int Update(Vector2D origin, double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!IsEmitting || Definition.RatePerSecond == 0 || deltaSeconds == 0)
            return 0;

        double bounded = Math.Min(deltaSeconds, Definition.MaxEmissionDeltaSeconds);
        double scheduledFloat = _remainder + Definition.RatePerSecond * bounded;
        long scheduled = BoundedFloor(scheduledFloat);
        _remainder = scheduledFloat - scheduled;
        int requested = (int)Math.Min(scheduled, Definition.MaxSpawnsPerUpdate);

        long fullSchedule = BoundedFloor(scheduledFloat +
            Definition.RatePerSecond * (deltaSeconds - bounded));
        _system.RecordSkippedEmissions(Math.Max(0, fullSchedule - requested));
        return SpawnMany(origin, requested);
    }

    public void Stop() => IsEmitting = false;

    /// <summary>Resume emission; optionally replay exactly from the initial random seed.</summary>
    public void Restart(bool resetRandom = false)
    {
        IsEmitting = true;
        _remainder = 0;
        if (resetRandom) _random = new ParticleRandom(_seed);
    }

    private int SpawnMany(Vector2D origin, int count)
    {
        int spawned = 0;
        for (int i = 0; i < count; i++)
        {
            if (!TrySampleSpawn(origin, out ParticleSpawn spawn))
            {
                _system.RejectGeneratedInvalidSpawn();
                continue;
            }
            if (_system.TrySpawn(spawn)) spawned++;
        }
        return spawned;
    }

    private bool TrySampleSpawn(Vector2D origin, out ParticleSpawn spawn)
    {
        double x = origin.X + (Definition.Shape == ParticleSpawnShape.Box
            ? _random.Sample(Definition.SpawnOffsetX) : Definition.SpawnOffsetX.Min);
        double y = origin.Y + (Definition.Shape == ParticleSpawnShape.Box
            ? _random.Sample(Definition.SpawnOffsetY) : Definition.SpawnOffsetY.Min);
        if (Definition.Shape == ParticleSpawnShape.Disk)
        {
            double distance = Math.Sqrt(_random.NextUnit()) * Definition.SpawnRadius;
            double angle = _random.NextUnit() * (Math.PI * 2);
            x += Math.Cos(angle) * distance;
            y += Math.Sin(angle) * distance;
        }
        double direction = _random.Sample(Definition.DirectionRadians);
        double speed = _random.Sample(Definition.Speed);
        double vx = Math.Cos(direction) * speed;
        double vy = Math.Sin(direction) * speed;
        double life = _random.Sample(Definition.LifetimeSeconds);
        double startSize = _random.Sample(Definition.StartSize);
        double endSize = _random.Sample(Definition.EndSize);
        double rotation = _random.Sample(Definition.RotationRadians);
        double spin = _random.Sample(Definition.AngularVelocityRadiansPerSecond);
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(vx) ||
            !double.IsFinite(vy) || !double.IsFinite(life) || !double.IsFinite(startSize) ||
            !double.IsFinite(endSize) || !double.IsFinite(rotation) || !double.IsFinite(spin))
        {
            spawn = default;
            return false;
        }
        spawn = new ParticleSpawn(new Vector2D(x, y), new Vector2D(vx, vy),
            new Vector2D(Definition.AccelerationX, Definition.AccelerationY),
            life, startSize, endSize, Definition.StartColor, Definition.EndColor,
            rotation, spin);
        return true;
    }

    private static long BoundedFloor(double value)
    {
        if (double.IsPositiveInfinity(value) || value >= long.MaxValue)
            return long.MaxValue;
        return Math.Max(0, (long)Math.Floor(value + 1e-10));
    }
}
