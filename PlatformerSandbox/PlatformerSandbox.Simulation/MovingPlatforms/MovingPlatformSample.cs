using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.MovingPlatforms;

/// <summary>
/// Authoritative analytical platform snapshot. Velocity is the instantaneous
/// right-hand velocity at a turnaround; no physics transform parenting exists.
/// </summary>
public readonly record struct MovingPlatformSample(
    int PlatformId, Aabb Bounds, Vector2D Velocity, double TimeSeconds);
