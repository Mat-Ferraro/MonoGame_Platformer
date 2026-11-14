using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;

namespace PlatformerSandbox.Tests.Simulation.Collision;

public sealed class ParabolicAabbCollisionDetectorTests
{
    private static Aabb Box(double x0, double y0, double x1, double y1) =>
        new(new Vector2D(x0, y0), new Vector2D(x1, y1));

    private static StaticAabbCollider Collider(Aabb bounds, int id = 7) => new(id, bounds);
    private static void Close(double expected, double actual) =>
        Assert.InRange(Math.Abs(expected - actual), 0.0, 1e-9);

    [Fact]
    public void ParabolicArcHitsCeilingDespiteReturningToStartingHeight()
    {
        var hit = ParabolicAabbCollisionDetector.Sweep(
            Box(0, 0, 1, 1), new Vector2D(0, 8), new Vector2D(0, -16), 1,
            Collider(Box(-2, 2.5, 2, 4)));
        Assert.NotNull(hit);
        Close(0.25, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(0, -1), hit.Value.Normal);
        Close(1.5, hit.Value.MovingBoundsAtImpact.Bottom);
        Close(4, hit.Value.VelocityAtImpact.Y);
    }

    [Fact]
    public void ParabolicApexGrazingDoesNotBlock()
    {
        var hit = ParabolicAabbCollisionDetector.Sweep(
            Box(0, 0, 1, 1), new Vector2D(0, 8), new Vector2D(0, -16), 1,
            Collider(Box(-2, 3, 2, 4)));
        Assert.Null(hit);
    }

    [Fact]
    public void AccelerationAloneCanHitThinWall()
    {
        var hit = ParabolicAabbCollisionDetector.Sweep(
            Box(0, 0, 1, 1), Vector2D.Zero, new Vector2D(4, 0), 3,
            Collider(Box(3, -1, 3.001, 2)));
        Assert.NotNull(hit);
        Close(1, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
        Close(4, hit.Value.VelocityAtImpact.X);
    }

    [Fact]
    public void AcceleratedPathAvoidsObstacleOutsideActualVerticalCorridor()
    {
        var hit = ParabolicAabbCollisionDetector.Sweep(
            Box(0, 0, 1, 1), new Vector2D(4, 8), new Vector2D(0, -16), 1,
            Collider(Box(3, -1, 3.1, 0)));
        Assert.Null(hit);
    }

    [Fact]
    public void DownwardAccelerationFindsFloorContact()
    {
        var hit = ParabolicAabbCollisionDetector.Sweep(
            Box(0, 3, 1, 4), new Vector2D(2, 0), new Vector2D(0, -4), 2,
            Collider(Box(-10, -1, 10, 0)));
        Close(Math.Sqrt(1.5), hit!.Value.TimeSeconds);
        Assert.Equal(new Vector2D(0, 1), hit.Value.Normal);
        Close(0, hit.Value.MovingBoundsAtImpact.Bottom);
    }

    [Fact]
    public void ContactAtExactEndpointCountsIfMovingInward()
    {
        var hit = ParabolicAabbCollisionDetector.Sweep(
            Box(0, 0, 1, 1), Vector2D.Zero, new Vector2D(4, 0), 1,
            Collider(Box(3, -1, 4, 2)));
        Assert.NotNull(hit);
        Close(1, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
    }

    [Fact]
    public void ApexAtEndpointDoesNotCountIfAcceleratingAway()
    {
        Assert.Null(ParabolicAabbCollisionDetector.Sweep(
            Box(0, 0, 1, 1), new Vector2D(0, 8), new Vector2D(0, -16), 0.5,
            Collider(Box(-2, 3, 2, 4))));
    }

    [Fact]
    public void StartingAtFaceWithInwardAccelerationIsTimeZeroHit()
    {
        var hit = ParabolicAabbCollisionDetector.Sweep(
            Box(0, 0, 1, 1), Vector2D.Zero, new Vector2D(4, 0), 1,
            Collider(Box(1, -1, 2, 2)));
        Close(0, hit!.Value.TimeSeconds);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
    }

    [Fact]
    public void StartingAtFaceWithOutwardAccelerationIsNotHit()
    {
        Assert.Null(ParabolicAabbCollisionDetector.Sweep(
            Box(0, 0, 1, 1), Vector2D.Zero, new Vector2D(-4, 0), 1,
            Collider(Box(1, -1, 2, 2))));
    }

    [Fact]
    public void StartingAtFaceMovingTangentiallyIsNotHit()
    {
        Assert.Null(ParabolicAabbCollisionDetector.Sweep(
            Box(0, 0, 1, 1), new Vector2D(0, 5), Vector2D.Zero, 1,
            Collider(Box(1, -10, 2, 10))));
    }

    [Fact]
    public void ReversingIntoPreviouslyTouchedWallIsDetectedAtLaterTime()
    {
        // x(t) = -2t + t^2: leaves the touching wall, reverses, returns
        // to the wall exactly at t=2, then enters it for t>2.
        var hit = ParabolicAabbCollisionDetector.Sweep(
            Box(0, 0, 1, 1), new Vector2D(-2, 0), new Vector2D(2, 0), 3,
            Collider(Box(1, -1, 2, 2)));
        Close(2, hit!.Value.TimeSeconds);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
    }

    [Fact]
    public void StraightLineMovementAgreesWithExistingSweptDetector()
    {
        var collider = Collider(Box(3, 0, 4, 1));
        var linear = AabbCollisionDetector.Sweep(Box(0, 0, 1, 1),
            new Vector2D(4, 0), collider);
        var curved = ParabolicAabbCollisionDetector.Sweep(Box(0, 0, 1, 1),
            new Vector2D(4, 0), Vector2D.Zero, 1, collider);
        Close(linear!.Value.TimeOfImpact, curved!.Value.TimeSeconds);
        Assert.Equal(linear.Value.Normal, curved.Value.Normal);
        Assert.Equal(linear.Value.MovingBoundsAtImpact, curved.Value.MovingBoundsAtImpact);
    }

    [Fact]
    public void SimultaneousCornerEntryPrefersXNormal()
    {
        var hit = ParabolicAabbCollisionDetector.Sweep(Box(0, 0, 1, 1),
            new Vector2D(2, 2), Vector2D.Zero, 1,
            Collider(Box(2, 2, 3, 3)));
        Close(0.5, hit!.Value.TimeSeconds);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
    }

    [Fact]
    public void IsolatedCornerGrazeDoesNotBlock()
    {
        Assert.Null(ParabolicAabbCollisionDetector.Sweep(Box(0, 0, 1, 1),
            new Vector2D(3, 1), Vector2D.Zero, 1,
            Collider(Box(2, 2, 3, 3))));
    }

    [Fact]
    public void AnObstructingBoxFarAwayIsIgnored()
    {
        Assert.Null(ParabolicAabbCollisionDetector.Sweep(Box(0, 0, 1, 1),
            new Vector2D(-4, 0), new Vector2D(-2, 0), 3,
            Collider(Box(5, -1, 6, 2))));
    }

    [Fact]
    public void DeterministicSmallestColliderIdForExactEqualImpactTimes()
    {
        var high = Collider(Box(3, -10, 4, 10), 9);
        var low = Collider(Box(3, -10, 4, 10), 2);
        var a = ParabolicAabbCollisionDetector.SweepFirst(Box(0, 0, 1, 1),
            new Vector2D(4, 0), Vector2D.Zero, 1, new[] { high, low });
        var b = ParabolicAabbCollisionDetector.SweepFirst(Box(0, 0, 1, 1),
            new Vector2D(4, 0), Vector2D.Zero, 1, new[] { low, high });
        Assert.Equal(2, a!.Value.ColliderId);
        Assert.Equal(a, b);
    }

    [Fact]
    public void InitialPositiveAreaOverlapReturnsNullLikeLinearSweep()
    {
        Assert.Null(ParabolicAabbCollisionDetector.Sweep(
            Box(0, 0, 2, 2), new Vector2D(2, 0), Vector2D.Zero, 1,
            Collider(Box(1, 0, 3, 2))));
    }

    [Fact]
    public void ZeroDurationProducesNoContact()
    {
        Assert.Null(ParabolicAabbCollisionDetector.Sweep(
            Box(0, 0, 1, 1), new Vector2D(10, 0), Vector2D.Zero, 0,
            Collider(Box(1, -1, 2, 2))));
    }
}
