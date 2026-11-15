using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.MovingPlatforms;

public sealed class MovingPlatformTerminalTailRegressionTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    [Theory]
    [InlineData(2.0)]
    [InlineData(2.4)]
    public void SixtySecondTurnaroundWithUnrepresentableTerminalTailCompletes(double oneWay)
    {
        var platform = new MovingPlatformTrajectory(7,
            Box(0, -1, 1, 0), new Vector2D(3, 0), oneWay);
        // This is the 60 Hz predecessor of the reported t=60.0 crash.
        // Its split produces a positive remainder smaller than 1 ULP of 60.
        double start = 59.983333333333334;
        double dt = 1.0 / 60.0;
        Assert.True(start + dt > start);
        Assert.Null(MovingPlatformCollisionDetector.SweepFirst(
            Box(50, 0, 50.8, 1.6), Vector2D.Zero, Vector2D.Zero,
            start, dt, [platform]));
    }

    [Fact]
    public void PhysicallySignificantSubresolutionTailStillThrows()
    {
        var fastPlatform = new MovingPlatformTrajectory(7,
            Box(0, -1, 1, 0), new Vector2D(4e10, 0), 2.4);
        double start = 59.983333333333334;
        double dt = 1.0 / 60.0;
        Assert.Throws<InvalidOperationException>(() =>
            MovingPlatformCollisionDetector.SweepFirst(
                Box(-1e11, 0, -1e11 + 0.8, 1.6),
                Vector2D.Zero, Vector2D.Zero,
                start, dt, [fastPlatform]));
    }

    [Fact]
    public void NonrepresentableCallerIntervalIsStillRejected()
    {
        var platform = new MovingPlatformTrajectory(7,
            Box(0, -1, 1, 0), new Vector2D(3, 0), 2.4);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovingPlatformCollisionDetector.SweepFirst(
                Box(50, 0, 50.8, 1.6), Vector2D.Zero, Vector2D.Zero,
                60.0, 2e-15, [platform]));
    }
}
