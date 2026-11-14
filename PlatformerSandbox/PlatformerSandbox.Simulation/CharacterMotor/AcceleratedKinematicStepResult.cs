using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// End state of one constant-acceleration, contact-constrained world step.
/// Returned acceleration is the currently projected acceleration for the
/// remainder of the step, not a permanently modified gameplay input.
/// </summary>
public sealed class AcceleratedKinematicStepResult
{
    internal AcceleratedKinematicStepResult(
        Aabb bounds, Vector2D velocity, Vector2D acceleration,
        double durationSeconds, AcceleratedKinematicContact[] contacts)
    {
        Bounds = bounds;
        Velocity = velocity;
        Acceleration = acceleration;
        DurationSeconds = durationSeconds;
        Contacts = Array.AsReadOnly((AcceleratedKinematicContact[])contacts.Clone());
    }

    public Aabb Bounds { get; }
    public Vector2D Velocity { get; }
    public Vector2D Acceleration { get; }
    public double DurationSeconds { get; }
    public IReadOnlyList<AcceleratedKinematicContact> Contacts { get; }
}
