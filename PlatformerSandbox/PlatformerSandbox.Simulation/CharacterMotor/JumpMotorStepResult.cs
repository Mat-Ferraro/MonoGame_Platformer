using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>Immutable snapshot returned after one authoritative headless movement step.</summary>
public sealed class JumpMotorStepResult
{
    internal JumpMotorStepResult(
        Aabb bounds, Vector2D velocity, JumpMotorPhase phase,
        bool isGrounded, double elapsedSeconds,
        double stepDurationSeconds, JumpMotorContact[] contacts)
    {
        Bounds = bounds;
        Velocity = velocity;
        Phase = phase;
        IsGrounded = isGrounded;
        ElapsedSeconds = elapsedSeconds;
        StepDurationSeconds = stepDurationSeconds;
        Contacts = Array.AsReadOnly((JumpMotorContact[])contacts.Clone());
    }

    public Aabb Bounds { get; }
    public Vector2D Velocity { get; }
    public JumpMotorPhase Phase { get; }
    public bool IsGrounded { get; }
    public double ElapsedSeconds { get; }
    public double StepDurationSeconds { get; }
    public IReadOnlyList<JumpMotorContact> Contacts { get; }
}
