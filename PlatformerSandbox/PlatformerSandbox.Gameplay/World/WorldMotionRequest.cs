namespace PlatformerSandbox.Gameplay.World;

/// <summary>
/// Typed, tick-bound changes to authoritative horizontal motion sources.
/// Environmental velocity (wind/conveyor) is a CONTINUOUS, signed world-space
/// transport contribution. An impulse is a one-shot signed velocity kick
/// which decays over successful world ticks. They are not player input and
/// may operate while a character is stunned.
/// </summary>
public enum WorldMotionRequestKind
{
    SetEnvironmentVelocity = 1,
    AddHorizontalImpulse = 2
}

public readonly record struct WorldMotionRequest(
    int PlayerId, WorldMotionRequestKind Kind, double HorizontalSpeed);

public readonly record struct WorldMotionSnapshot(
    double EnvironmentVelocityX, double ImpulseVelocityX,
    double DashVelocityX, double CombinedExternalVelocityX,
    int FacingDirection);

/// <summary>
/// Owned by exactly one world player. Forked together with the motor and
/// ability coordinator, so rejected world ticks cannot consume impulses,
/// alter environmental settings, or change dash direction.
/// </summary>
internal sealed class WorldPlayerMotion
{
    public const double ImpulseDecayPerTick = 0.82;
    public const double ImpulseZeroThreshold = 0.001;
    public const double DashSpeedMultiplier = 2.0;
    public double EnvironmentVelocityX { get; private set; }
    public double ImpulseVelocityX { get; private set; }
    public int FacingDirection { get; private set; } = 1;
    public int DashDirection { get; private set; }

    public WorldPlayerMotion Fork() => (WorldPlayerMotion)MemberwiseClone();

    public void Face(int direction)
    {
        if (direction is not (-1 or 1))
            throw new ArgumentOutOfRangeException(nameof(direction));
        FacingDirection = direction;
    }

    public void BeginDash() => DashDirection = FacingDirection;

    public void Apply(WorldMotionRequest request)
    {
        switch (request.Kind)
        {
            case WorldMotionRequestKind.SetEnvironmentVelocity:
                EnvironmentVelocityX = request.HorizontalSpeed;
                break;
            case WorldMotionRequestKind.AddHorizontalImpulse:
                double next = ImpulseVelocityX + request.HorizontalSpeed;
                if (!double.IsFinite(next) || Math.Abs(next) > 10000.0)
                    throw new ArgumentOutOfRangeException(nameof(request));
                ImpulseVelocityX = next;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request));
        }
    }

    public double ExternalSpeed(bool dashActive, double ordinaryMaximumSpeed) =>
        EnvironmentVelocityX + ImpulseVelocityX +
        (dashActive ? DashDirection * ordinaryMaximumSpeed * DashSpeedMultiplier : 0.0);

    public WorldMotionSnapshot Snapshot(bool dashActive, double ordinaryMaximumSpeed)
    {
        double dash = dashActive
            ? DashDirection * ordinaryMaximumSpeed * DashSpeedMultiplier : 0.0;
        return new WorldMotionSnapshot(EnvironmentVelocityX, ImpulseVelocityX,
            dash, EnvironmentVelocityX + ImpulseVelocityX + dash, FacingDirection);
    }

    public void CompleteTick()
    {
        double next = ImpulseVelocityX * ImpulseDecayPerTick;
        ImpulseVelocityX = Math.Abs(next) < ImpulseZeroThreshold ? 0.0 : next;
    }
}
