using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>Immutable step outcome; simulation owns the movement state.</summary>
public sealed class SteerableJumpStepResult
{
    internal SteerableJumpStepResult(
        Aabb bounds, Vector2D velocity, bool grounded,
        CharacterControlContact[] contacts)
    {
        Bounds = bounds;
        Velocity = velocity;
        IsGrounded = grounded;
        Contacts = Array.AsReadOnly((CharacterControlContact[])contacts.Clone());
    }

    public Aabb Bounds { get; }
    public Vector2D Velocity { get; }
    public bool IsGrounded { get; }
    public IReadOnlyList<CharacterControlContact> Contacts { get; }
}
