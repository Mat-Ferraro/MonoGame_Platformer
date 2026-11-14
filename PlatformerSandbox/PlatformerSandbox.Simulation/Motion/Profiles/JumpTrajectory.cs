namespace PlatformerSandbox.Simulation.Motion.Profiles;

/// <summary>
/// Analytically defined vertical jump trajectory. Its power-curve exponent is
/// derived from the designer's constraints by JumpConstraintSolver.
///
/// Rising (0 &lt;= t &lt;= Tr): y = H * [1 - (1 - t/Tr)^p]
/// Falling (Tr &lt; t &lt;= Tr+Tf): y = H * [1 - ((t-Tr)/Tf)^p]
///
/// This profile does not model collisions, inputs, landing, or platform motion.
/// A runtime motor may interrupt or replace the profile when gameplay changes.
/// </summary>
public sealed class JumpTrajectory
{
    internal JumpTrajectory(
        double height,
        double riseTimeSeconds,
        double fallTimeSeconds,
        double apexRegionFraction,
        double exponent)
    {
        Height = height;
        RiseTimeSeconds = riseTimeSeconds;
        FallTimeSeconds = fallTimeSeconds;
        ApexRegionFraction = apexRegionFraction;
        Exponent = exponent;
    }

    public double Height { get; }
    public double RiseTimeSeconds { get; }
    public double FallTimeSeconds { get; }
    public double TotalTimeSeconds => RiseTimeSeconds + FallTimeSeconds;
    public double ApexRegionFraction { get; }
    public double Exponent { get; }

    /// <summary>
    /// Time during which the reference jump stays at or above
    /// Height * (1 - ApexRegionFraction), summed over rising and falling.
    /// </summary>
    public double TimeNearApexSeconds =>
        TotalTimeSeconds * Math.Pow(ApexRegionFraction, 1.0 / Exponent);

    /// <summary>
    /// Evaluates the reference jump at time since takeoff.
    /// Values beyond the nominal return-to-launch-height time are rejected:
    /// the profile must not silently act as an implicit ground collision.
    /// </summary>
    public JumpSample Evaluate(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) ||
            elapsedSeconds < 0.0 ||
            elapsedSeconds > TotalTimeSeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }

        double height;
        double velocity;
        double acceleration;

        if (elapsedSeconds <= RiseTimeSeconds)
        {
            double remaining = 1.0 - elapsedSeconds / RiseTimeSeconds;
            height = Height * (1.0 - Math.Pow(remaining, Exponent));
            velocity = Height * Exponent / RiseTimeSeconds
                * Math.Pow(remaining, Exponent - 1.0);
            acceleration = -Height * Exponent * (Exponent - 1.0)
                / (RiseTimeSeconds * RiseTimeSeconds)
                * Math.Pow(remaining, Exponent - 2.0);
        }
        else
        {
            // The public end time is computed by adding rise and fall durations.
            // Subtracting the rise duration does not necessarily recover the
            // fall duration bit-for-bit (e.g., 0.4 + 0.3 - 0.4). If this is
            // exactly the defined endpoint, force progress to 1 so the jump
            // returns to takeoff height EXACTLY, which is critical for strict
            // collision boundary comparisons. This is not a collision response.
            double progress = elapsedSeconds == TotalTimeSeconds
                ? 1.0
                : (elapsedSeconds - RiseTimeSeconds) / FallTimeSeconds;
            height = Height * (1.0 - Math.Pow(progress, Exponent));
            velocity = -Height * Exponent / FallTimeSeconds
                * Math.Pow(progress, Exponent - 1.0);
            acceleration = -Height * Exponent * (Exponent - 1.0)
                / (FallTimeSeconds * FallTimeSeconds)
                * Math.Pow(progress, Exponent - 2.0);
        }

        if (!double.IsFinite(height) ||
            !double.IsFinite(velocity) ||
            !double.IsFinite(acceleration))
        {
            throw new OverflowException("The reference jump cannot be evaluated with finite values.");
        }

        return new JumpSample(height, velocity, acceleration);
    }
}
