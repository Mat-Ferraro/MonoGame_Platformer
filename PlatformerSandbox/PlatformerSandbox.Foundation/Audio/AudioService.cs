namespace PlatformerSandbox.Foundation.Audio;

/// <summary>Platform adapter owns sound hardware and native voices.</summary>
public interface IAudioVoice : IDisposable
{
    bool IsPlaying { get; }
    void SetVolume(float volume);
    // Optional for backend adapters without stereo pan support.
    void SetPan(float pan) { }
    void Stop();
}

public interface IAudioPlaybackBackend : IDisposable
{
    IAudioVoice? Play(string sampleId, float volume, float pitch, bool loop);
}

public enum AudioPlayResult
{
    Played,
    UnknownCue,
    DuplicateEvent,
    Cooldown,
    VoiceLimit,
    Muted,
    BackendUnavailable
}

/// <summary>Semantic cue request with optional stable post-commit event key.</summary>
public readonly record struct AudioCueRequest(string CueId, string Owner = "level",
    string SourceId = "", string EventKey = "", AudioEmitterPosition? Spatial = null);

/// <summary>
/// Headless admission, variation, mixing, ownership and duplicate suppression.
/// Presentation-only: never called by simulation and never mutates gameplay state.
/// Caller uses one thread (normal MonoGame Update). Randomness is audio-owned.
/// </summary>
public sealed class AudioService : IDisposable
{
    private sealed record ActiveVoice(AudioCueDefinition Cue, string Owner,
        IAudioVoice Voice, long Sequence, AudioEmitterPosition? Spatial);

    private readonly AudioCueCatalog _catalog;
    private readonly IAudioPlaybackBackend _backend;
    private readonly Random _audioRandom;
    private readonly List<ActiveVoice> _active = new();
    private readonly Dictionary<(string Cue, string Source), double> _cooldowns = new();
    private readonly Queue<string> _recentEventQueue = new();
    private readonly HashSet<string> _recentEventSet = new(StringComparer.Ordinal);
    private readonly int _maxVoices;
    private long _nextSequence;
    private double _time;
    private bool _disposed;
    private bool _worldPaused;
    private AudioListener? _listener;
    public AudioMixer Mixer { get; } = new();
    public int ActiveVoiceCount => _active.Count;
    public int DeniedVoiceCount { get; private set; }
    public bool IsMuted { get; private set; }

    public AudioService(AudioCueCatalog catalog, IAudioPlaybackBackend backend,
        int maxVoices = 16, int? randomSeed = null)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        if (maxVoices < 1 || maxVoices > 256)
            throw new ArgumentOutOfRangeException(nameof(maxVoices));
        _maxVoices = maxVoices;
        _audioRandom = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
    }

    public void SetMute(bool mute)
    {
        ThrowIfDisposed();
        IsMuted = mute;
        UpdateMix();
    }

    public void SetWorldPaused(bool paused)
    {
        ThrowIfDisposed();
        _worldPaused = paused;
        UpdateMix();
    }

    /// <summary>Update the listener using the last committed player position.
    /// Cosmetic mix updates must never mutate world or authoritative physics.</summary>
    public void SetListener(AudioListener? listener)
    {
        ThrowIfDisposed();
        if (listener is AudioListener ear && (!double.IsFinite(ear.X) ||
            !double.IsFinite(ear.Y)))
            throw new ArgumentOutOfRangeException(nameof(listener));
        _listener = listener;
        UpdateMix();
    }

    public void Update(double deltaSeconds)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        _time += deltaSeconds;
        PruneCompleted();
    }

    public AudioPlayResult Play(AudioCueRequest request)
    {
        ThrowIfDisposed();
        if (!_catalog.TryGet(request.CueId, out AudioCueDefinition cue))
            return AudioPlayResult.UnknownCue;
        // Long-lived loops must still be created while the world is paused or
        // muted, so unpausing/unmuting does not leave ambience permanently absent.
        // One-shots are suppressed while inaudible.
        AudioSpatialMix spatial = AudioSpatializer.Evaluate(_listener, request.Spatial);
        float effectiveGain = (IsMuted ? 0 :
            Mixer.EffectiveGain(cue.Category, cue.Gain, _worldPaused)) * spatial.Gain;
        if (effectiveGain <= 0 && !cue.Loop)
            return AudioPlayResult.Muted;
        if (!string.IsNullOrEmpty(request.EventKey) && _recentEventSet.Contains(request.EventKey))
            return AudioPlayResult.DuplicateEvent;
        string source = request.SourceId ?? "";
        var cooldownKey = (cue.Id, source);
        if (cue.CooldownSeconds > 0 && _cooldowns.TryGetValue(cooldownKey, out double last) &&
            _time - last < cue.CooldownSeconds)
            return AudioPlayResult.Cooldown;
        PruneCompleted();
        if (_active.Count(v => v.Cue.Id.Equals(cue.Id, StringComparison.OrdinalIgnoreCase)) >= cue.MaxVoices)
        {
            DeniedVoiceCount++;
            return AudioPlayResult.VoiceLimit;
        }
        if (_active.Count >= _maxVoices)
        {
            ActiveVoice? victim = _active.Where(v => v.Cue.Priority < cue.Priority)
                .OrderBy(v => v.Cue.Priority).ThenBy(v => v.Sequence).FirstOrDefault();
            if (victim is null)
            {
                DeniedVoiceCount++;
                return AudioPlayResult.VoiceLimit;
            }
            CloseVoice(victim);
        }

        string sample = cue.Samples[_audioRandom.Next(cue.Samples.Count)];
        float pitch = cue.PitchMinimum + (float)_audioRandom.NextDouble() *
            (cue.PitchMaximum - cue.PitchMinimum);
        IAudioVoice? voice;
        try
        {
            voice = _backend.Play(sample, effectiveGain, pitch, cue.Loop);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Recoverable cosmetic failure. Host can use its D01 diagnostics
            // to investigate missing/unsupported audio hardware or assets.
            System.Diagnostics.Debug.WriteLine("A01 audio voice rejected by backend: " + ex);
            return AudioPlayResult.BackendUnavailable;
        }
        if (voice is null) return AudioPlayResult.BackendUnavailable;
        try { voice.SetPan(spatial.Pan); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine("A02 audio pan rejected by backend: " + ex);
            voice.Dispose();
            return AudioPlayResult.BackendUnavailable;
        }
        _active.Add(new ActiveVoice(cue, request.Owner ?? "level", voice,
            ++_nextSequence, request.Spatial));
        _cooldowns[cooldownKey] = _time;
        if (!string.IsNullOrEmpty(request.EventKey)) RememberEvent(request.EventKey);
        return AudioPlayResult.Played;
    }

    public void StopOwner(string owner)
    {
        ThrowIfDisposed();
        for (int i = _active.Count - 1; i >= 0; i--)
            if (string.Equals(_active[i].Owner, owner, StringComparison.Ordinal))
                CloseVoice(_active[i]);
    }

    public void SetMasterVolume(float gain)
    {
        ThrowIfDisposed();
        Mixer.SetMasterVolume(gain);
        UpdateMix();
    }
    public void SetCategoryVolume(AudioCategory category, float gain)
    {
        ThrowIfDisposed();
        Mixer.SetCategoryVolume(category, gain);
        UpdateMix();
    }

    private void RememberEvent(string key)
    {
        if (!_recentEventSet.Add(key)) return;
        _recentEventQueue.Enqueue(key);
        while (_recentEventQueue.Count > 512)
            _recentEventSet.Remove(_recentEventQueue.Dequeue());
    }

    private void PruneCompleted()
    {
        for (int i = _active.Count - 1; i >= 0; i--)
            if (!_active[i].Voice.IsPlaying)
            {
                _active[i].Voice.Dispose();
                _active.RemoveAt(i);
            }
    }

    private void UpdateMix()
    {
        foreach (ActiveVoice active in _active)
        {
            AudioSpatialMix spatial = AudioSpatializer.Evaluate(_listener, active.Spatial);
            active.Voice.SetVolume((IsMuted ? 0 :
                Mixer.EffectiveGain(active.Cue.Category, active.Cue.Gain, _worldPaused)) *
                spatial.Gain);
            active.Voice.SetPan(spatial.Pan);
        }
    }

    private void CloseVoice(ActiveVoice state)
    {
        state.Voice.Stop();
        state.Voice.Dispose();
        _active.Remove(state);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        for (int i = _active.Count - 1; i >= 0; i--) CloseVoice(_active[i]);
        _backend.Dispose();
        _disposed = true;
    }
}
