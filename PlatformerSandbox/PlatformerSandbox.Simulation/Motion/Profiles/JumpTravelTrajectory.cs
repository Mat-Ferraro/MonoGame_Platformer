namespace PlatformerSandbox.Simulation.Motion.Profiles;

/// <summary>
/// Immutable, event-aware composition of an analytical vertical flight and
/// horizontal trajectories restarted at specified input-change events.
///
/// Neither axis numerically integrates. This is an unobstructed DESIGN
/// reference, not an authoritative motor or collision outcome. Acceleration
/// may jump at events; position and velocity remain continuous.
/// </summary>
public sealed class JumpTravelTrajectory
{
    private readonly JumpFlightTrajectory _vertical;
    private readonly double[] _startTimes;
    private readonly HorizontalMotionTrajectory[] _horizontalSegments;

    internal JumpTravelTrajectory(
        JumpFlightTrajectory vertical,
        HorizontalMotionProfile horizontalProfile,
        double[] startTimes,
        HorizontalMotionTrajectory[] horizontalSegments,
        double initialHorizontalPosition)
    {
        _vertical = vertical;
        HorizontalProfile = horizontalProfile;
        // Own the arrays, even if a future caller changes the construction path.
        _startTimes = (double[])startTimes.Clone();
        _horizontalSegments = (HorizontalMotionTrajectory[])horizontalSegments.Clone();
        InitialHorizontalPosition = initialHorizontalPosition;

        // Fail at construction, not only when a caller asks for the endpoint.
        JumpTravelSample terminal = Evaluate(TotalDurationSeconds);
        SignedTravelDistance = terminal.HorizontalPosition - initialHorizontalPosition;
        if (!double.IsFinite(SignedTravelDistance))
            throw new OverflowException("Reference jump travel distance is not finite.");
    }

    public HorizontalMotionProfile HorizontalProfile { get; }
    public double InitialHorizontalPosition { get; }
    public double TotalDurationSeconds => _vertical.TotalDurationSeconds;
    public double SignedTravelDistance { get; }

    /// <summary>
    /// Sample both axes at any time within [0, vertical reference duration].
    /// An input change belongs to the NEW segment at its exact event time.
    /// No landing, input-device interaction, or collision response occurs.
    /// </summary>
    public JumpTravelSample Evaluate(double timeSinceTakeoffSeconds)
    {
        if (!double.IsFinite(timeSinceTakeoffSeconds) ||
            timeSinceTakeoffSeconds < 0.0 ||
            timeSinceTakeoffSeconds > TotalDurationSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(timeSinceTakeoffSeconds));
        }

        int index = Array.BinarySearch(_startTimes, timeSinceTakeoffSeconds);
        if (index < 0)
            index = ~index - 1;

        HorizontalMotionSample horizontal = _horizontalSegments[index].Evaluate(
            timeSinceTakeoffSeconds - _startTimes[index]);
        JumpSample vertical = _vertical.Evaluate(timeSinceTakeoffSeconds);

        return new JumpTravelSample(
            horizontal.Position,
            vertical.VerticalDisplacement,
            horizontal.Velocity,
            vertical.VerticalVelocity,
            horizontal.Acceleration,
            vertical.VerticalAcceleration);
    }
}
