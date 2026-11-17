using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework.Audio;
using PlatformerSandbox.Foundation.Audio;

namespace PlatformerSandbox.Audio;

/// <summary>
/// A02 playable WAV placeholder music. For long production tracks replace this
/// with a streaming backend; short, pre-deployed PCM test loops are fully owned.
/// No compiled Song/XNB dependency.
/// </summary>
internal sealed class MonoGameWavMusicBackend : IMusicPlaybackBackend
{
    private readonly string _folder;
    private readonly Dictionary<string, SoundEffect> _tracks = new(StringComparer.OrdinalIgnoreCase);
    private SoundEffectInstance? _voice;
    private float _volume;
    private bool _disposed;

    public MonoGameWavMusicBackend(string folder) => _folder = folder ??
        throw new ArgumentNullException(nameof(folder));

    public void Play(string trackId, bool repeat)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(MonoGameWavMusicBackend));
        if (!AudioCueDefinition.IsAssetId(trackId))
            throw new ArgumentException("Invalid music track ID", nameof(trackId));
        if (!_tracks.TryGetValue(trackId, out SoundEffect? effect))
        {
            using var file = File.OpenRead(Path.Combine(_folder, trackId + ".wav"));
            effect = SoundEffect.FromStream(file);
            _tracks.Add(trackId, effect);
        }
        CloseVoice();
        var next = effect.CreateInstance();
        try
        {
            next.IsLooped = repeat;
            next.Volume = _volume;
            next.Play();
            _voice = next;
        }
        catch
        {
            next.Dispose();
            throw;
        }
    }
    public void Stop() => CloseVoice();
    public void Pause()
    {
        if (_voice is { State: SoundState.Playing } voice) voice.Pause();
    }
    public void Resume()
    {
        if (_voice is { State: SoundState.Paused } voice) voice.Resume();
    }
    public void SetVolume(float gain)
    {
        _volume = Math.Clamp(gain, 0f, 1f);
        if (_voice is not null) _voice.Volume = _volume;
    }
    private void CloseVoice()
    {
        if (_voice is null) return;
        _voice.Stop();
        _voice.Dispose();
        _voice = null;
    }
    public void Dispose()
    {
        if (_disposed) return;
        CloseVoice();
        foreach (SoundEffect track in _tracks.Values) track.Dispose();
        _tracks.Clear();
        _disposed = true;
    }
}
