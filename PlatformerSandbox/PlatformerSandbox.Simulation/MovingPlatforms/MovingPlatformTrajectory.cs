using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.MovingPlatforms;

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

    public int Id { get; }
    public Aabb InitialBounds { get; }
    public Vector2D TravelOffset { get; }
    public Vector2D TravelVelocity { get; }
    public double OneWayDurationSeconds { get; }
    public double PeriodSeconds => 2.0 * OneWayDurationSeconds;

    public MovingPlatformSample Evaluate(double elapsedSeconds)
    {
        double phase = Phase(elapsedSeconds);
        bool outbound = phase < OneWayDurationSeconds;
        double progress = outbound
            ? phase / OneWayDurationSeconds
            : (PeriodSeconds - phase) / OneWayDurationSeconds;

        // Endpoint snap: exactly reach the prescribed bounding boxes at
        // each reversal, including t = one-way and t = full period.
        Vector2D offset = progress == 0.0 ? Vector2D.Zero :
            progress == 1.0 ? TravelOffset : TravelOffset * progress;
        Vector2D velocity = outbound ? TravelVelocity : -TravelVelocity;
        return new MovingPlatformSample(
            Id, InitialBounds.Translated(offset), velocity, elapsedSeconds);
    }

    /// <summary>
    /// Time to the NEXT velocity-discontinuity event, strictly positive.
    /// A call at a turnaround returns one full one-way traversal.
    /// </summary>
    public double TimeUntilNextTurn(double elapsedSeconds)
    {
        double phase = Phase(elapsedSeconds);
        double remaining = phase < OneWayDurationSeconds
            ? OneWayDurationSeconds - phase
            : PeriodSeconds - phase;
        if (!(remaining > 0.0))
            throw new InvalidOperationException("Platform event cannot advance time.");
        return remaining;
    }

    private double Phase(double elapsedSeconds)
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
        double tolerance = Math.Min(OneWayDurationSeconds / 1024.0, 4.0 * ulp);
        if (Math.Abs(phase - OneWayDurationSeconds) <= tolerance)
            return OneWayDurationSeconds;
        if (phase <= tolerance || PeriodSeconds - phase <= tolerance)
            return 0.0;
        return phase;
    }
}
