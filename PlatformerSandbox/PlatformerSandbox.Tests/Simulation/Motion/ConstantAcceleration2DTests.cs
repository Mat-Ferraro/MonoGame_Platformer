using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Motion.Integration;

namespace PlatformerSandbox.Tests.Simulation.Motion;

public sealed class ConstantAcceleration2DTests
{
    private static void Close(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, 1e-10);

    [Fact]
    public void BothAxesUseExactAnalyticalEquation()
    {
        MotionSample2D s = ConstantAcceleration2D.Evaluate(
            new Vector2D(1, -2), new Vector2D(2, 3),
            new Vector2D(4, -2), 2);
        Assert.Equal(new Vector2D(13, 0), s.Position);
        Assert.Equal(new Vector2D(10, -1), s.Velocity);
    }

    [Fact]
    public void ZeroTimeReturnsInitialState()
    {
        var s = ConstantAcceleration2D.Evaluate(
            new Vector2D(3, 5), new Vector2D(-4, 6),
            new Vector2D(2, -8), 0);
        Assert.Equal(new Vector2D(3, 5), s.Position);
        Assert.Equal(new Vector2D(-4, 6), s.Velocity);
    }

    [Fact]
    public void SplittingAtArbitraryTimePreservesState()
    {
        Vector2D velocity = new(4, -3);
        Vector2D acceleration = new(-2, 6);
        var full = ConstantAcceleration2D.Evaluate(Vector2D.Zero, velocity, acceleration, 1);
        var first = ConstantAcceleration2D.Evaluate(Vector2D.Zero, velocity, acceleration, 0.375);
        var second = ConstantAcceleration2D.Evaluate(first.Position, first.Velocity,
            acceleration, 0.625);
        Close(full.Position.X, second.Position.X);
        Close(full.Position.Y, second.Position.Y);
        Close(full.Velocity.X, second.Velocity.X);
        Close(full.Velocity.Y, second.Velocity.Y);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidTimeRejected(double time) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ConstantAcceleration2D.Evaluate(Vector2D.Zero,
                Vector2D.Zero, Vector2D.Zero, time));
}
