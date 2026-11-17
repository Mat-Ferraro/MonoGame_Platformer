using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Foundation.Presentation.Particles;

/// <summary>
/// One value-type particle in preallocated storage. Renderer access is read-only:
/// GetAt returns a value copy, not ownership of the pooled slot.
/// </summary>
public struct ParticleState
{
    public Vector2D Position { get; internal set; }
    public Vector2D Velocity { get; internal set; }
    public Vector2D Acceleration { get; internal set; }
    public double AgeSeconds { get; internal set; }
    public double LifetimeSeconds { get; internal set; }
    public double RotationRadians { get; internal set; }
    public double AngularVelocityRadiansPerSecond { get; internal set; }
    public double InitialSize { get; internal set; }
    public double FinalSize { get; internal set; }
    public ParticleColor InitialColor { get; internal set; }
    public ParticleColor FinalColor { get; internal set; }

    public readonly double NormalizedAge => LifetimeSeconds <= 0
        ? 1 : Math.Clamp(AgeSeconds / LifetimeSeconds, 0, 1);

    public readonly double Size =>
        InitialSize + (FinalSize - InitialSize) * NormalizedAge;

    public readonly ParticleColor Color =>
        ParticleColor.Lerp(InitialColor, FinalColor, NormalizedAge);

    internal static ParticleState Create(in ParticleSpawn spawn) => new()
    {
        Position = spawn.Position,
        Velocity = spawn.Velocity,
        Acceleration = spawn.Acceleration,
        AgeSeconds = 0,
        LifetimeSeconds = spawn.LifetimeSeconds,
        RotationRadians = spawn.RotationRadians,
        AngularVelocityRadiansPerSecond = spawn.AngularVelocityRadiansPerSecond,
        InitialSize = spawn.InitialSize,
        FinalSize = spawn.FinalSize,
        InitialColor = spawn.InitialColor,
        FinalColor = spawn.FinalColor
    };
}

/// <summary>Cumulative counters; all increments saturate rather than wrap.</summary>
public readonly record struct ParticleDiagnostics(
    int Capacity,
    int ActiveParticles,
    long Spawned,
    long Expired,
    long DroppedAtCapacity,
    long DroppedInvalid,
    long SkippedEmissionOpportunities);
