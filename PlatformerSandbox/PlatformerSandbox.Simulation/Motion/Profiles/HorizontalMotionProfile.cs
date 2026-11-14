namespace PlatformerSandbox.Simulation.Motion.Profiles;

/// <summary>
/// Solved, immutable motion rules. Different instances can represent ground,
/// air, ice, or other surfaces; this class does not choose which is active.
/// </summary>
public sealed class HorizontalMotionProfile
{
    internal HorizontalMotionProfile(
        double maximumSpeed,
        double accelerationRate,
        double stoppingDecelerationRate,
        double reversalDecelerationRate)
    {
        MaximumSpeed = maximumSpeed;
        AccelerationRate = accelerationRate;
        StoppingDecelerationRate = stoppingDecelerationRate;
        ReversalDecelerationRate = reversalDecelerationRate;
    }

    public double MaximumSpeed { get; }
    public double AccelerationRate { get; }
    public double StoppingDecelerationRate { get; }
    public double ReversalDecelerationRate { get; }

    /// <summary>
    /// Constructs a complete analytical position/velocity curve for a fixed
    /// input direction, starting at an arbitrary finite velocity. A new input
    /// or external movement event starts a new curve from its current sample.
    /// Initial overspeed is decelerated, never clamped instantaneously.
    /// </summary>
    public HorizontalMotionTrajectory CreateTrajectory(
        double initialPosition,
        double initialVelocity,
        HorizontalDirection direction)
    {
        if (!double.IsFinite(initialPosition))
            throw new ArgumentOutOfRangeException(nameof(initialPosition));
        if (!double.IsFinite(initialVelocity))
            throw new ArgumentOutOfRangeException(nameof(initialVelocity));
        if (direction is not (HorizontalDirection.Left or HorizontalDirection.Neutral or HorizontalDirection.Right))
            throw new ArgumentOutOfRangeException(nameof(direction));

        double dir = (int)direction;
        if (dir == 0.0)
        {
            double duration = Math.Abs(initialVelocity) / StoppingDecelerationRate;
            double acceleration = -Math.Sign(initialVelocity) * StoppingDecelerationRate;
            return new HorizontalMotionTrajectory(initialPosition, initialVelocity,
                acceleration, duration, 0.0,
                0.0, 0.0, 0.0,
                0.0);
        }

        double target = dir * MaximumSpeed;
        double directedVelocity = initialVelocity * dir;

        if (directedVelocity < 0.0)
        {
            // Opposite input: reverse/brake to rest, then accelerate to target.
            double reverseDuration = Math.Abs(initialVelocity) / ReversalDecelerationRate;
            double accelerateDuration = MaximumSpeed / AccelerationRate;
            return new HorizontalMotionTrajectory(initialPosition, initialVelocity,
                dir * ReversalDecelerationRate, reverseDuration, 0.0,
                dir * AccelerationRate, accelerateDuration, target,
                target);
        }

        if (directedVelocity > MaximumSpeed)
        {
            // Overspeed (e.g. after knockback): recover smoothly to input target.
            double duration = (directedVelocity - MaximumSpeed) / StoppingDecelerationRate;
            return new HorizontalMotionTrajectory(initialPosition, initialVelocity,
                -dir * StoppingDecelerationRate, duration, target,
                0.0, 0.0, target,
                target);
        }

        // Accelerate from rest or from a sub-target speed, then cruise.
        double accelerateTime = (MaximumSpeed - directedVelocity) / AccelerationRate;
        return new HorizontalMotionTrajectory(initialPosition, initialVelocity,
            dir * AccelerationRate, accelerateTime, target,
            0.0, 0.0, target,
            target);
    }
}
