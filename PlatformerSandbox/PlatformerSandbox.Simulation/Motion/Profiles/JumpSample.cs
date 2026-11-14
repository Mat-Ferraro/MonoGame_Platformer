namespace PlatformerSandbox.Simulation.Motion.Profiles;

/// <summary>
/// An analytical reference-jump sample with upward-positive values.
/// VerticalDisplacement is relative to the launch position.
/// </summary>
public readonly record struct JumpSample(
    double VerticalDisplacement,
    double VerticalVelocity,
    double VerticalAcceleration);
