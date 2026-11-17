namespace PlatformerSandbox.Foundation.Audio;

/// <summary>Single-track music backend; the MonoGame adapter uses Song/MediaPlayer.</summary>
public interface IMusicPlaybackBackend : IDisposable
{
    void Play(string trackId, bool repeat);
    void Stop();
    void Pause();
    void Resume();
    void SetVolume(float gain);
}

/// <summary>
/// Single-track fade-out / fade-in director. Repeated requests for the active
/// track are stable. It never overlaps two songs; adaptive stems are deferred.
/// The game owns the backend and chooses whether music continues during pause.
/// </summary>
public sealed class MusicDirector : IDisposable
{
    private readonly IMusicPlaybackBackend _backend;
    private string? _active;
    private string? _requested;
    private double _fadeDuration;
    private double _fadeRemaining;
    private bool _fadeOut;
    // Normalized fade phase, separated from the master/category bus gain.
    private float _outputFactor = 1f;
    private float _fadeFrom = 1f;
    private bool _paused;
    private bool _disposed;
    private float _busGain = 1;

    public string? ActiveTrack => _active;
    public string? RequestedTrack => _requested;
    public bool IsPaused => _paused;

    public MusicDirector(IMusicPlaybackBackend backend) =>
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));

    public void Request(string? trackId, double fadeSeconds = 0.5)
    {
        ThrowIfDisposed();
        if (trackId is not null && !AudioCueDefinition.IsAssetId(trackId))
            throw new ArgumentException("Invalid music ID", nameof(trackId));
        if (!double.IsFinite(fadeSeconds) || fadeSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(fadeSeconds));
        if (trackId == _requested || (trackId == _active && !_fadeOut)) return;
        _requested = trackId;
        _fadeDuration = fadeSeconds;
        _fadeRemaining = fadeSeconds;
        _fadeFrom = _outputFactor;
        _fadeOut = _active is not null;
        if (fadeSeconds == 0)
        {
            SwitchTracks();
            _outputFactor = _active is null ? 0f : 1f;
            ApplyVolume();
        }
        else if (!_fadeOut)
        {
            SwitchTracks();
            _outputFactor = 0;
            ApplyVolume();
        }
        else
            ApplyVolume(); // Preserve the current fade gain on retarget.
    }

    public void SetBusGain(float gain)
    {
        ThrowIfDisposed();
        if (!float.IsFinite(gain) || gain < 0 || gain > 1)
            throw new ArgumentOutOfRangeException(nameof(gain));
        _busGain = gain;
        ApplyVolume();
    }

    public void Pause()
    {
        ThrowIfDisposed();
        if (_paused) return;
        _paused = true;
        if (_active is not null) _backend.Pause();
    }
    public void Resume()
    {
        ThrowIfDisposed();
        if (!_paused) return;
        _paused = false;
        if (_active is not null) _backend.Resume();
    }

    public void Update(double deltaSeconds)
    {
        ThrowIfDisposed();
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (_paused || _active is null || _fadeDuration == 0) return;
        if (_fadeRemaining > 0) _fadeRemaining = Math.Max(0, _fadeRemaining - deltaSeconds);
        if (_fadeOut)
        {
            _outputFactor = _fadeDuration <= 0 ? 0f :
                _fadeFrom * (float)(_fadeRemaining / _fadeDuration);
            if (_fadeRemaining == 0)
            {
                SwitchTracks();
                _fadeOut = false;
                _fadeRemaining = _active is null ? 0 : _fadeDuration;
                _outputFactor = 0;
            }
        }
        else
            _outputFactor = _fadeRemaining <= 0 ? 1f :
                (float)(1 - _fadeRemaining / _fadeDuration);
        ApplyVolume();
    }

    private void SwitchTracks()
    {
        if (_active is not null) _backend.Stop();
        _active = _requested;
        if (_active is not null)
        {
            _backend.Play(_active, repeat: true);
            if (_paused) _backend.Pause();
        }
    }

    private void ApplyVolume()
    {
        _backend.SetVolume(_active is null ? 0 :
            Math.Clamp(_outputFactor * _busGain, 0, 1));
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
    public void Dispose()
    {
        if (_disposed) return;
        _backend.Stop();
        _backend.Dispose();
        _disposed = true;
    }
}
