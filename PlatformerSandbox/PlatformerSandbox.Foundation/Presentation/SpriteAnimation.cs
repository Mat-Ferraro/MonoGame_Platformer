namespace PlatformerSandbox.Foundation.Presentation;

/// <summary>Presentation-only frame pointer; the rendering adapter determines
/// how a frame ID maps to a sprite atlas, procedural pose, or other visual.</summary>
public readonly record struct SpriteAnimationFrame(int FrameId, double DurationSeconds);

public enum SpriteAnimationMode { Loop, Once, PingPong }

/// <summary>Immutable clip definition with independent per-frame durations.</summary>
public sealed class SpriteAnimationClip
{
    private readonly SpriteAnimationFrame[] _frames;
    private readonly int[] _sequence;
    private readonly double[] _endTimes;

    public string Name { get; }
    public SpriteAnimationMode Mode { get; }
    private readonly IReadOnlyList<SpriteAnimationFrame> _readOnlyFrames;
    public IReadOnlyList<SpriteAnimationFrame> Frames => _readOnlyFrames;
    public double CycleDurationSeconds { get; }

    public SpriteAnimationClip(string name, IReadOnlyList<SpriteAnimationFrame> frames,
        SpriteAnimationMode mode = SpriteAnimationMode.Loop)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Clip name required", nameof(name));
        ArgumentNullException.ThrowIfNull(frames);
        if (frames.Count == 0) throw new ArgumentException("Clip must contain frames", nameof(frames));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        Name = name;
        Mode = mode;
        _frames = new SpriteAnimationFrame[frames.Count];
        for (int i = 0; i < frames.Count; i++)
        {
            SpriteAnimationFrame frame = frames[i];
            if (frame.FrameId < 0 || !double.IsFinite(frame.DurationSeconds) ||
                frame.DurationSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(frames),
                    "Frame IDs must be nonnegative, and frame durations finite and positive.");
            _frames[i] = frame;
        }
        _readOnlyFrames = Array.AsReadOnly(_frames);
        int sequenceLength = mode == SpriteAnimationMode.PingPong && frames.Count > 2
            ? 2 * frames.Count - 2 : frames.Count;
        _sequence = new int[sequenceLength];
        _endTimes = new double[sequenceLength];
        double elapsed = 0;
        for (int i = 0; i < sequenceLength; i++)
        {
            int frameIndex = i < frames.Count ? i : 2 * frames.Count - 2 - i;
            _sequence[i] = frameIndex;
            elapsed += _frames[frameIndex].DurationSeconds;
            if (!double.IsFinite(elapsed))
                throw new ArgumentOutOfRangeException(nameof(frames), "Total duration overflow.");
            _endTimes[i] = elapsed;
        }
        CycleDurationSeconds = elapsed;
    }

    internal int FindFrameIndex(double time)
    {
        // Boundary at the next frame; exact cycle end is handled by player.
        for (int i = 0; i < _endTimes.Length; i++)
            if (time < _endTimes[i]) return _sequence[i];
        return _sequence[^1];
    }

    internal SpriteAnimationFrame FrameAt(int index) => _frames[index];
    internal int LastIndex => _frames.Length - 1;
}

/// <summary>Independent visual timeline. Never produces simulation commands
/// or mutates gameplay state. SetClip is stable when given the same object.</summary>
public sealed class SpriteAnimationPlayer
{
    public SpriteAnimationClip Clip { get; private set; }
    public double ElapsedSeconds { get; private set; }
    public bool IsFinished { get; private set; }
    public int FrameIndex => IsFinished && Clip.Mode == SpriteAnimationMode.Once
        ? Clip.LastIndex : Clip.FindFrameIndex(ElapsedSeconds);
    public SpriteAnimationFrame CurrentFrame => Clip.FrameAt(FrameIndex);

    public SpriteAnimationPlayer(SpriteAnimationClip clip) =>
        Clip = clip ?? throw new ArgumentNullException(nameof(clip));

    public void SetClip(SpriteAnimationClip clip, bool restart = false)
    {
        ArgumentNullException.ThrowIfNull(clip);
        if (ReferenceEquals(clip, Clip) && !restart) return;
        Clip = clip;
        ElapsedSeconds = 0;
        IsFinished = false;
    }

    public void Advance(double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (deltaSeconds == 0 || IsFinished) return;
        double duration = Clip.CycleDurationSeconds;
        if (Clip.Mode == SpriteAnimationMode.Once)
        {
            if (deltaSeconds >= duration - ElapsedSeconds)
            {
                ElapsedSeconds = duration;
                IsFinished = true;
            }
            else ElapsedSeconds += deltaSeconds;
        }
        else
        {
            double remainder = deltaSeconds % duration;
            ElapsedSeconds = (ElapsedSeconds + remainder) % duration;
        }
    }
}
