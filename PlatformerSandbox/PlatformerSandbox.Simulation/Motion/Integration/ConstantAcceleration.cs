namespace PlatformerSandbox.Simulation.Motion.Integration;

/// <summary>
/// Analytically evaluates motion under constant acceleration on one axis.
/// This component does not process collisions, inputs, or rendering.
/// </summary>
public static class ConstantAcceleration
{
    /// <summary>
    /// Evaluates p(t) = p0 + v0*t + (a*t*t)/2 and v(t) = v0 + a*t.
    /// Time is measured from the supplied initial state.
    /// </summary>
    public static MotionSample Evaluate(
        double initialPosition,
        double initialVelocity,
        double acceleration,
        double elapsedSeconds)
    {
        if (!double.IsFinite(initialPosition))
            throw new ArgumentOutOfRangeException(nameof(initialPosition));

        if (!double.IsFinite(initialVelocity))
            throw new ArgumentOutOfRangeException(nameof(initialVelocity));

        if (!double.IsFinite(acceleration))
            throw new ArgumentOutOfRangeException(nameof(acceleration));

        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));

        double position = initialPosition
            + initialVelocity * elapsedSeconds
            + 0.5 * acceleration * elapsedSeconds * elapsedSeconds;

        double velocity = initialVelocity + acceleration * elapsedSeconds;

        if (!double.IsFinite(position) || !double.IsFinite(velocity))
        {
            throw new OverflowException("The evaluated motion is outside the finite double range.");
        }

        return new MotionSample(position, velocity);
    }
}
