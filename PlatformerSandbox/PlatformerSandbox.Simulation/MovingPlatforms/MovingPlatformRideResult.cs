using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.MovingPlatforms;

/// <summary>
/// Proposed authoritative body state from relative platform motion.
/// If support is lost, only the supported portion is advanced; the caller must
/// integrate the remaining time as free motion with its collision policies.
/// </summary>
public readonly record struct MovingPlatformRideResult(
    Aabb Bounds, Vector2D WorldVelocity, double AdvancedSeconds,
    bool IsSupported, double? DepartureSeconds);
