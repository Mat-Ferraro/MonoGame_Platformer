using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Foundation.Presentation.Particles;

/// <summary>
/// Bounded headless particle pool with stable iteration/draw order. Spawn and
/// advance on the presentation thread. Uses no per-particle heap allocations.
/// No MonoGame, gameplay, or collision-system reference.
/// </summary>
public sealed class ParticleSystem
{
    private readonly ParticleState[] _storage;
    private readonly int[] _activeSlots;
    private readonly int[] _freeSlots;
    private int _activeCount;
    private int _freeCount;
    private long _spawned;
    private long _expired;
    private long _droppedCapacity;
    private long _droppedInvalid;
    private long _skippedEmissions;

    public int Capacity => _storage.Length;
    public int ActiveCount => _activeCount;
    public double MaxUpdateDeltaSeconds { get; }
    public ParticleFaultPolicy FaultPolicy { get; }
    public ParticleDiagnostics Diagnostics => new(Capacity, _activeCount,
        _spawned, _expired, _droppedCapacity, _droppedInvalid, _skippedEmissions);

    public ParticleSystem(int capacity = 2048, double maxUpdateDeltaSeconds = 0.1,
        ParticleFaultPolicy faultPolicy = ParticleFaultPolicy.Strict)
    {
        if (capacity < 1 || capacity > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        if (!double.IsFinite(maxUpdateDeltaSeconds) ||
            maxUpdateDeltaSeconds <= 0 || maxUpdateDeltaSeconds > 1)
            throw new ArgumentOutOfRangeException(nameof(maxUpdateDeltaSeconds));
        if (!Enum.IsDefined(faultPolicy))
            throw new ArgumentOutOfRangeException(nameof(faultPolicy));
        _storage = new ParticleState[capacity];
        _activeSlots = new int[capacity];
        _freeSlots = new int[capacity];
        _freeCount = capacity;
        for (int i = 0; i < capacity; i++)
            _freeSlots[i] = capacity - i - 1;
        MaxUpdateDeltaSeconds = maxUpdateDeltaSeconds;
        FaultPolicy = faultPolicy;
    }

    public ParticleState GetAt(int activeIndex)
    {
        if ((uint)activeIndex >= (uint)_activeCount)
            throw new ArgumentOutOfRangeException(nameof(activeIndex));
        return _storage[_activeSlots[activeIndex]];
    }

    public bool TrySpawn(in ParticleSpawn spawn)
    {
        if (!IsValid(spawn))
        {
            if (FaultPolicy == ParticleFaultPolicy.Strict)
                throw new ArgumentOutOfRangeException(nameof(spawn),
                    "Particle spawn values must be finite with positive lifetime and nonnegative sizes.");
            Increment(ref _droppedInvalid);
            return false;
        }
        if (_freeCount == 0)
        {
            Increment(ref _droppedCapacity);
            return false;
        }
        int slot = _freeSlots[--_freeCount];
        _storage[slot] = ParticleState.Create(spawn);
        _activeSlots[_activeCount++] = slot;
        Increment(ref _spawned);
        return true;
    }

    /// <summary>
    /// Advance only bounded cosmetic time, even after a long frame stall.
    /// An expired particle is removed when AgeSeconds reaches LifetimeSeconds.
    /// Survivors preserve original spawn order despite pool-slot reuse.
    /// </summary>
    public void Update(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        double dt = Math.Min(deltaSeconds, MaxUpdateDeltaSeconds);
        if (dt == 0) return;
        int write = 0;
        for (int read = 0; read < _activeCount; read++)
        {
            int slot = _activeSlots[read];
            ref ParticleState p = ref _storage[slot];
            double age = p.AgeSeconds + dt;
            if (age >= p.LifetimeSeconds)
            {
                ReturnSlot(slot);
                Increment(ref _expired);
                continue;
            }

            double vx = p.Velocity.X + p.Acceleration.X * dt;
            double vy = p.Velocity.Y + p.Acceleration.Y * dt;
            double x = p.Position.X + p.Velocity.X * dt +
                0.5 * p.Acceleration.X * dt * dt;
            double y = p.Position.Y + p.Velocity.Y * dt +
                0.5 * p.Acceleration.Y * dt * dt;
            double rotation = p.RotationRadians + p.AngularVelocityRadiansPerSecond * dt;
            if (!double.IsFinite(age) || !double.IsFinite(x) ||
                !double.IsFinite(y) || !double.IsFinite(vx) ||
                !double.IsFinite(vy) || !double.IsFinite(rotation))
            {
                if (FaultPolicy == ParticleFaultPolicy.Strict)
                    throw new InvalidOperationException(
                        "Non-finite cosmetic particle state during update.");
                ReturnSlot(slot);
                Increment(ref _droppedInvalid);
                continue;
            }
            p.AgeSeconds = age;
            p.Position = new Vector2D(x, y);
            p.Velocity = new Vector2D(vx, vy);
            p.RotationRadians = rotation;
            _activeSlots[write++] = slot;
        }
        _activeCount = write;
    }

    public void Clear(bool resetDiagnostics = false)
    {
        _activeCount = 0;
        _freeCount = Capacity;
        for (int i = 0; i < Capacity; i++)
            _freeSlots[i] = Capacity - i - 1;
        if (resetDiagnostics)
            _spawned = _expired = _droppedCapacity = _droppedInvalid = _skippedEmissions = 0;
    }

    internal void RejectGeneratedInvalidSpawn()
    {
        if (FaultPolicy == ParticleFaultPolicy.Strict)
            throw new InvalidOperationException("Emitter produced non-finite cosmetic spawn data.");
        Increment(ref _droppedInvalid);
    }

    internal void RecordSkippedEmissions(long count)
    {
        if (count > 0) _skippedEmissions = SaturatingAdd(_skippedEmissions, count);
    }

    internal static long SaturatingAdd(long a, long b) =>
        b > 0 && a > long.MaxValue - b ? long.MaxValue : a + b;

    private void ReturnSlot(int slot)
    {
        _storage[slot] = default;
        _freeSlots[_freeCount++] = slot;
    }

    private static void Increment(ref long counter)
    {
        if (counter != long.MaxValue) counter++;
    }

    private static bool IsValid(in ParticleSpawn s) =>
        double.IsFinite(s.Position.X) && double.IsFinite(s.Position.Y) &&
        double.IsFinite(s.Velocity.X) && double.IsFinite(s.Velocity.Y) &&
        double.IsFinite(s.Acceleration.X) && double.IsFinite(s.Acceleration.Y) &&
        double.IsFinite(s.LifetimeSeconds) && s.LifetimeSeconds > 0 &&
        double.IsFinite(s.InitialSize) && s.InitialSize >= 0 &&
        double.IsFinite(s.FinalSize) && s.FinalSize >= 0 &&
        double.IsFinite(s.RotationRadians) &&
        double.IsFinite(s.AngularVelocityRadiansPerSecond) &&
        ValidColor(s.InitialColor) && ValidColor(s.FinalColor);

    private static bool ValidColor(ParticleColor c) =>
        ValidChannel(c.R) && ValidChannel(c.G) && ValidChannel(c.B) && ValidChannel(c.A);

    private static bool ValidChannel(double value) =>
        double.IsFinite(value) && value >= 0 && value <= 1;
}
