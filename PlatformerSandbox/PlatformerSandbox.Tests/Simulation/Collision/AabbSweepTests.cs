using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;

namespace PlatformerSandbox.Tests.Simulation.Collision;

public sealed class AabbSweepTests
{
    private static Aabb Box(double x0, double y0, double x1, double y1) =>
        new(new Vector2D(x0, y0), new Vector2D(x1, y1));
    private static StaticAabbCollider Obstacle(Aabb bounds) => new(7, bounds);

    private static void Close(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0, 1e-11);

    [Fact]
    public void RightwardSweepReportsFirstImpactTimeAndNormal()
    {
        var hit = AabbCollisionDetector.Sweep(Box(0, 0, 1, 1), new Vector2D(4, 0),
            Obstacle(Box(3, 0, 4, 1)));
        Assert.True(hit.HasValue);
        Assert.Equal(7, hit.Value.ColliderId);
        Close(0.5, hit.Value.TimeOfImpact);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
        Assert.Equal(Box(2, 0, 3, 1), hit.Value.MovingBoundsAtImpact);
    }

    [Fact]
    public void ThinWallIsDetectedEvenWhenMotionPassesThroughInOneSegment()
    {
        var hit = AabbCollisionDetector.Sweep(Box(0, 0, 1, 1), new Vector2D(10, 0),
            Obstacle(Box(3, 0, 3.01, 1)));
        Close(0.2, hit!.Value.TimeOfImpact);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
    }

    [Fact]
    public void LeftwardSweepReportsRightFacingNormal()
    {
        var hit = AabbCollisionDetector.Sweep(Box(5, 0, 6, 1), new Vector2D(-4, 0),
            Obstacle(Box(2, 0, 3, 1)));
        Assert.Equal(new Vector2D(1, 0), hit!.Value.Normal);
        Close(0.5, hit.Value.TimeOfImpact);
        Assert.Equal(Box(3, 0, 4, 1), hit.Value.MovingBoundsAtImpact);
    }

    [Fact]
    public void UpwardSweepReportsDownwardNormal()
    {
        var hit = AabbCollisionDetector.Sweep(Box(0, 0, 1, 1), new Vector2D(0, 4),
            Obstacle(Box(0, 3, 1, 4)));
        Assert.Equal(new Vector2D(0, -1), hit!.Value.Normal);
        Close(0.5, hit.Value.TimeOfImpact);
    }

    [Fact]
    public void DownwardSweepReportsUpwardNormal()
    {
        var hit = AabbCollisionDetector.Sweep(Box(0, 5, 1, 6), new Vector2D(0, -4),
            Obstacle(Box(0, 2, 1, 3)));
        Assert.Equal(new Vector2D(0, 1), hit!.Value.Normal);
        Close(0.5, hit.Value.TimeOfImpact);
    }

    [Fact]
    public void TouchingFaceAndMovingInwardReportsImmediateImpact()
    {
        var hit = AabbCollisionDetector.Sweep(Box(0, 0, 1, 1), new Vector2D(2, 0),
            Obstacle(Box(1, 0, 2, 1)));
        Close(0, hit!.Value.TimeOfImpact);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
    }

    [Fact]
    public void TouchingFaceAndMovingAwayDoesNotBlock()
    {
        var hit = AabbCollisionDetector.Sweep(Box(0, 0, 1, 1), new Vector2D(-2, 0),
            Obstacle(Box(1, 0, 2, 1)));
        Assert.Null(hit);
    }

    [Fact]
    public void TouchingFaceAndMovingTangentiallyDoesNotBlock()
    {
        var hit = AabbCollisionDetector.Sweep(Box(0, 0, 1, 1), new Vector2D(0, 3),
            Obstacle(Box(1, 0, 2, 5)));
        Assert.Null(hit);
    }

    [Fact]
    public void FullySeparatedParallelAxisDoesNotHit()
    {
        var hit = AabbCollisionDetector.Sweep(Box(0, 0, 1, 1), new Vector2D(5, 0),
            Obstacle(Box(3, 2, 4, 3)));
        Assert.Null(hit);
    }

    [Fact]
    public void EndOfSegmentFaceContactCounts()
    {
        var hit = AabbCollisionDetector.Sweep(Box(0, 0, 1, 1), new Vector2D(1, 0),
            Obstacle(Box(2, 0, 3, 1)));
        Close(1, hit!.Value.TimeOfImpact);
    }

    [Fact]
    public void BoxBeyondSegmentDoesNotHit()
    {
        var hit = AabbCollisionDetector.Sweep(Box(0, 0, 1, 1), new Vector2D(1, 0),
            Obstacle(Box(3, 0, 4, 1)));
        Assert.Null(hit);
    }

    [Fact]
    public void DiagonalCornerEntryUsesHorizontalNormalForExactTie()
    {
        var hit = AabbCollisionDetector.Sweep(Box(0, 0, 1, 1), new Vector2D(2, 2),
            Obstacle(Box(2, 2, 3, 3)));
        Close(0.5, hit!.Value.TimeOfImpact);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
    }

    [Fact]
    public void IsolatedCornerGrazingDoesNotBlock()
    {
        // X exit and Y entry occur together at t=1; no inward overlap interval.
        var hit = AabbCollisionDetector.Sweep(Box(0, 0, 1, 1), new Vector2D(3, 1),
            Obstacle(Box(2, 2, 3, 3)));
        Assert.Null(hit);
    }

    [Fact]
    public void MovingAwayFromSeparatedObstacleDoesNotHit()
    {
        var hit = AabbCollisionDetector.Sweep(Box(0, 0, 1, 1), new Vector2D(-6, 0),
            Obstacle(Box(3, 0, 4, 1)));
        Assert.Null(hit);
    }

    [Fact]
    public void InitialPenetrationRequiresSeparateOverlapQuery()
    {
        Aabb moving = Box(0, 0, 2, 2);
        var obstacle = Obstacle(Box(1, 0, 3, 2));
        Assert.NotNull(AabbCollisionDetector.FindOverlap(moving, obstacle));
        Assert.Null(AabbCollisionDetector.Sweep(moving, new Vector2D(3, 0), obstacle));
    }

    [Fact]
    public void ZeroDisplacementDoesNotReportSweptHit() =>
        Assert.Null(AabbCollisionDetector.Sweep(Box(0, 0, 1, 1), Vector2D.Zero,
            Obstacle(Box(1, 0, 2, 1))));

    [Fact]
    public void NullObstacleIsRejected() =>
        Assert.Throws<ArgumentNullException>(() =>
            AabbCollisionDetector.Sweep(Box(0, 0, 1, 1), Vector2D.Zero, null!));
}
