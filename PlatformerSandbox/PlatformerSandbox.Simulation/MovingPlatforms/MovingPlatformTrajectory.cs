using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.MovingPlatforms;

public enum PlatformPlaybackCommand { Pause, Resume, Reverse }

/// <summary>
/// An exactly evaluable, periodic, constant-speed ping-pong platform path.
/// Every reversal is a named analytical event. The caller, not the platform,
/// controls the authoritative simulation clock.
/// </summary>
public sealed class MovingPlatformTrajectory
{
    public MovingPlatformTrajectory(
        int id, Aabb initialBounds, Vector2D travelOffset,
        double oneWayDurationSeconds)
    {
        if (id < 0)
            throw new ArgumentOutOfRangeException(nameof(id));
        if (!initialBounds.IsValid)
            throw new ArgumentException("A valid platform AABB is required.", nameof(initialBounds));
        if (travelOffset == Vector2D.Zero)
            throw new ArgumentException("Stationary platforms belong in the static collider set.",
                nameof(travelOffset));
        if (!double.IsFinite(oneWayDurationSeconds) || oneWayDurationSeconds <= 0.0 ||
            !double.IsFinite(2.0 * oneWayDurationSeconds))
            throw new ArgumentOutOfRangeException(nameof(oneWayDurationSeconds));

        // Validate the entire path, not just its starting box and speed.
        _ = initialBounds.Translated(travelOffset);
        Vector2D travelVelocity = travelOffset * (1.0 / oneWayDurationSeconds);

        Id = id;
        InitialBounds = initialBounds;
        TravelOffset = travelOffset;
        OneWayDurationSeconds = oneWayDurationSeconds;
        TravelVelocity = travelVelocity;
    }

    // A transient, solver-recorded path for a dynamic support. It is not authored
    // playback and is valid only for the staged fixed tick.
    private readonly (double Time, Aabb Bounds)[]? _tickMotion;
    private MovingPlatformTrajectory(int id, (double Time, Aabb Bounds)[] points)
    {
        Id=id; _tickMotion=points; InitialBounds=points[0].Bounds;
        OneWayDurationSeconds=points[^1].Time-points[0].Time;
        TravelOffset=points[^1].Bounds.Min-points[0].Bounds.Min;
        TravelVelocity=TravelOffset*(1.0/OneWayDurationSeconds);
    }
    public static MovingPlatformTrajectory FromTickMotion(int id, IReadOnlyList<(double Time,Aabb Bounds)> points)
    {
        ArgumentNullException.ThrowIfNull(points);
        if(id<0||points.Count<2)throw new ArgumentException("Invalid tick motion.");
        for(int i=0;i<points.Count;i++)
            if(!double.IsFinite(points[i].Time)||points[i].Time<0||!points[i].Bounds.IsValid||
                (i>0&&points[i].Time<=points[i-1].Time))throw new ArgumentException("Invalid tick motion point.");
        return new MovingPlatformTrajectory(id,points.ToArray());
    }
    private int TickLeg(double time)
    {
        var points=_tickMotion!;
        for(int i=1;i<points.Length-1;i++)if(time<points[i].Time)return i-1;
        return points.Length-2;
    }
    public bool IsTickMotion => _tickMotion is not null;
    public int Id { get; }
    public Aabb InitialBounds { get; }
    public Vector2D TravelOffset { get; }
    public Vector2D TravelVelocity { get; }
    public double OneWayDurationSeconds { get; }
    public double PeriodSeconds => 2.0 * OneWayDurationSeconds;

    // Authored paths remain immutable. Each WorldSession owns a separate runtime copy.
    private readonly List<PlaybackSegment> _playback = new();
    private bool _runtimeCopy;
    private readonly record struct PlaybackSegment(double Start, double LocalPhase, bool Paused, Aabb Anchor);
    public MovingPlatformTrajectory CreateRuntimeCopy() => new(Id, InitialBounds, TravelOffset, OneWayDurationSeconds) { _runtimeCopy = true };
    /// <summary>Capture playback only, for a staged world tick which may retry with a blocked path.
    /// Invoke the returned restore action if the entire transaction fails.</summary>
    public Action CapturePlaybackRollback()
    {
        if(!_runtimeCopy)throw new InvalidOperationException("Only runtime playback is transactional.");
        var prior=_playback.ToArray();
        return ()=>{_playback.Clear();_playback.AddRange(prior);};
    }
    public bool IsPaused(double elapsedSeconds) => PlaybackIndex(elapsedSeconds) is int i && _playback[i].Paused;

    private int? PlaybackIndex(double time)
    {
        if (!double.IsFinite(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));
        for (int i = _playback.Count - 1; i >= 0; --i)
            if (_playback[i].Start <= time) return i;
        return null;
    }

    private double LocalPhase(double time, int? index) => index is int i
        ? Phase(_playback[i].LocalPhase + (_playback[i].Paused ? 0 : time - _playback[i].Start), _playback[i].Paused ? null : time) : Phase(time);

    /// <summary>Commands are applied only at committed world tick boundaries, before the next Step.
    /// Never call during an in-flight motor query. Historical segments support analytical queries.
    /// </summary>
    public void ApplyPlayback(PlatformPlaybackCommand command, double elapsedSeconds)
    {
        if (!_runtimeCopy) throw new InvalidOperationException("Only a world-owned runtime path may be controlled.");
        if (!Enum.IsDefined(command)) throw new ArgumentOutOfRangeException(nameof(command));
        int? index = PlaybackIndex(elapsedSeconds);
        if (_playback.Count > 0 && elapsedSeconds < _playback[^1].Start)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds), "Playback commands must be chronological.");
        bool paused = index is int i && _playback[i].Paused;
        if ((command == PlatformPlaybackCommand.Pause && paused) || (command == PlatformPlaybackCommand.Resume && !paused)) return;
        var anchor = Evaluate(elapsedSeconds).Bounds;
        double phase = LocalPhase(elapsedSeconds, index);
        if (command == PlatformPlaybackCommand.Reverse)
        {
            // At endpoints only the inward leg is legal; reverse there is a no-op.
            if (phase == 0 || phase == OneWayDurationSeconds) return;
            phase = PeriodSeconds - phase;
        }
        else paused = command == PlatformPlaybackCommand.Pause;
        var segment = new PlaybackSegment(elapsedSeconds, phase, paused, anchor);
        if (_playback.Count > 0 && _playback[^1].Start == elapsedSeconds) _playback[^1] = segment;
        else _playback.Add(segment);
    }

    public MovingPlatformSample Evaluate(double elapsedSeconds)
    {
        if(_tickMotion is { } points)
        {
            int legIndex = TickLeg(elapsedSeconds);
            var a = points[legIndex];
            var b = points[legIndex + 1];
            var supportVelocity = (b.Bounds.Min - a.Bounds.Min) * (1.0 / (b.Time - a.Time));
            var bounds = elapsedSeconds <= a.Time ? a.Bounds : elapsedSeconds >= b.Time ? b.Bounds :
                a.Bounds.Translated(supportVelocity * (elapsedSeconds - a.Time));
            return new(Id, bounds, supportVelocity, elapsedSeconds);
        }
        int? index = PlaybackIndex(elapsedSeconds);
        double phase = LocalPhase(elapsedSeconds, index);
        bool outbound = phase < OneWayDurationSeconds;
        double progress = outbound
            ? phase / OneWayDurationSeconds
            : (PeriodSeconds - phase) / OneWayDurationSeconds;

        // Endpoint snap: exactly reach the prescribed bounding boxes at
        // each reversal, including t = one-way and t = full period.
        Vector2D offset = progress == 0.0 ? Vector2D.Zero :
            progress == 1.0 ? TravelOffset : TravelOffset * progress;
        Vector2D velocity = outbound ? TravelVelocity : -TravelVelocity;
        if (index is int i)
        {
            var segment = _playback[i];
            if (segment.Paused) return new(Id, segment.Anchor, Vector2D.Zero, elapsedSeconds);
            // Preserve the exact contacting face at a command boundary, rather than recomputing
            // an equivalent reflected phase with potentially different floating-point rounding.
            if (elapsedSeconds == segment.Start) return new(Id, segment.Anchor, velocity, elapsedSeconds);
        }
        return new MovingPlatformSample(Id, InitialBounds.Translated(offset), velocity, elapsedSeconds);
    }

    /// <summary>
    /// Time to the NEXT velocity-discontinuity event, strictly positive (infinity while paused with no known resume).
    /// A call at a turnaround returns one full one-way traversal.
    /// </summary>
    public double TimeUntilNextTurn(double elapsedSeconds)
    {
        if(_tickMotion is { } points)
        {
            foreach(var point in points)if(point.Time>elapsedSeconds)return point.Time-elapsedSeconds;
            return double.PositiveInfinity;
        }
        int? index = PlaybackIndex(elapsedSeconds);
        double phase = LocalPhase(elapsedSeconds, index);
        double remaining = phase < OneWayDurationSeconds
            ? OneWayDurationSeconds - phase
            : PeriodSeconds - phase;
        if (index is int i && _playback[i].Paused) remaining = double.PositiveInfinity;
        int next = (index ?? -1) + 1;
        if (next < _playback.Count) remaining = Math.Min(remaining, _playback[next].Start - elapsedSeconds);
        if (!(remaining > 0.0))
            throw new InvalidOperationException("Platform event cannot advance time.");
        return remaining;
    }

    private double Phase(double elapsedSeconds, double? absoluteTime = null)
    {
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0.0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        double phase = elapsedSeconds % PeriodSeconds;

        // Modulo and repeated fixed-step clock additions need not produce the
        // same floating-point representation of an exact turnaround. At the
        // third 2.4-second turnaround, for example, 7.199999999999999 % 4.8
        // is just below 2.4. The remaining event time can then be smaller
        // than one representable increment of the absolute simulation clock.
        // Snap ONLY within a few ULPs of a known analytical event. This also
        // keeps Evaluate and TimeUntilNextTurn on the same side of the event.
        double ulp = Math.BitIncrement(elapsedSeconds) - elapsedSeconds;
        if (absoluteTime is double world) ulp = Math.Max(ulp, Math.BitIncrement(world) - world);
        double tolerance = Math.Min(OneWayDurationSeconds / 1024.0, 4.0 * ulp);
        if (Math.Abs(phase - OneWayDurationSeconds) <= tolerance)
            return OneWayDurationSeconds;
        if (phase <= tolerance || PeriodSeconds - phase <= tolerance)
            return 0.0;
        return phase;
    }
}
