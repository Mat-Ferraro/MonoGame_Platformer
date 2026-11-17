using PlatformerSandbox.Foundation.Audio;
using PlatformerSandbox.Gameplay.World;

namespace PlatformerSandbox.Audio;

/// <summary>Game-specific mapping; reusable audio core never references WorldSession.</summary>
internal sealed class PlatformerAudioPresenter
{
    private readonly AudioService _audio;
    public PlatformerAudioPresenter(AudioService audio) => _audio = audio;

    public AudioPlayResult Present(WorldGameplayEvent worldEvent)
    {
        string? cue = worldEvent.Kind switch
        {
            WorldGameplayEventKind.JumpStarted => "player.jump",
            WorldGameplayEventKind.Landed => "player.land",
            WorldGameplayEventKind.WallHit => "player.wall",
            WorldGameplayEventKind.PlayerRespawned => "player.respawn",
            WorldGameplayEventKind.AbilityActivated => "player.ability",
            _ => null
        };
        if (cue is null) return AudioPlayResult.UnknownCue;
        return _audio.Play(new AudioCueRequest(cue, "level",
            "player:" + worldEvent.PlayerId,
            worldEvent.WorldInstanceId + "/" + worldEvent.WorldTick + "/" + worldEvent.Sequence,
            new AudioEmitterPosition(worldEvent.Position.X, worldEvent.Position.Y,
                InnerRadius: 2.5, OuterRadius: 22)));
    }
}
