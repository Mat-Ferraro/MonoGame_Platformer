using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// End state after the requested time has elapsed, plus an immutable contact
/// history. This is the result of ONE straight constant-velocity segment,
/// not the future complete accelerating character motor.
/// </summary>
public sealed class LinearKinematicStepResult
{
    internal LinearKinematicStepResult(
        Aabb bounds,
        Vector2D velocity,
        double durationSeconds,
        LinearKinematicContact[] contacts)
    {
        Bounds = bounds;
        Velocity = velocity;
        DurationSeconds = durationSeconds;
        Contacts = Array.AsReadOnly((LinearKinematicContact[])contacts.Clone());
    }

    public Aabb Bounds { get; }
    public Vector2D Velocity { get; }
    public double DurationSeconds { get; }
    public IReadOnlyList<LinearKinematicContact> Contacts { get; }
}
