using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.MovingPlatforms;

/// <summary>
/// Regression coverage for representable-time rounding at platform reversals.
/// In particular, 1/60-second updates do not necessarily land exactly on a
/// platform's analytical 2.4-second turn event after repeated clock additions.
/// </summary>
public sealed class MovingPlatformTurnaroundPrecisionTests
{
    private const double FixedStep = 1.0 / 60.0;

    private static MovingPlatformTrajectory Path(double oneWaySeconds = 2.4) =>
        new(7,
            new Aabb(new Vector2D(5.3, 2.05), new Vector2D(7.35, 2.30)),
            new Vector2D(2.4, 0.0), oneWaySeconds);

    private static Aabb Rider(MovingPlatformSample platform) =>
        new(new Vector2D(platform.Bounds.Left + 0.3, platform.Bounds.Top),
            new Vector2D(platform.Bounds.Left + 1.1, platform.Bounds.Top + 1.6));

    private static void Near(double expected, double actual, double tolerance = 1e-9) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, tolerance);

    [Fact]
    public void ClockJustBeforeThirdTurnIsTreatedAsTheTurnEvent()
    {
        var platform = Path();
        // This is the actual rounded absolute clock reached when a 1/60 s
        // interval is split at the third turnaround (7.2 seconds).
        double nearTurn = 7.199999999999999;
        var sample = platform.Evaluate(nearTurn);
        Near(platform.InitialBounds.Left + platform.TravelOffset.X,
            sample.Bounds.Left);
        Near(-1.0, sample.Velocity.X);
        Near(2.4, platform.TimeUntilNextTurn(nearTurn));
    }

    [Fact]
    public void ClockDistinctlyBeforeTurnStillUsesOutboundVelocity()
    {
        var platform = Path();
        double before = 2.4 - 1e-8;
        Assert.True(platform.Evaluate(before).Velocity.X > 0.0);
        Assert.InRange(platform.TimeUntilNextTurn(before), 0.0, 2e-8);
    }

    [Fact]
    public void ReproducedThirdTurnPreviouslyThrewNonadvancingEvent()
    {
        var platform = Path();
        double start = 7.19999999999998;
        Aabb body = Rider(platform.Evaluate(start));

        MovingPlatformRideResult result = MovingPlatformRideSolver.Step(
            platform, start, FixedStep, body, 0.0, 0.0);

        Assert.True(result.IsSupported);
        Near(FixedStep, result.AdvancedSeconds);
        Near(platform.Evaluate(start + FixedStep).Bounds.Left + 0.3,
            result.Bounds.Left);
    }

    [Fact]
    public void RoundedAbsoluteEndWithTinyRelativeResidualDoesNotThrow()
    {
        var platform = Path(0.11);
        // This combination is reached during repeated 60 Hz advances. The
        // rounded absolute end equals a computed intermediate event time,
        // though a sub-ULP positive relative-time remainder remains.
        double start = 22.533333333333335;
        Aabb body = Rider(platform.Evaluate(start));

        MovingPlatformRideResult result = MovingPlatformRideSolver.Step(
            platform, start, FixedStep, body, 0.0, 0.0);

        Assert.True(result.IsSupported);
        Assert.Equal(FixedStep, result.AdvancedSeconds);
        Near(platform.Evaluate(start + FixedStep).Bounds.Left + 0.3,
            result.Bounds.Left);
    }

    [Theory]
    [InlineData(2.4)] // The actual gym configuration
    [InlineData(0.11)] // Exposes a sub-ULP terminal remainder
    [InlineData(0.3)]
    [InlineData(0.01)] // Very frequent turnarounds and terminal sub-ULP time
    public void RepeatedSixtyHertzRidingCrossesManyTurnarounds(double oneWaySeconds)
    {
        var platform = Path(oneWaySeconds);
        double clock = 0.0;
        Aabb body = Rider(platform.Evaluate(clock));

        for (int frame = 0; frame < 1200; ++frame)
        {
            MovingPlatformRideResult result = MovingPlatformRideSolver.Step(
                platform, clock, FixedStep, body, 0.0, 0.0);
            Assert.True(result.IsSupported);
            Assert.Equal(FixedStep, result.AdvancedSeconds);
            clock += FixedStep;
            body = result.Bounds;

            MovingPlatformSample expected = platform.Evaluate(clock);
            Near(expected.Bounds.Left + 0.3, body.Left, 1e-8);
            Near(expected.Bounds.Top, body.Bottom);
            Near(expected.Velocity.X, result.WorldVelocity.X);
        }
    }
}
