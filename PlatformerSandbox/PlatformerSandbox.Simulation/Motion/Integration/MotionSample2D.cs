using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Motion.Integration;

/// <summary>Position and velocity of a two-axis analytical motion state.</summary>
public readonly record struct MotionSample2D(Vector2D Position, Vector2D Velocity);
