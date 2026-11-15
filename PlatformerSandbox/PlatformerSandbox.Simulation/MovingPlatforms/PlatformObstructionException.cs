namespace PlatformerSandbox.Simulation.MovingPlatforms;

/// <summary>
/// World-authoring error: two non-character colliders would occupy the same
/// space. This cannot be repaired by moving the character or swallowing a
/// collision. The event time and ordered IDs are deterministic.
/// </summary>
public sealed class PlatformObstructionException : InvalidOperationException
{
    public PlatformObstructionException(PlatformObstruction obstruction)
        : base($"Moving platform {obstruction.MovingPlatformId} obstructed by " +
               $"{(obstruction.OtherIsMoving ? "moving platform" : "static collider")} " +
               $"{obstruction.OtherColliderId} at world time " +
               $"{obstruction.WorldTimeSeconds:R}.")
    {
        Obstruction = obstruction;
    }

    public PlatformObstruction Obstruction { get; }
}

public readonly record struct PlatformObstruction(
    int MovingPlatformId, int OtherColliderId, bool OtherIsMoving,
    double WorldTimeSeconds);
