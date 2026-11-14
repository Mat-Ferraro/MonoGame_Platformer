using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.MovingPlatforms;

public sealed class MovingPlatformRideSolverTests
{
    private static readonly Aabb PlatformBounds = new(
        new Vector2D(0, -1), new Vector2D(4, 0));
    private static readonly Aabb Body = new(
        new Vector2D(1, 0), new Vector2D(2, 1));

    private static MovingPlatformTrajectory Path(
        Vector2D? offset = null, double oneWayTime = 2) =>
        new(7, PlatformBounds, offset ?? new Vector2D(4, 0), oneWayTime);

    private static void Near(double expected, double actual, double tolerance = 1e-8) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, tolerance);

    [Fact]
    public void StationaryRelativeRiderIsCarriedByPlatform()
    {
        var result = MovingPlatformRideSolver.Step(Path(), 0, 1, Body, 0, 0);
        Assert.True(result.IsSupported);
        Assert.Null(result.DepartureSeconds);
        Near(3, result.Bounds.Left);
        Near(0, result.Bounds.Bottom);
        Near(2, result.WorldVelocity.X);
    }

    [Fact]
    public void RiderCarriesAlongVerticalPlatformMotion()
    {
        var platform = Path(new Vector2D(0, 4));
        var result = MovingPlatformRideSolver.Step(platform, 0, 1, Body, 0, 0);
        Near(1, result.Bounds.Left);
        Near(2, result.Bounds.Bottom);
        Assert.Equal(new Vector2D(0, 2), result.WorldVelocity);
    }

    [Fact]
    public void RelativeRunningAndPlatformCarryComposeExactlyOnce()
    {
        var result = MovingPlatformRideSolver.Step(Path(), 0, 1, Body, 1, 0);
        Near(4, result.Bounds.Left);  // start 1 + platform 2 + run 1
        Near(3, result.WorldVelocity.X);
    }

    [Fact]
    public void RelativeAccelerationAddsCorrectDistanceAndVelocity()
    {
        var result = MovingPlatformRideSolver.Step(Path(), 0, 1, Body, 0, 2);
        Near(4, result.Bounds.Left); // start 1 + carry 2 + relative 1
        Near(4, result.WorldVelocity.X); // carry 2 + relative 2
    }

    [Fact]
    public void PlatformReversalCarriesRiderBackWithoutTeleportation()
    {
        var result = MovingPlatformRideSolver.Step(Path(), 0, 3, Body, 0, 0);
        Assert.True(result.IsSupported);
        Near(3, result.Bounds.Left); // moved 4 and returned 2
        Near(-2, result.WorldVelocity.X);
        Near(3, result.AdvancedSeconds);
    }

    [Fact]
    public void ExactTurnaroundPositionIsContinuousButVelocityReverses()
    {
        var result = MovingPlatformRideSolver.Step(Path(), 0, 2, Body, 0, 0);
        Near(5, result.Bounds.Left);
        Near(-2, result.WorldVelocity.X); // right-continuous at endpoint
    }

    [Fact]
    public void MultipleTurnsMaintainRelativePosition()
    {
        var result = MovingPlatformRideSolver.Step(Path(), 0, 8, Body, 0, 0);
        Assert.True(result.IsSupported);
        Near(1, result.Bounds.Left);
        Near(2, result.WorldVelocity.X);
    }

    [Fact]
    public void SteppingInPartitionsEqualsOneAnalyticalStep()
    {
        var platform = Path();
        var entire = MovingPlatformRideSolver.Step(platform, 0, 3, Body, 0.2, 0.1);
        var first = MovingPlatformRideSolver.Step(platform, 0, 1.25, Body, 0.2, 0.1);
        var second = MovingPlatformRideSolver.Step(platform, 1.25, 1.75,
            first.Bounds, 0.2 + 0.1 * 1.25, 0.1);
        Assert.True(entire.IsSupported);
        Assert.True(second.IsSupported);
        Near(entire.Bounds.Left, second.Bounds.Left);
        Near(entire.Bounds.Bottom, second.Bounds.Bottom);
        Near(entire.WorldVelocity.X, second.WorldVelocity.X);
    }

    [Fact]
    public void RelativeWalkOffRightEdgeStopsAtExactDeparture()
    {
        var platform = Path(new Vector2D(10, 0), oneWayTime: 10);
        var start = new Aabb(new Vector2D(2.5, 0), new Vector2D(3.5, 1));
        var result = MovingPlatformRideSolver.Step(platform, 0, 2, start, 3, 0);
        Assert.False(result.IsSupported);
        Near(0.5, result.AdvancedSeconds);
        Near(0.5, result.DepartureSeconds!.Value);
        Near(4.5, result.Bounds.Left); // local boundary x=4 + carry x=0.5
        Near(4, result.WorldVelocity.X);
    }

    [Fact]
    public void RelativeWalkOffLeftEdgeStopsAtExactDeparture()
    {
        var platform = Path(new Vector2D(10, 0), oneWayTime: 10);
        var start = new Aabb(new Vector2D(0.5, 0), new Vector2D(1.5, 1));
        var result = MovingPlatformRideSolver.Step(platform, 0, 2, start, -3, 0);
        Assert.False(result.IsSupported);
        Near(0.5, result.AdvancedSeconds);
        Near(-0.5, result.Bounds.Left); // platform +0.5, relative left -1
        Near(-2, result.WorldVelocity.X);
    }

    [Fact]
    public void AccelerationWalkOffUsesAnalyticalRoot()
    {
        var platform = Path(new Vector2D(10, 0), oneWayTime: 10);
        var result = MovingPlatformRideSolver.Step(platform, 0, 2, Body, 0, 2);
        Near(Math.Sqrt(3), result.AdvancedSeconds);
        Assert.False(result.IsSupported);
        Near(4 + Math.Sqrt(3), result.Bounds.Left);
    }

    [Fact]
    public void LeavingExactlyAtStepEndCountsAsDeparture()
    {
        var platform = Path(new Vector2D(10, 0), oneWayTime: 10);
        var result = MovingPlatformRideSolver.Step(platform, 0, 3, Body, 1, 0);
        Assert.False(result.IsSupported);
        Near(3, result.AdvancedSeconds);
        Near(7, result.Bounds.Left);
    }

    [Fact]
    public void NarrowTravelRemainsSupported()
    {
        var result = MovingPlatformRideSolver.Step(Path(), 0, 1, Body, 0.5, 0);
        Assert.True(result.IsSupported);
        Near(3.5, result.Bounds.Left);
    }

    [Fact]
    public void ZeroDurationIsObservational()
    {
        var result = MovingPlatformRideSolver.Step(Path(), 0, 0, Body, 2, 3);
        Assert.True(result.IsSupported);
        Assert.Equal(Body, result.Bounds);
        Near(0, result.AdvancedSeconds);
        Near(4, result.WorldVelocity.X);
    }

    [Fact]
    public void CanWalkOnPlatformMovingLeft()
    {
        var leftward = Path(new Vector2D(-4, 0));
        var result = MovingPlatformRideSolver.Step(leftward, 0, 0.5, Body, 1, 0);
        Assert.True(result.IsSupported);
        Near(0.5, result.Bounds.Left);
        Near(-1, result.WorldVelocity.X);
    }

    [Fact]
    public void LaunchVelocityInheritsBothPlatformAxesExactlyOnce()
    {
        var platform = Path(new Vector2D(6, -2));
        var sample = platform.Evaluate(0.5);
        var launch = MovingPlatformRideSolver.InheritLaunchVelocity(
            new Vector2D(4, 7), sample);
        Assert.Equal(new Vector2D(7, 6), launch);
    }

    [Fact]
    public void LaunchVelocityAtTurnUsesCurrentDirection()
    {
        var platform = Path(new Vector2D(6, -2));
        var launch = MovingPlatformRideSolver.InheritLaunchVelocity(
            new Vector2D(4, 7), platform.Evaluate(2));
        Assert.Equal(new Vector2D(1, 8), launch);
    }

    [Fact]
    public void BoundaryTouchAloneIsNotSupport()
    {
        var beside = new Aabb(new Vector2D(4, 0), new Vector2D(5, 1));
        Assert.False(MovingPlatformRideSolver.IsSupported(beside, PlatformBounds));
    }

    [Fact]
    public void FloatingAboveIsNotSupport()
    {
        var above = Body.Translated(new Vector2D(0, 0.01));
        Assert.False(MovingPlatformRideSolver.IsSupported(above, PlatformBounds));
    }

    [Fact]
    public void UnsupportedBodyCannotReceiveFreeCarry()
    {
        var above = Body.Translated(new Vector2D(0, 1));
        Assert.Throws<InvalidOperationException>(() =>
            MovingPlatformRideSolver.Step(Path(), 0, 1, above, 0, 0));
    }

    [Fact]
    public void EventLimitFailsWithoutPublishingPartialState()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MovingPlatformRideSolver.Step(Path(new Vector2D(4, 0), 1),
                0, 5, Body, 0, 0, maxTurns: 1));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidStartTimeIsRejected(double time) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovingPlatformRideSolver.Step(Path(), time, 1, Body, 0, 0));

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidDurationIsRejected(double duration) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovingPlatformRideSolver.Step(Path(), 0, duration, Body, 0, 0));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidRelativeVelocityIsRejected(double velocity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovingPlatformRideSolver.Step(Path(), 0, 1, Body, velocity, 0));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidRelativeAccelerationIsRejected(double acceleration) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovingPlatformRideSolver.Step(Path(), 0, 1, Body, 0, acceleration));

    [Fact]
    public void InvalidEventLimitIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovingPlatformRideSolver.Step(Path(), 0, 1, Body, 0, 0, maxTurns: 0));

    [Fact]
    public void InvalidBodyIsRejected() =>
        Assert.Throws<ArgumentException>(() =>
            MovingPlatformRideSolver.Step(Path(), 0, 1, default, 0, 0));

    [Fact]
    public void NullPlatformIsRejected() =>
        Assert.Throws<ArgumentNullException>(() =>
            MovingPlatformRideSolver.Step(null!, 0, 1, Body, 0, 0));
}
