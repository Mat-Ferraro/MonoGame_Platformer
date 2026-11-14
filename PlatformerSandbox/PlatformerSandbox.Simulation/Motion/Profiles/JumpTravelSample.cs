namespace PlatformerSandbox.Simulation.Motion.Profiles;

/// <summary>
/// Sample of a two-axis unobstructed REFERENCE jump. Horizontal position is
/// absolute in the supplied coordinate system; vertical displacement is
/// relative to takeoff. Positive X is right and positive Y is up.
/// </summary>
public readonly record struct JumpTravelSample(
    double HorizontalPosition,
    double VerticalDisplacement,
    double HorizontalVelocity,
    double VerticalVelocity,
    double HorizontalAcceleration,
    double VerticalAcceleration);
