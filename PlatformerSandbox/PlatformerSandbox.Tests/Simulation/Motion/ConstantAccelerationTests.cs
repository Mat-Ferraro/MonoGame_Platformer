using PlatformerSandbox.Simulation.Motion.Integration;
using Xunit;

namespace PlatformerSandbox.Tests.Simulation.Motion;

public sealed class ConstantAccelerationTests
{
    [Theory]
    [InlineData(0.0, 0.0, 20.0)]
    [InlineData(0.2, 3.0, 10.0)]
    [InlineData(0.4, 4.0, 0.0)]
    [InlineData(0.6, 3.0, -10.0)]
    public void MatchesKnownTrajectory(
        double seconds,
        double expectedPosition,
        double expectedVelocity)
    {
        MotionSample result = ConstantAcceleration.Evaluate(
            initialPosition: 0.0,
            initialVelocity: 20.0,
            acceleration: -50.0,
            elapsedSeconds: seconds);

        AssertClose(expectedPosition, result.Position);
        AssertClose(expectedVelocity, result.Velocity);
    }

    [Fact]
    public void SplittingAnUninterruptedIntervalPreservesTheResult()
    {
        const double acceleration = -50.0;

        MotionSample whole = ConstantAcceleration.Evaluate(0.0, 20.0, acceleration, 0.4);
        MotionSample first = ConstantAcceleration.Evaluate(0.0, 20.0, acceleration, 0.15);
        MotionSample second = ConstantAcceleration.Evaluate(
            first.Position, first.Velocity, acceleration, 0.25);

        AssertClose(whole.Position, second.Position);
        AssertClose(whole.Velocity, second.Velocity);
    }

    [Fact]
    public void ZeroElapsedTimePreservesTheInitialState()
    {
        MotionSample result = ConstantAcceleration.Evaluate(12.0, -3.0, 7.0, 0.0);

        Assert.Equal(12.0, result.Position);
        Assert.Equal(-3.0, result.Velocity);
    }

    [Fact]
    public void RejectsNegativeTime()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConstantAcceleration.Evaluate(0.0, 0.0, 0.0, -0.1));
    }

    [Fact]
    public void RejectsNonFiniteInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConstantAcceleration.Evaluate(double.NaN, 0.0, 0.0, 0.1));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => ConstantAcceleration.Evaluate(0.0, 0.0, 0.0, double.PositiveInfinity));
    }

    private static void AssertClose(double expected, double actual)
    {
        Assert.InRange(Math.Abs(expected - actual), 0.0, 1e-10);
    }
}
