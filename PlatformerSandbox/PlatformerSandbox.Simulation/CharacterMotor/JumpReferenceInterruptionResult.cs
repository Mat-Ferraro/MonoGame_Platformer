using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Contacts;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// Terminal state of an unobstructed reference OR the first collision event.
/// If interrupted, RemainingSeconds must be handled by the future authoritative
/// motor; continuing the original reference would be physically incorrect.
/// </summary>
public sealed class JumpReferenceInterruptionResult
{
    internal JumpReferenceInterruptionResult(
        Aabb bounds, Vector2D velocity, double elapsedSeconds,
        double remainingSeconds, JumpProfileSweepHit? contact)
    {
        Bounds = bounds;
        Velocity = velocity;
        ElapsedSeconds = elapsedSeconds;
        RemainingSeconds = remainingSeconds;
        Contact = contact;
    }

    public Aabb Bounds { get; }
    public Vector2D Velocity { get; }
    public double ElapsedSeconds { get; }
    public double RemainingSeconds { get; }
    public JumpProfileSweepHit? Contact { get; }
    public bool IsInterrupted => Contact.HasValue;
}
