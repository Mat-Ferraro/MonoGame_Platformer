using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.States;
using PlatformerSandbox.Gameplay.Abilities;

namespace PlatformerSandbox.Gameplay.World;

/// <summary>
/// Read-only, post-commit events for presentation consumers. These are NOT
/// commands, and consumers never feed results back into the physics step.
/// </summary>
public enum WorldGameplayEventKind
{
    PlayerJoined,
    PlayerRemoved,
    PlayerRespawned,
    JumpStarted,
    LeftGround,
    Landed,
    WallHit,
    TriggerEntered,
    TriggerExited,
    CharacterStateChanged,
    CharacterStateRequestDenied,
    AbilityActivated,
    AbilityCompleted,
    AbilityCancelled,
    AbilityInterrupted,
    AbilityRejected,
    EnvironmentMotionChanged,
    HorizontalImpulseApplied
}

/// <summary>
/// WorldInstanceId + WorldTick + Sequence is the stable batch event key.
/// The host gives a fresh WorldInstanceId to every newly constructed world,
/// including full restarts; identical replays use the same explicit ID.
/// Position is an END-OF-TICK world-space point, not necessarily an exact
/// time-of-impact position. Collision and trigger IDs occupy separate spaces.
/// </summary>
public readonly record struct WorldGameplayEvent(
    long WorldInstanceId, long WorldTick, int Sequence,
    WorldGameplayEventKind Kind, int PlayerId, Vector2D Position,
    int? ColliderId = null, int? TriggerId = null, double Intensity = 0.0,
    WorldCharacterStateChange? StateChange = null,
    CharacterStateRequestKind? StateRequestKind = null,
    CharacterStateRejection? Rejection = null,
    CharacterAbilityId? AbilityId = null,
    CharacterAbilityRejection? AbilityRejection = null,
    CharacterAbilityEndReason? AbilityEndReason = null);

/// <summary>
/// Nonblocking, deterministic, data-only overlap volume. A trigger reports
/// boundary entry/exit; it does not apply damage, respawn, or game rules.
/// Swept pass-through events and continuous overlap along the entire tick
/// are deliberately outside this first trigger contract.
/// </summary>
public sealed record WorldTriggerDefinition
{
    public WorldTriggerDefinition(int id, string tag, Aabb bounds)
    {
        if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
        if (string.IsNullOrWhiteSpace(tag) || tag != tag.Trim())
            throw new ArgumentException("Trigger tag must be nonempty and trimmed.", nameof(tag));
        if (!bounds.IsValid)
            throw new ArgumentException("Trigger bounds must be valid.", nameof(bounds));
        Id = id;
        Tag = tag;
        Bounds = bounds;
    }

    public int Id { get; }
    public string Tag { get; }
    public Aabb Bounds { get; }
}
