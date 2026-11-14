using PlatformerSandbox.Simulation.Motion.Constraints;

namespace PlatformerSandbox.Simulation.Motion.Profiles;

/// <summary>
/// Complete, unobstructed, upward-positive, vertical jump trajectory.
/// Time is measured from takeoff, and displacement is relative to takeoff.
///
/// For a held jump this delegates to the existing Milestone 5 profile.
/// For an early release it composes held ascent, Milestone 6's cut ascent,
/// and an analytical descent from the cut apex to takeoff height.
///
/// It NEVER resolves a landing, collides with geometry, or modifies game state.
/// </summary>
public sealed class JumpFlightTrajectory
{
    private readonly JumpTrajectory _heldJump;
    private readonly JumpReleaseTrajectory? _releasedAscent;

    internal JumpFlightTrajectory(JumpTrajectory heldJump)
    {
        _heldJump = heldJump;
        PeakHeight = heldJump.Height;
        ApexTimeSinceTakeoffSeconds = heldJump.RiseTimeSeconds;
        DescentDurationSeconds = heldJump.FallTimeSeconds;
        TotalDurationSeconds = heldJump.TotalTimeSeconds;
        DescentPolicy = JumpDescentPolicy.PreserveReferenceFallLaw;
    }

    internal JumpFlightTrajectory(
        JumpTrajectory heldJump,
        JumpReleaseTrajectory releasedAscent,
        JumpDescentPolicy descentPolicy,
        double descentDurationSeconds)
    {
        _heldJump = heldJump;
        _releasedAscent = releasedAscent;
        PeakHeight = releasedAscent.PeakHeight;
        ApexTimeSinceTakeoffSeconds = releasedAscent.ApexTimeSinceTakeoffSeconds;
        DescentDurationSeconds = descentDurationSeconds;
        TotalDurationSeconds = ApexTimeSinceTakeoffSeconds + descentDurationSeconds;
        DescentPolicy = descentPolicy;
    }

    // Internal immutable held-flight definition used by the runtime release adapter.
    // The cut flight is composed from the same original constraints; never
    // infer a new held profile from the current position/velocity.
    internal JumpTrajectory HeldJump => _heldJump;

    public bool IsEarlyReleased => _releasedAscent is not null;
    public double? ReleaseTimeSinceTakeoffSeconds => _releasedAscent?.ReleaseTimeSeconds;
    public double PeakHeight { get; }
    public double ApexTimeSinceTakeoffSeconds { get; }
    public double DescentDurationSeconds { get; }
    public double TotalDurationSeconds { get; }
    public JumpDescentPolicy DescentPolicy { get; }

    /// <summary>
    /// Returns the exact reference displacement, velocity and acceleration at
    /// the specified elapsed time. Defined only on [0, TotalDurationSeconds].
    /// An event boundary is right-continuous for acceleration: position and
    /// velocity remain continuous while acceleration may jump at the event.
    /// The end point has zero displacement and a DOWNWARD velocity; it is not
    /// a landing/collision and does not clamp velocity to zero.
    /// </summary>
    public JumpSample Evaluate(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) ||
            elapsedSeconds < 0.0 ||
            elapsedSeconds > TotalDurationSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }

        if (_releasedAscent is not { } released)
            return _heldJump.Evaluate(elapsedSeconds);

        if (elapsedSeconds < released.ReleaseTimeSeconds)
            return _heldJump.Evaluate(elapsedSeconds);

        if (elapsedSeconds < ApexTimeSinceTakeoffSeconds)
            return released.Evaluate(elapsedSeconds - released.ReleaseTimeSeconds);

        // The public endpoint is computed by adding two floating-point durations.
        // Subtraction may not recover the original duration bit-for-bit.
        // Evaluate it explicitly so the reference always returns to y=0 exactly.
        if (elapsedSeconds == TotalDurationSeconds)
            return EvaluateCutDescent(DescentDurationSeconds);

        double descentElapsedSeconds = elapsedSeconds - ApexTimeSinceTakeoffSeconds;
        return EvaluateCutDescent(descentElapsedSeconds);
    }

    private JumpSample EvaluateCutDescent(double descentElapsedSeconds)
    {
        double duration = DescentDurationSeconds;
        double exponent = _heldJump.Exponent;
        double peak = PeakHeight;

        // Avoid an almost-zero residual at the reference endpoint.
        if (descentElapsedSeconds == duration)
        {
            return new JumpSample(
                0.0,
                -peak * exponent / duration,
                -peak * exponent * (exponent - 1.0) / (duration * duration));
        }

        double progress = descentElapsedSeconds / duration;
        double displacement = peak * (1.0 - Math.Pow(progress, exponent));
        double velocity = -peak * exponent / duration
            * Math.Pow(progress, exponent - 1.0);
        double acceleration = -peak * exponent * (exponent - 1.0)
            / (duration * duration) * Math.Pow(progress, exponent - 2.0);

        if (!double.IsFinite(displacement) ||
            !double.IsFinite(velocity) ||
            !double.IsFinite(acceleration))
        {
            throw new OverflowException("The cut-jump descent cannot be evaluated with finite values.");
        }

        return new JumpSample(displacement, velocity, acceleration);
    }
}
