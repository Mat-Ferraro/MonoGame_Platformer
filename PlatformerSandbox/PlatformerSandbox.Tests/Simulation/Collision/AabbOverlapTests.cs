using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;

namespace PlatformerSandbox.Tests.Simulation.Collision;

public sealed class AabbOverlapTests
{
    private static Aabb Box(double x0, double y0, double x1, double y1) =>
        new(new Vector2D(x0, y0), new Vector2D(x1, y1));

    private static StaticAabbCollider Obstacle(Aabb bounds) => new(17, bounds);

    [Fact]
    public void IdenticalBoxesOverlap() =>
        Assert.True(AabbCollisionDetector.Overlaps(Box(0, 0, 2, 2), Box(0, 0, 2, 2)));

    [Fact]
    public void ClearlySeparatedBoxesDoNotOverlap() =>
        Assert.False(AabbCollisionDetector.Overlaps(Box(0, 0, 1, 1), Box(3, 0, 4, 1)));

    [Fact]
    public void FaceTouchIsNotPositiveOverlap() =>
        Assert.False(AabbCollisionDetector.Overlaps(Box(0, 0, 1, 1), Box(1, 0, 2, 1)));

    [Fact]
    public void CornerTouchIsNotPositiveOverlap() =>
        Assert.False(AabbCollisionDetector.Overlaps(Box(0, 0, 1, 1), Box(1, 1, 2, 2)));

    [Fact]
    public void SidePenetrationReportsNormalFacingMovingBox()
    {
        var result = AabbCollisionDetector.FindOverlap(Box(0, 0, 2, 2), Obstacle(Box(1.5, 0, 3, 2)));
        Assert.True(result.HasValue);
        Assert.Equal(17, result.Value.ColliderId);
        Assert.Equal(new Vector2D(-1, 0), result.Value.Normal);
        Assert.Equal(0.5, result.Value.PenetrationDepth);
        Assert.Equal(new Vector2D(-0.5, 0), result.Value.MinimumSeparation);
    }

    [Fact]
    public void RightSideOfObstacleProducesPositiveNormal()
    {
        var result = AabbCollisionDetector.FindOverlap(Box(2.5, 0, 4, 2), Obstacle(Box(1, 0, 3, 2)));
        Assert.Equal(new Vector2D(1, 0), result!.Value.Normal);
        Assert.Equal(0.5, result.Value.PenetrationDepth);
    }

    [Fact]
    public void AboveObstacleProducesUpwardNormal()
    {
        var result = AabbCollisionDetector.FindOverlap(Box(0, 1.5, 1, 2.5), Obstacle(Box(-1, 0, 2, 2)));
        Assert.Equal(new Vector2D(0, 1), result!.Value.Normal);
        Assert.Equal(0.5, result.Value.PenetrationDepth);
    }

    [Fact]
    public void BelowObstacleProducesDownwardNormal()
    {
        var result = AabbCollisionDetector.FindOverlap(Box(0, 0, 1, 2), Obstacle(Box(-1, 1.5, 2, 5)));
        Assert.Equal(new Vector2D(0, -1), result!.Value.Normal);
        Assert.Equal(0.5, result.Value.PenetrationDepth);
    }

    [Fact]
    public void EqualAxisPenetrationsPreferHorizontalNormal()
    {
        var result = AabbCollisionDetector.FindOverlap(Box(0, 0, 2, 2), Obstacle(Box(1, 1, 3, 3)));
        Assert.Equal(new Vector2D(-1, 0), result!.Value.Normal);
        Assert.Equal(1.0, result.Value.PenetrationDepth);
    }

    [Fact]
    public void FullyContainedBoxUsesSeparationDistanceNotOverlapWidth()
    {
        // A 2x2 box in the middle of a 10x10 obstacle must travel 6 units,
        // not merely its own width, to become non-overlapping.
        var result = AabbCollisionDetector.FindOverlap(Box(4, 4, 6, 6), Obstacle(Box(0, 0, 10, 10)));
        Assert.Equal(new Vector2D(1, 0), result!.Value.Normal);
        Assert.Equal(6.0, result.Value.PenetrationDepth);
    }

    [Fact]
    public void TouchingBoxesHaveNoPenetrationReport() =>
        Assert.Null(AabbCollisionDetector.FindOverlap(Box(0, 0, 1, 1), Obstacle(Box(1, 0, 2, 1))));

    [Fact]
    public void DefaultInvalidGeometryIsRejected()
    {
        Assert.Throws<ArgumentException>(() =>
            AabbCollisionDetector.Overlaps(default, Box(0, 0, 1, 1)));
        Assert.Throws<ArgumentException>(() =>
            AabbCollisionDetector.Overlaps(Box(0, 0, 1, 1), default));
    }

    [Fact]
    public void NullObstacleIsRejected() =>
        Assert.Throws<ArgumentNullException>(() =>
            AabbCollisionDetector.FindOverlap(Box(0, 0, 1, 1), null!));
}
