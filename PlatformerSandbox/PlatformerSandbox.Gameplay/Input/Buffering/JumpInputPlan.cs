namespace PlatformerSandbox.Gameplay.Input.Buffering;

/// <summary>
/// Immutable proposed input decision. BufferedJumpInput only changes its
/// persistent state after the corresponding simulation step succeeds.
/// </summary>
public readonly record struct JumpInputPlan(
    double? PendingPressTimeSeconds,
    bool ButtonHeld,
    bool StartJump,
    bool AuthorizeCoyote,
    bool ReleaseOnStart);
