using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Integration;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.Collision;

/// <summary>
/// Regression coverage for analytical face-crossing precision, particularly
/// the repeated small updates used by the playable moving-platform gym.
/// </summary>
public sealed class AnalyticalAabbPrecisionRegressionTests
{
    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));

    private static void Near(double expected, double actual, double tolerance = 1e-8) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, tolerance);

    [Fact]
    public void OneBitGapBeforeWallRemainsARealCollision()
    {
        double wallLeft = Math.BitIncrement(1.0);
        var obstacle = new StaticAabbCollider(10, Box(wallLeft, -1, 2.0, 3.0));
        MotionSample2D Evaluate(double t) => new(
            new Vector2D(3.0 * t, 0.0), new Vector2D(3.0, 0.0));
        var hit = AnalyticalAabbCollisionDetector.SweepFirst(
            Box(0, 0, 1, 1), Evaluate, 1.0 / 60.0, [obstacle]);
        Assert.True(hit.HasValue);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
        Near(wallLeft, hit.Value.MovingBoundsAtImpact.Right);
    }

    [Fact]
    public void InitialWallTouchMovingInwardIsAZeroTimeImpact()
    {
        var wall = new StaticAabbCollider(1, Box(1, -1, 2, 5));
        MotionSample2D Evaluate(double t) => new(
            new Vector2D(2 * t, 0.3 * t), new Vector2D(2, 0.3));
        var hit = AnalyticalAabbCollisionDetector.SweepFirst(
            Box(0, 0, 1, 1), Evaluate, 0.1, [wall]);
        Assert.True(hit.HasValue);
        Near(0.0, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
    }

    [Fact]
    public void WallCollisionWhileAscendingUsesHorizontalNormal()
    {
        var wall = new StaticAabbCollider(2, Box(2, -1, 2.2, 5));
        MotionSample2D Evaluate(double t) => new(
            new Vector2D(4 * t, t), new Vector2D(4, 1));
        var hit = AnalyticalAabbCollisionDetector.SweepFirst(
            Box(0, 0, 1, 1), Evaluate, 0.4, [wall]);
        Assert.True(hit.HasValue);
        Near(0.25, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
    }

    [Fact]
    public void CurvedCeilingImpactHasDownwardFacingNormal()
    {
        var ceiling = new StaticAabbCollider(3, Box(-1, 2, 2, 2.3));
        MotionSample2D Evaluate(double t) => new(
            new Vector2D(0.2 * t, 6 * t - 10 * t * t),
            new Vector2D(0.2, 6 - 20 * t));
        var hit = AnalyticalAabbCollisionDetector.SweepFirst(
            Box(0, 0, 0.8, 1.6), Evaluate, 0.15, [ceiling]);
        Assert.True(hit.HasValue);
        Assert.Equal(new Vector2D(0, -1), hit.Value.Normal);
        Near(2.0, hit.Value.MovingBoundsAtImpact.Top);
    }

    [Fact]
    public void ExactEndOfStepLandingStillCounts()
    {
        var floor = new StaticAabbCollider(4, Box(-2, -1, 2, 0));
        MotionSample2D Evaluate(double t) => new(
            new Vector2D(0, -2 * t), new Vector2D(0, -2));
        var hit = AnalyticalAabbCollisionDetector.SweepFirst(
            Box(0, 1, 0.8, 2.6), Evaluate, 0.5, [floor]);
        Assert.True(hit.HasValue);
        Near(0.5, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(0, 1), hit.Value.Normal);
    }

    [Fact]
    public void RestingFloorContactDuringUpwardTakeoffIsNotAnImpact()
    {
        var floor = new StaticAabbCollider(5, Box(-4, -1, 4, 0));
        MotionSample2D Evaluate(double t) => new(
            new Vector2D(3 * t, 7 * t - 4 * t * t),
            new Vector2D(3, 7 - 8 * t));
        Assert.Null(AnalyticalAabbCollisionDetector.SweepFirst(
            Box(0, 0, 0.8, 1.6), Evaluate, 0.1, [floor]));
    }

    [Fact]
    public void PureCornerGrazeDoesNotCreateAContact()
    {
        var obstacle = new StaticAabbCollider(6, Box(2, 2, 3, 3));
        MotionSample2D Evaluate(double t) => new(
            new Vector2D(t, t), new Vector2D(1, 1));
        Assert.Null(AnalyticalAabbCollisionDetector.SweepFirst(
            Box(0, 0, 1, 1), Evaluate, 1.0, [obstacle]));
    }

    [Fact]
    public void TangentialFloorTouchDoesNotBecomeAFalseCollision()
    {
        var floor = new StaticAabbCollider(7, Box(-5, -1, 5, 0));
        MotionSample2D Evaluate(double t) => new(
            new Vector2D(4 * t, 0), new Vector2D(4, 0));
        Assert.Null(AnalyticalAabbCollisionDetector.SweepFirst(
            Box(0, 0, 0.8, 1.6), Evaluate, 0.3, [floor]));
    }

    [Fact]
    public void PlatformRelativeSweepDetectsLandingWithoutTreatingPlatformAsStatic()
    {
        var platformBounds = Box(5.3, 2.05, 7.35, 2.30);
        var platform = new StaticAabbCollider(8, platformBounds);
        var body = Box(5.6, 2.8, 6.4, 4.4);
        // Express the character movement in the platform's moving frame.
        MotionSample2D EvaluateRelative(double t) => new(
            new Vector2D(-1.0 * t, -6.0 * t),
            new Vector2D(-1.0, -6.0));
        var hit = AnalyticalAabbCollisionDetector.SweepFirst(
            body, EvaluateRelative, 0.15, [platform]);
        Assert.True(hit.HasValue);
        Assert.Equal(new Vector2D(0, 1), hit.Value.Normal);
        Near(platformBounds.Top, hit.Value.MovingBoundsAtImpact.Bottom);
    }

    [Theory]
    [InlineData(7.199999999999999)]
    [InlineData(7.2)]
    [InlineData(7.200000000000001)]
    [InlineData(9.6)]
    [InlineData(12.0)]
    public void RelativeLandingQueryNearRepeatedPlatformTurnsRemainsValid(double time)
    {
        var platform = new MovingPlatformTrajectory(9,
            Box(5.3, 2.05, 7.35, 2.30), new Vector2D(2.4, 0), 2.4);
        var snapshot = platform.Evaluate(time);
        var body = Box(snapshot.Bounds.Left + 0.35, snapshot.Bounds.Top + 0.015,
            snapshot.Bounds.Left + 1.15, snapshot.Bounds.Top + 1.615);
        double relativeX = -snapshot.Velocity.X;
        MotionSample2D EvaluateRelative(double t) => new(
            new Vector2D(relativeX * t, -4 * t),
            new Vector2D(relativeX, -4));
        var hit = AnalyticalAabbCollisionDetector.SweepFirst(
            body, EvaluateRelative, 0.01,
            [new StaticAabbCollider(snapshot.PlatformId, snapshot.Bounds)]);
        Assert.True(hit.HasValue);
        Assert.Equal(new Vector2D(0, 1), hit.Value.Normal);
        Near(snapshot.Bounds.Top, hit.Value.MovingBoundsAtImpact.Bottom);
    }
}
