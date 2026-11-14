using PlatformerSandbox.Simulation.Motion.Integration;

namespace PlatformerSandbox.Simulation.Motion.Profiles;

/// <summary>
/// Unobstructed 1D analytical trajectory for an input held constant from t=0.
/// At most two constant-acceleration phases are followed by indefinite cruising.
///
/// Phase transition times are explicit, and boundary velocity is set to its
/// exact target to prevent substep-dependent overshoot. Acceleration is
/// right-continuous at transitions; position and velocity remain continuous.
/// No collision, ground contact, rendering, or simulation state is modified.
/// </summary>
public sealed class HorizontalMotionTrajectory
{
    private readonly double _initialPosition;
    private readonly double _initialVelocity;
    private readonly double _firstAcceleration;
    private readonly double _firstDuration;
    private readonly double _firstEndVelocity;
    private readonly double _secondAcceleration;
    private readonly double _secondDuration;
    private readonly double _secondEndVelocity;
    private readonly double _secondStartPosition;
    private readonly double _cruiseStartPosition;
    private readonly double _cruiseVelocity;

    internal HorizontalMotionTrajectory(
        double initialPosition,
        double initialVelocity,
        double firstAcceleration,
        double firstDuration,
        double firstEndVelocity,
        double secondAcceleration,
        double secondDuration,
        double secondEndVelocity,
        double cruiseVelocity)
    {
        if (!AllFinite(initialPosition, initialVelocity, firstAcceleration, firstDuration,
            firstEndVelocity, secondAcceleration, secondDuration, secondEndVelocity, cruiseVelocity) ||
            firstDuration < 0.0 || secondDuration < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(firstDuration),
                "This horizontal trajectory cannot be represented with finite analytical parameters.");
        }

        _initialPosition = initialPosition;
        _initialVelocity = initialVelocity;
        _firstAcceleration = firstAcceleration;
        _firstDuration = firstDuration;
        _firstEndVelocity = firstEndVelocity;
        _secondAcceleration = secondAcceleration;
        _secondDuration = secondDuration;
        _secondEndVelocity = secondEndVelocity;
        _cruiseVelocity = cruiseVelocity;

        _secondStartPosition = ConstantAcceleration.Evaluate(
            initialPosition, initialVelocity, firstAcceleration, firstDuration).Position;
        _cruiseStartPosition = ConstantAcceleration.Evaluate(
            _secondStartPosition, firstEndVelocity, secondAcceleration, secondDuration).Position;
        TimeToCruiseSeconds = firstDuration + secondDuration;
        if (!double.IsFinite(TimeToCruiseSeconds))
            throw new OverflowException("Trajectory transition times overflow double precision.");
    }

    public double TimeToCruiseSeconds { get; }
    public double CruiseVelocity => _cruiseVelocity;

    /// <summary>
    /// Evaluate at any finite, nonnegative time. Motion after reaching its
    /// target speed continues indefinitely at that velocity, without assuming
    /// a wall, edge, or future input event.
    /// </summary>
    public HorizontalMotionSample Evaluate(double elapsedSeconds)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));

        if (elapsedSeconds < _firstDuration)
        {
            MotionSample sample = ConstantAcceleration.Evaluate(
                _initialPosition, _initialVelocity, _firstAcceleration, elapsedSeconds);
            return new HorizontalMotionSample(sample.Position, sample.Velocity, _firstAcceleration);
        }

        if (elapsedSeconds < TimeToCruiseSeconds)
        {
            MotionSample sample = ConstantAcceleration.Evaluate(
                _secondStartPosition, _firstEndVelocity,
                _secondAcceleration, elapsedSeconds - _firstDuration);
            return new HorizontalMotionSample(sample.Position, sample.Velocity, _secondAcceleration);
        }

        MotionSample cruise = ConstantAcceleration.Evaluate(
            _cruiseStartPosition, _secondEndVelocity,
            0.0, elapsedSeconds - TimeToCruiseSeconds);
        return new HorizontalMotionSample(cruise.Position, _cruiseVelocity, 0.0);
    }

    private static bool AllFinite(params double[] values)
    {
        foreach (double value in values)
            if (!double.IsFinite(value)) return false;
        return true;
    }
}
