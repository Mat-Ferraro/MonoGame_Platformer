namespace PlatformerSandbox.Gameplay.Input.Buffering;

/// <summary>
/// Input assistance windows measured in authoritative simulation seconds.
/// Zero disables the corresponding grace period. Neither value modifies physics.
/// </summary>
public readonly record struct JumpTimingConstraints(
    double CoyoteTimeSeconds,
    double JumpBufferSeconds);
