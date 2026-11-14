using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// Result of one unified static/moving accelerated segment. A moving-floor
/// landing returns at the impact time so the owning controller can resume
/// the rest of the update under its ground/riding policy.
/// </summary>
public sealed class UnifiedAcceleratedStepResult
{
    internal UnifiedAcceleratedStepResult(Aabb bounds, Vector2D velocity,
        Vector2D acceleration, double advancedSeconds, int? movingFloorId,
        CharacterControlContact[] contacts)
    {
        Bounds = bounds;
        Velocity = velocity;
        Acceleration = acceleration;
        AdvancedSeconds = advancedSeconds;
        MovingFloorId = movingFloorId;
        Contacts = Array.AsReadOnly((CharacterControlContact[])contacts.Clone());
    }

    public Aabb Bounds { get; }
    public Vector2D Velocity { get; }
    public Vector2D Acceleration { get; }
    public double AdvancedSeconds { get; }
    public int? MovingFloorId { get; }
    public IReadOnlyList<CharacterControlContact> Contacts { get; }
}
