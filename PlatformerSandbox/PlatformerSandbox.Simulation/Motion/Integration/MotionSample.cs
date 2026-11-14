namespace PlatformerSandbox.Simulation.Motion.Integration;

/// <summary>
/// Position and velocity along a single axis at a given instant.
/// Units and positive direction are defined by the caller.
/// </summary>
public readonly record struct MotionSample(double Position, double Velocity);
