namespace PlatformerSandbox.Simulation.Motion.Constraints;

/// <summary>
/// Designer-facing target for the SIGNED horizontal displacement of one
/// unobstructed jump under a fixed reference input schedule.
///
/// Horizontal speed is the unknown solved by the inverse calculation. The
/// three time constraints retain their Milestone 8 meanings. The initial
/// velocity is expressed as a fraction of the SOLVED maximum speed to make
/// horizontal displacement scale analytically with maximum speed.
/// </summary>
/// <param name="SignedTargetDistance">Positive right, negative left, in world units. Must be nonzero.</param>
/// <param name="TimeToMaximumSpeedSeconds">Acceleration from rest to full speed.</param>
/// <param name="TimeToStopSeconds">Neutral-input braking from full speed to rest.</param>
/// <param name="TimeToReverseSeconds">Total full-speed-to-opposite-full-speed time.</param>
/// <param name="InitialVelocityFractionOfMaximumSpeed">Signed fraction in [-1, 1].</param>
public readonly record struct JumpTravelDistanceConstraints(
    double SignedTargetDistance,
    double TimeToMaximumSpeedSeconds,
    double TimeToStopSeconds,
    double TimeToReverseSeconds,
    double InitialVelocityFractionOfMaximumSpeed = 0.0);
