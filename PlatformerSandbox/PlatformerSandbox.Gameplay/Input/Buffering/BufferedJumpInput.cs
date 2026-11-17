namespace PlatformerSandbox.Gameplay.Input.Buffering;

/// <summary>
/// Gameplay-owned discrete jump input buffering and coyote-time eligibility.
/// No collision or device APIs are involved. Decisions are made only at fixed
/// simulation boundaries: a press immediately before landing is consumed on
/// the NEXT fixed update after contact, never mid-step.
/// </summary>
public sealed class BufferedJumpInput
{
    private readonly JumpTimingConstraints _constraints;
    private double? _pendingPressTimeSeconds;
    private double? _lastWalkOffTimeSeconds;
    private bool _buttonHeld;

    public BufferedJumpInput(JumpTimingConstraints constraints)
    {
        if (!double.IsFinite(constraints.CoyoteTimeSeconds) ||
            constraints.CoyoteTimeSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(constraints),
                "Coyote time must be finite and nonnegative.");
        if (!double.IsFinite(constraints.JumpBufferSeconds) ||
            constraints.JumpBufferSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(constraints),
                "Jump buffer must be finite and nonnegative.");
        _constraints = constraints;
    }

    /// <summary>Independent copy for a proposed fixed-tick update.</summary>
    public BufferedJumpInput Fork()
    {
        var copy = new BufferedJumpInput(_constraints);
        copy._pendingPressTimeSeconds = _pendingPressTimeSeconds;
        copy._lastWalkOffTimeSeconds = _lastWalkOffTimeSeconds;
        copy._buttonHeld = _buttonHeld;
        return copy;
    }

    public bool HasPendingPress => _pendingPressTimeSeconds is not null;
    public bool IsButtonHeld => _buttonHeld;

    /// <summary>
    /// Computes a candidate without mutating this object. A release edge
    /// records that a buffered tap should start its jump already shortened.
    /// A pressed edge supersedes the previous buffered press.
    /// </summary>
    public JumpInputPlan Plan(double nowSeconds, bool isGrounded,
        bool isJumping, bool pressedThisStep, bool releasedThisStep)
    {
        if (!double.IsFinite(nowSeconds) || nowSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(nowSeconds));
        if (_pendingPressTimeSeconds is double press && press > nowSeconds ||
            _lastWalkOffTimeSeconds is double left && left > nowSeconds)
            throw new ArgumentOutOfRangeException(nameof(nowSeconds),
                "Simulation time cannot move backward.");

        double? pending = pressedThisStep ? nowSeconds : _pendingPressTimeSeconds;
        if (pending is double captured &&
            nowSeconds - captured > _constraints.JumpBufferSeconds)
            pending = null;

        bool held = releasedThisStep ? false : (pressedThisStep || _buttonHeld);
        bool coyote = !isGrounded && !isJumping &&
            _constraints.CoyoteTimeSeconds > 0.0 &&
            _lastWalkOffTimeSeconds is double walkedOff &&
            nowSeconds - walkedOff <= _constraints.CoyoteTimeSeconds;
        bool start = !isJumping && pending.HasValue && (isGrounded || coyote);

        return new JumpInputPlan(pending, held, start,
            start && !isGrounded && coyote,
            start && !held);
    }

    /// <summary>
    /// Called only after a successful authoritative simulation update.
    /// A completed landing clears coyote time but retains an unconsumed press.
    /// A walk-off (not a jump takeoff) creates the one available coyote window.
    /// </summary>
    public void Commit(JumpInputPlan plan, double endSeconds,
        bool wasGrounded, bool isGrounded, bool jumpStarted)
    {
        if (!double.IsFinite(endSeconds) || endSeconds < 0.0 ||
            plan.PendingPressTimeSeconds is double captured && captured > endSeconds)
            throw new ArgumentOutOfRangeException(nameof(endSeconds));

        _pendingPressTimeSeconds = jumpStarted ? null : plan.PendingPressTimeSeconds;
        _buttonHeld = plan.ButtonHeld;
        if (jumpStarted || isGrounded)
            _lastWalkOffTimeSeconds = null;
        else if (wasGrounded)
            _lastWalkOffTimeSeconds = endSeconds;
    }

    /// <summary>Discard queued presses, held state, and grace on respawn.</summary>
    public void Reset()
    {
        _pendingPressTimeSeconds = null;
        _lastWalkOffTimeSeconds = null;
        _buttonHeld = false;
    }
}