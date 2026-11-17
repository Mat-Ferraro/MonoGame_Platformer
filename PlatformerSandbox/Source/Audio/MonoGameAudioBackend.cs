using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Audio;
using PlatformerSandbox.Foundation.Audio;

namespace PlatformerSandbox.Audio;

/// <summary>DesktopGL-native SFX backend. WAVs are short original A01 test cues.
/// SoundEffect resources are shared; each playback owns its own instance.</summary>
internal sealed class MonoGameAudioBackend : IAudioPlaybackBackend
{
    private readonly Dictionary<string, SoundEffect> _effects = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public MonoGameAudioBackend(string assetDirectory, AudioCueCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        try
        {
            foreach (string sample in catalog.Cues.SelectMany(c => c.Samples).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string path = Path.Combine(assetDirectory, sample + ".wav");
                using var stream = File.OpenRead(path);
                _effects.Add(sample, SoundEffect.FromStream(stream));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public IAudioVoice? Play(string sampleId, float volume, float pitch, bool loop)
    {
        if (_disposed || !_effects.TryGetValue(sampleId, out SoundEffect? effect)) return null;
        SoundEffectInstance instance = effect.CreateInstance();
        try
        {
            instance.Volume = Math.Clamp(volume, 0, 1);
            instance.Pitch = Math.Clamp(pitch, -1, 1);
            instance.IsLooped = loop;
            instance.Play();
            return new MonoGameAudioVoice(instance);
        }
        catch
        {
            instance.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        foreach (SoundEffect effect in _effects.Values) effect.Dispose();
        _effects.Clear();
        _disposed = true;
    }

    private sealed class MonoGameAudioVoice : IAudioVoice
    {
        private readonly SoundEffectInstance _instance;
        private bool _disposed;
        public MonoGameAudioVoice(SoundEffectInstance instance) => _instance = instance;
        public bool IsPlaying => !_disposed && _instance.State == SoundState.Playing;
        public void SetVolume(float volume)
        {
            if (!_disposed) _instance.Volume = Math.Clamp(volume, 0, 1);
        }
        public void SetPan(float pan)
        {
            if (!_disposed) _instance.Pan = Math.Clamp(pan, -1, 1);
        }
        public void Stop()
        {
            if (!_disposed) _instance.Stop();
        }
        public void Dispose()
        {
            if (_disposed) return;
            _instance.Stop();
            _instance.Dispose();
            _disposed = true;
        }
    }
}
