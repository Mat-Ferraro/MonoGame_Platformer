namespace PlatformerSandbox.Simulation.Motion.Profiles;

/// <summary>
/// Analytically evaluated upward motion following an early jump-button release.
/// The release segment preserves position and velocity at the instant of release,
/// then applies a constant downward acceleration until it reaches its new apex.
///
/// This is ONLY the cut-ascent segment. It does not invent a descent/landing
/// policy, advance a motor, or override collisions. Displacements remain relative
/// to the ORIGINAL jump takeoff position (positive-up).
/// </summary>
public sealed class JumpReleaseTrajectory
{
    internal JumpReleaseTrajectory(
        double releaseTimeSeconds,
        double releaseHeight,
        double releaseVelocity,
        double cutDeceleration,
        double timeFromReleaseToApexSeconds,
        double peakHeight)
    {
        ReleaseTimeSeconds = releaseTimeSeconds;
        ReleaseHeight = releaseHeight;
        ReleaseVelocity = releaseVelocity;
        CutDeceleration = cutDeceleration;
        TimeFromReleaseToApexSeconds = timeFromReleaseToApexSeconds;
        PeakHeight = peakHeight;
    }

    public double ReleaseTimeSeconds { get; }
    public double ReleaseHeight { get; }
    public double ReleaseVelocity { get; }
    public double CutDeceleration { get; }
    public double TimeFromReleaseToApexSeconds { get; }
    public double PeakHeight { get; }
    public double ApexTimeSinceTakeoffSeconds => ReleaseTimeSeconds + TimeFromReleaseToApexSeconds;

    /// <summary>
    /// Evaluates only the cut-ascent interval, where elapsedSeconds is time
    /// SINCE RELEASE (not time since takeoff). There is no implicit fall phase.
    /// </summary>
    public JumpSample Evaluate(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) ||
            elapsedSeconds < 0.0 ||
            elapsedSeconds > TimeFromReleaseToApexSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }

        // Return the mathematically defined endpoints exactly.
        if (elapsedSeconds == 0.0)
            return new JumpSample(ReleaseHeight, ReleaseVelocity, -CutDeceleration);
        if (elapsedSeconds == TimeFromReleaseToApexSeconds)
            return new JumpSample(PeakHeight, 0.0, -CutDeceleration);

        double position = ReleaseHeight + ReleaseVelocity * elapsedSeconds
            - 0.5 * CutDeceleration * elapsedSeconds * elapsedSeconds;
        double velocity = ReleaseVelocity - CutDeceleration * elapsedSeconds;
        if (!double.IsFinite(position) || !double.IsFinite(velocity))
            throw new OverflowException("The release trajectory cannot be evaluated with finite values.");

        return new JumpSample(position, velocity, -CutDeceleration);
    }
}
