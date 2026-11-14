using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>Read-only outcome of a complete controller step. The controller owns the state.</summary>
public sealed class CharacterControlStepResult
{
    internal CharacterControlStepResult(
        Aabb bounds, Vector2D velocity, bool isGrounded, bool isJumping,
        bool jumpStarted, int jumpsStarted, double elapsedSeconds,
        double durationSeconds, CharacterControlContact[] contacts)
    {
        Bounds = bounds;
        Velocity = velocity;
        IsGrounded = isGrounded;
        IsJumping = isJumping;
        JumpStarted = jumpStarted;
        JumpsStarted = jumpsStarted;
        ElapsedSeconds = elapsedSeconds;
        DurationSeconds = durationSeconds;
        Contacts = Array.AsReadOnly((CharacterControlContact[])contacts.Clone());
    }

    public Aabb Bounds { get; }
    public Vector2D Velocity { get; }
    public bool IsGrounded { get; }
    public bool IsJumping { get; }
    public bool JumpStarted { get; }
    public int JumpsStarted { get; }
    public double ElapsedSeconds { get; }
    public double DurationSeconds { get; }
    public IReadOnlyList<CharacterControlContact> Contacts { get; }
}
