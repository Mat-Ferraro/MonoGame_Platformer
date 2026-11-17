using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Foundation.Presentation.Particles;

/// <summary>A closed interval sampled using an emitter-owned cosmetic RNG.</summary>
public readonly record struct ParticleRange(double Min, double Max)
{
    public void Validate(string name, bool strictlyPositive = false)
    {
        if (!double.IsFinite(Min) || !double.IsFinite(Max) || Min > Max ||
            (strictlyPositive ? Min <= 0 : Min < 0))
            throw new ArgumentOutOfRangeException(name,
                "Particle range must be finite, ordered, and nonnegative (or positive). ");
    }

    public double Lerp(double unit) => Min + (Max - Min) * unit;
}

/// <summary>Linear, unpremultiplied RGBA component values in [0, 1].</summary>
public readonly record struct ParticleColor(double R, double G, double B, double A)
{
    public static ParticleColor White => new(1, 1, 1, 1);
    public static ParticleColor Transparent => new(1, 1, 1, 0);

    public void Validate(string name)
    {
        if (!Valid(R) || !Valid(G) || !Valid(B) || !Valid(A))
            throw new ArgumentOutOfRangeException(name,
                "Each color component must be finite and in [0, 1].");
    }

    public static ParticleColor Lerp(ParticleColor from, ParticleColor to, double fraction)
    {
        double t = Math.Clamp(fraction, 0, 1);
        return new ParticleColor(
            from.R + (to.R - from.R) * t,
            from.G + (to.G - from.G) * t,
            from.B + (to.B - from.B) * t,
            from.A + (to.A - from.A) * t);
    }

    private static bool Valid(double x) => double.IsFinite(x) && x >= 0 && x <= 1;
}

public enum ParticleSpawnShape
{
    Point,
    Box,
    Disk
}

public enum ParticleFaultPolicy
{
    /// <summary>Fail early on non-finite cosmetic state during development.</summary>
    Strict,
    /// <summary>Discard only damaged cosmetics and increment diagnostics.</summary>
    RecoverAndCount
}

/// <summary>
/// One validated, reusable emitter preset. Immutable after construction; no texture,
/// graphics device, gameplay event or authoritative physics dependency.
/// Full composite effect definitions and emitter-local coordinates belong to P03.
/// </summary>
public sealed record ParticleEmitterDefinition
{
    public string Id { get; init; } = "";
    public int BurstCount { get; init; }
    public double RatePerSecond { get; init; }
    public int MaxSpawnsPerUpdate { get; init; } = 64;
    public double MaxEmissionDeltaSeconds { get; init; } = 0.1;
    public ParticleSpawnShape Shape { get; init; } = ParticleSpawnShape.Point;
    public ParticleRange SpawnOffsetX { get; init; } = new(0, 0);
    public ParticleRange SpawnOffsetY { get; init; } = new(0, 0);
    public double SpawnRadius { get; init; }
    public ParticleRange LifetimeSeconds { get; init; } = new(0.3, 0.6);
    public ParticleRange Speed { get; init; } = new(0, 1);
    public ParticleRange DirectionRadians { get; init; } = new(0, Math.PI * 2);
    public double AccelerationX { get; init; }
    public double AccelerationY { get; init; }
    public ParticleRange RotationRadians { get; init; } = new(0, 0);
    public ParticleRange AngularVelocityRadiansPerSecond { get; init; } = new(0, 0);
    public ParticleRange StartSize { get; init; } = new(1, 1);
    public ParticleRange EndSize { get; init; } = new(0, 0);
    public ParticleColor StartColor { get; init; } = ParticleColor.White;
    public ParticleColor EndColor { get; init; } = ParticleColor.Transparent;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 100 ||
            Id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')))
            throw new ArgumentException("Emitter ID must use letters, digits, '.', '_' or '-'.", nameof(Id));
        if (BurstCount < 0 || BurstCount > 100_000)
            throw new ArgumentOutOfRangeException(nameof(BurstCount));
        if (!double.IsFinite(RatePerSecond) || RatePerSecond < 0 || RatePerSecond > 100_000)
            throw new ArgumentOutOfRangeException(nameof(RatePerSecond));
        if (BurstCount == 0 && RatePerSecond == 0)
            throw new ArgumentException("At least one emission mode must be enabled.", nameof(BurstCount));
        if (MaxSpawnsPerUpdate < 1 || MaxSpawnsPerUpdate > 100_000)
            throw new ArgumentOutOfRangeException(nameof(MaxSpawnsPerUpdate));
        if (!double.IsFinite(MaxEmissionDeltaSeconds) ||
            MaxEmissionDeltaSeconds <= 0 || MaxEmissionDeltaSeconds > 1)
            throw new ArgumentOutOfRangeException(nameof(MaxEmissionDeltaSeconds));
        if (!Enum.IsDefined(Shape))
            throw new ArgumentOutOfRangeException(nameof(Shape));
        ValidateSignedRange(SpawnOffsetX, nameof(SpawnOffsetX));
        ValidateSignedRange(SpawnOffsetY, nameof(SpawnOffsetY));
        if (!double.IsFinite(SpawnRadius) || SpawnRadius < 0)
            throw new ArgumentOutOfRangeException(nameof(SpawnRadius));
        if (Shape != ParticleSpawnShape.Box &&
            (SpawnOffsetX.Min != SpawnOffsetX.Max || SpawnOffsetY.Min != SpawnOffsetY.Max))
            throw new ArgumentException("Point/disk spawns require fixed center offsets.", nameof(Shape));
        if (Shape == ParticleSpawnShape.Disk && SpawnRadius <= 0)
            throw new ArgumentOutOfRangeException(nameof(SpawnRadius), "Disk spawns require a positive radius.");
        LifetimeSeconds.Validate(nameof(LifetimeSeconds), strictlyPositive: true);
        Speed.Validate(nameof(Speed));
        // Direction and rotation angles can legitimately be negative.
        ValidateSignedRange(DirectionRadians, nameof(DirectionRadians));
        ValidateSignedRange(RotationRadians, nameof(RotationRadians));
        ValidateSignedRange(AngularVelocityRadiansPerSecond, nameof(AngularVelocityRadiansPerSecond));
        StartSize.Validate(nameof(StartSize));
        EndSize.Validate(nameof(EndSize));
        if (!double.IsFinite(AccelerationX))
            throw new ArgumentOutOfRangeException(nameof(AccelerationX));
        if (!double.IsFinite(AccelerationY))
            throw new ArgumentOutOfRangeException(nameof(AccelerationY));
        StartColor.Validate(nameof(StartColor));
        EndColor.Validate(nameof(EndColor));
    }

    private static void ValidateSignedRange(ParticleRange range, string name)
    {
        if (!double.IsFinite(range.Min) || !double.IsFinite(range.Max) || range.Min > range.Max)
            throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>Runtime particle creation data; public for headless tests and later adapters.</summary>
public readonly record struct ParticleSpawn(
    Vector2D Position,
    Vector2D Velocity,
    Vector2D Acceleration,
    double LifetimeSeconds,
    double InitialSize,
    double FinalSize,
    ParticleColor InitialColor,
    ParticleColor FinalColor,
    double RotationRadians = 0,
    double AngularVelocityRadiansPerSecond = 0);
