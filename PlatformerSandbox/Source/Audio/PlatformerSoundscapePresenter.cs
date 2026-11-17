using System;
using PlatformerSandbox.Foundation.Audio;

namespace PlatformerSandbox.Audio;

/// <summary>Project-specific level-to-audio adapter; the reusable policy knows no LDtk.</summary>
internal sealed class PlatformerSoundscapePresenter
{
    private readonly AudioService _audio;
    private readonly MusicDirector _music;
    private readonly AudioSoundscapeCatalog _catalog;
    private string? _lastScene;

    public PlatformerSoundscapePresenter(AudioService audio, MusicDirector music,
        AudioSoundscapeCatalog catalog)
    {
        _audio = audio ?? throw new ArgumentNullException(nameof(audio));
        _music = music ?? throw new ArgumentNullException(nameof(music));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public void ChangeScene(string sceneId)
    {
        if (string.Equals(sceneId, _lastScene, StringComparison.OrdinalIgnoreCase)) return;
        _lastScene = sceneId;
        _audio.StopOwner("level");
        if (!_catalog.TryGet(sceneId, out AudioSoundscape? soundscape))
        {
            _music.Request(null, .6);
            return;
        }
        foreach (AudioSoundscapeEmitter emitter in soundscape.Emitters)
        {
            AudioPlayResult result = _audio.Play(new AudioCueRequest(emitter.CueId,
                Owner: "level", SourceId: sceneId + "/" + emitter.Id,
                Spatial: emitter.Position));
            if (result is AudioPlayResult.UnknownCue or AudioPlayResult.BackendUnavailable)
                System.Diagnostics.Debug.WriteLine("A02 environmental emitter not played: " +
                    sceneId + "/" + emitter.Id + " result=" + result);
        }
        _music.Request(soundscape.TrackId, .7);
    }
}
