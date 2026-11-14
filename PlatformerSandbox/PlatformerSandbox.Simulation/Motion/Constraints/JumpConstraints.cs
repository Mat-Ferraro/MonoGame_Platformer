namespace PlatformerSandbox.Simulation.Motion.Constraints;

/// <summary>
/// Designer-facing constraints for a reference vertical jump.
/// Positions and velocities use a positive-up convention.
/// The reference trajectory is unobstructed and assumes no midair input changes.
/// </summary>
/// <param name="Height">Maximum vertical displacement above takeoff, in world units.</param>
/// <param name="RiseTimeSeconds">Time from takeoff to the apex, in seconds.</param>
/// <param name="FallTimeSeconds">Time from the apex to the takeoff height, in seconds.</param>
/// <param name="ApexRegionFraction">
/// Fraction of the jump height measured down from its apex (e.g., 0.10 = top 10%).
/// </param>
/// <param name="TimeNearApexSeconds">
/// Optional total time spent within the apex region across ascent and descent.
/// This is NOT a literal pause at the apex. Null selects constant-acceleration
/// rise/fall segments (the power-curve exponent of 2).
/// </param>
public readonly record struct JumpConstraints(
    double Height,
    double RiseTimeSeconds,
    double FallTimeSeconds,
    double ApexRegionFraction = 0.10,
    double? TimeNearApexSeconds = null);
