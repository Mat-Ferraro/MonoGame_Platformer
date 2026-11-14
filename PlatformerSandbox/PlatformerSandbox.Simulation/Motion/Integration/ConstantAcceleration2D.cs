using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Motion.Integration;

/// <summary>
/// Analytical, framework-independent two-axis constant acceleration.
/// Delegates to the already-tested one-axis equations; never time-steps.
/// </summary>
public static class ConstantAcceleration2D
{
    public static MotionSample2D Evaluate(
        Vector2D initialPosition,
        Vector2D initialVelocity,
        Vector2D acceleration,
        double elapsedSeconds)
    {
        MotionSample x = ConstantAcceleration.Evaluate(
            initialPosition.X, initialVelocity.X, acceleration.X, elapsedSeconds);
        MotionSample y = ConstantAcceleration.Evaluate(
            initialPosition.Y, initialVelocity.Y, acceleration.Y, elapsedSeconds);
        return new MotionSample2D(
            new Vector2D(x.Position, y.Position),
            new Vector2D(x.Velocity, y.Velocity));
    }
}
