using System;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Media;
using PlatformerSandbox.Foundation.Audio;

namespace PlatformerSandbox.Audio;

/// <summary>Optional native Song backend for future compiled music assets.
/// The A01 test build ships SFX and ambience only, no licensed song.</summary>
internal sealed class MonoGameMusicBackend : IMusicPlaybackBackend
{
    private readonly ContentManager _content;
    private bool _disposed;
    public MonoGameMusicBackend(ContentManager content) => _content = content;

    public void Play(string trackId, bool repeat)
    {
        if (_disposed) return;
        // Music is authored as compiled Songs under Content/Audio. This loader
        // is not used unless a game scene explicitly requests such a track.
        Song song = _content.Load<Song>("Audio/" + trackId);
        MediaPlayer.IsRepeating = repeat;
        MediaPlayer.Play(song);
    }
    public void Stop() { if (!_disposed) MediaPlayer.Stop(); }
    public void Pause() { if (!_disposed) MediaPlayer.Pause(); }
    public void Resume() { if (!_disposed) MediaPlayer.Resume(); }
    public void SetVolume(float gain)
    {
        if (!_disposed) MediaPlayer.Volume = Math.Clamp(gain, 0f, 1f);
    }
    public void Dispose()
    {
        if (_disposed) return;
        MediaPlayer.Stop();
        _disposed = true;
    }
}
