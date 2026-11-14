using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Contacts;

namespace PlatformerSandbox.Simulation.MovingPlatforms;

/// <summary>
/// Proposed body state, not a mutation of authoritative motor state.
/// AdvancedSeconds may be less than requested if a wall/ceiling collision
/// or support departure requires a fresh analytical movement segment.
/// </summary>
public readonly record struct MovingPlatformInteractionResult(
    Aabb Bounds,
    Vector2D WorldVelocity,
    double AdvancedSeconds,
    MovingPlatformInteractionPhase Phase,
    MovingPlatformSweepHit? Contact,
    bool LandedThisStep)
{
    public bool IsSupported => Phase == MovingPlatformInteractionPhase.RidingCompleted;
}
