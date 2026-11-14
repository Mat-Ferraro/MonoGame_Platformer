using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.MovingPlatforms;

public sealed class MovingPlatformTrajectoryTests
{
    private static readonly Aabb Initial = new(
        new Vector2D(2, -1), new Vector2D(6, 0));

    private static MovingPlatformTrajectory Path() =>
        new(41, Initial, new Vector2D(8, 0), 2);

    private static void Near(double expected, double actual, double tolerance = 1e-9) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, tolerance);

    [Fact]
    public void StartPositionAndVelocityAreExact()
    {
        var sample = Path().Evaluate(0);
        Assert.Equal(41, sample.PlatformId);
        Assert.Equal(Initial, sample.Bounds);
        Assert.Equal(new Vector2D(4, 0), sample.Velocity);
    }

    [Fact]
    public void HalfwayOutboundIsAnalytical()
    {
        var sample = Path().Evaluate(1);
        Near(6, sample.Bounds.Left);
        Near(4, sample.Velocity.X);
    }

    [Fact]
    public void TurnaroundSnapsToExactEndpointAndReversesVelocity()
    {
        var sample = Path().Evaluate(2);
        Assert.Equal(Initial.Translated(new Vector2D(8, 0)), sample.Bounds);
        Assert.Equal(new Vector2D(-4, 0), sample.Velocity);
    }

    [Fact]
    public void InboundHalfwayReturnsTowardStart()
    {
        var sample = Path().Evaluate(3);
        Near(6, sample.Bounds.Left);
        Near(-4, sample.Velocity.X);
    }

    [Fact]
    public void FullPeriodRestoresExactStartAndOutboundVelocity()
    {
        var sample = Path().Evaluate(4);
        Assert.Equal(Initial, sample.Bounds);
        Assert.Equal(new Vector2D(4, 0), sample.Velocity);
    }

    [Fact]
    public void PeriodicSnapshotsRepeat()
    {
        var platform = Path();
        Near(platform.Evaluate(0.6).Bounds.Left, platform.Evaluate(8.6).Bounds.Left);
        Near(platform.Evaluate(0.6).Velocity.X, platform.Evaluate(8.6).Velocity.X);
    }

    [Fact]
    public void TimeToNextTurnIsStrictlyPositiveAtBoundary()
    {
        var platform = Path();
        Near(2, platform.TimeUntilNextTurn(0));
        Near(1.5, platform.TimeUntilNextTurn(0.5));
        Near(2, platform.TimeUntilNextTurn(2));
        Near(1, platform.TimeUntilNextTurn(3));
        Near(2, platform.TimeUntilNextTurn(4));
    }

    [Fact]
    public void DiagonalTravelUsesBothAxes()
    {
        var platform = new MovingPlatformTrajectory(2, Initial, new Vector2D(6, 4), 3);
        var sample = platform.Evaluate(1.5);
        Near(5, sample.Bounds.Left);
        Near(1, sample.Bounds.Bottom);
        Near(2, sample.Velocity.X);
        Near(4.0 / 3, sample.Velocity.Y);
    }

    [Fact]
    public void NegativeTravelDirectionIsAllowed()
    {
        var platform = new MovingPlatformTrajectory(2, Initial, new Vector2D(-4, -2), 2);
        var sample = platform.Evaluate(1);
        Near(0, sample.Bounds.Left);
        Near(-2, sample.Bounds.Bottom);
        Assert.Equal(new Vector2D(-2, -1), sample.Velocity);
    }

    [Fact]
    public void DifferentSamplingPartitionsEvaluateTheSameTrajectory()
    {
        var platform = Path();
        var direct = platform.Evaluate(2.75);
        var last = platform.Evaluate(0.5);
        last = platform.Evaluate(last.TimeSeconds + 1.0);
        last = platform.Evaluate(last.TimeSeconds + 1.25);
        Assert.Equal(direct, last);
    }

    [Fact]
    public void ZeroTravelIsNotAMovingPlatform() =>
        Assert.Throws<ArgumentException>(() =>
            new MovingPlatformTrajectory(1, Initial, Vector2D.Zero, 2));

    [Fact]
    public void NegativePlatformIdIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MovingPlatformTrajectory(-1, Initial, new Vector2D(1, 0), 2));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NaN)]
    [InlineData(1e308)]
    public void InvalidOneWayDurationIsRejected(double duration) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new MovingPlatformTrajectory(1, Initial, new Vector2D(1, 0), duration));

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidTimesAreRejected(double time)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Path().Evaluate(time));
        Assert.Throws<ArgumentOutOfRangeException>(() => Path().TimeUntilNextTurn(time));
    }

    [Fact]
    public void InvalidAabbIsRejected() =>
        Assert.Throws<ArgumentException>(() =>
            new MovingPlatformTrajectory(1, default, new Vector2D(1, 0), 2));
}
