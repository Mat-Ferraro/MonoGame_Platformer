namespace PlatformerSandbox.Gameplay.World;

/// <summary>
/// Stable player identity is separate from character identity, physical
/// input source ID, and a named authored spawn. InputSourceId is optional
/// for disconnected/scripted players; the world never polls devices.
/// </summary>
public readonly record struct WorldPlayerSlotRegistration(
    int PlayerId, int CharacterId, string SpawnId, int? InputSourceId = null);

/// <summary>Read-only current registration; a player may change devices or spawn.</summary>
public readonly record struct WorldPlayerSlotInfo(
    int PlayerId, int CharacterId, string? SpawnId, int? InputSourceId);
