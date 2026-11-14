namespace PlatformerSandbox.Simulation.Motion.Profiles;

/// <summary>
/// One-dimensional, positive-right sample with position measured in world units.
/// </summary>
public readonly record struct HorizontalMotionSample(
    double Position,
    double Velocity,
    double Acceleration);
