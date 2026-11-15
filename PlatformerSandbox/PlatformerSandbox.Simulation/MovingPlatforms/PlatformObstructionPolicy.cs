namespace PlatformerSandbox.Simulation.MovingPlatforms;

/// <summary>
/// Kinematic moving platforms follow immutable, author-specified timelines.
/// This policy determines how WORLD obstructions involving those platforms
/// are treated. It is independent of character-crush respawn policy and of
/// numerical fault sensitivity.
/// </summary>
public enum PlatformObstructionPolicy
{
    /// <summary>
    /// Retain authored, unstoppable platform movement, even through other
    /// world geometry. Player collision and crush still resolve normally.
    /// </summary>
    FollowAuthoredTrajectories = 0,

    /// <summary>
    /// Reject a step before committing any player state if a moving platform
    /// would positively overlap a static collider or another moving platform.
    /// Use when level design forbids platform/world interpenetration.
    /// </summary>
    RejectWorldObstructions = 1
}
