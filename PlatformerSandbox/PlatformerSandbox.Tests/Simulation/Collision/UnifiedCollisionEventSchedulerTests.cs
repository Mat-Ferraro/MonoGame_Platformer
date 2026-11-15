using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Integration;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.Collision;

public sealed class UnifiedCollisionEventSchedulerTests
{
    private static Aabb Box(double x, double y, double width = 1, double height = 1) =>
        new(new Vector2D(x, y), new Vector2D(x + width, y + height));

    private static MovingPlatformTrajectory Moving(int id, double x, double y,
        Vector2D travel, double oneWay = 1) =>
        new(id, Box(x, y), travel, oneWay);

    private static void Near(double expected, double actual, double tolerance = 1e-7) =>
        Assert.InRange(Math.Abs(expected - actual), 0, tolerance);

    [Fact]
    public void EarlierStaticContactWinsAgainstLaterMovingContact()
    {
        var staticWall = new StaticAabbCollider(1, Box(3, 0));
        var movingWall = Moving(7, 6, 0, new Vector2D(1, 0));
        var hit = UnifiedCollisionEventScheduler.SweepParabolicFirst(
            Box(0, 0), new Vector2D(10, 0), Vector2D.Zero,
            0.0, 1.0, [staticWall], [movingWall]);
        Assert.True(hit.HasValue);
        Assert.Equal(1, hit.Value.ColliderId);
        Assert.False(hit.Value.IsMovingPlatform);
        Near(0.2, hit.Value.TimeSeconds);
        Assert.Equal(Vector2D.Zero, hit.Value.SurfaceVelocityAtImpact);
    }

    [Fact]
    public void EarlierMovingContactWinsAgainstLaterStaticContact()
    {
        var staticWall = new StaticAabbCollider(1, Box(9, 0));
        var movingWall = Moving(7, 4, 0, new Vector2D(2, 0));
        var hit = UnifiedCollisionEventScheduler.SweepParabolicFirst(
            Box(0, 0), new Vector2D(10, 0), Vector2D.Zero,
            0.0, 1.0, [staticWall], [movingWall]);
        Assert.True(hit.HasValue);
        Assert.Equal(7, hit.Value.ColliderId);
        Assert.True(hit.Value.IsMovingPlatform);
        Near(0.375, hit.Value.TimeSeconds);
        Near(2, hit.Value.SurfaceVelocityAtImpact.X);
        Near(8, hit.Value.RelativeVelocityAtImpact.X);
    }

    [Fact]
    public void ExactTimeTiePrefersLowerIdAcrossColliderKinds()
    {
        // Both obstacles have the same X face; the moving obstacle only
        // translates vertically, so both X contact times are identical.
        var staticWall = new StaticAabbCollider(8, Box(4, 0));
        var movingWall = Moving(2, 4, 0, new Vector2D(0, 0.2));
        var hit = UnifiedCollisionEventScheduler.SweepParabolicFirst(
            Box(0, 0), new Vector2D(10, 0), Vector2D.Zero,
            0.0, 0.5, [staticWall], [movingWall]);
        Assert.True(hit.HasValue);
        Assert.Equal(2, hit.Value.ColliderId);
        Assert.True(hit.Value.IsMovingPlatform);
    }

    [Fact]
    public void GlobalIdUniquenessIsRequired()
    {
        var staticWall = new StaticAabbCollider(7, Box(4, 0));
        var movingWall = Moving(7, 6, 0, new Vector2D(1, 0));
        Assert.Throws<ArgumentException>(() =>
            UnifiedCollisionEventScheduler.SweepParabolicFirst(
                Box(0, 0), Vector2D.Zero, Vector2D.Zero,
                0, 0, [staticWall], [movingWall]));
    }

    [Fact]
    public void StartingPenetrationAgainstStaticIsRejectedEvenAtZeroDuration()
    {
        Assert.Throws<InvalidOperationException>(() =>
            UnifiedCollisionEventScheduler.SweepParabolicFirst(
                Box(0, 0), Vector2D.Zero, Vector2D.Zero,
                0, 0, [new StaticAabbCollider(3, Box(0.5, 0))],
                Array.Empty<MovingPlatformTrajectory>()));
    }

    [Fact]
    public void StartingPenetrationAgainstMovingIsRejectedEvenAtZeroDuration()
    {
        Assert.Throws<InvalidOperationException>(() =>
            UnifiedCollisionEventScheduler.SweepParabolicFirst(
                Box(0, 0), Vector2D.Zero, Vector2D.Zero,
                0, 0, Array.Empty<StaticAabbCollider>(),
                [Moving(7, 0.5, 0, new Vector2D(1, 0))]));
    }

    [Fact]
    public void ZeroDurationOnValidWorldHasNoCollision()
    {
        Assert.Null(UnifiedCollisionEventScheduler.SweepParabolicFirst(
            Box(0, 0), Vector2D.Zero, Vector2D.Zero,
            0, 0, [new StaticAabbCollider(3, Box(3, 0))],
            [Moving(7, 4, 0, new Vector2D(1, 0))]));
    }

    [Fact]
    public void NonAdvancingAbsoluteClockIsRejectedByRawQuery()
    {
        // The caller's explicit numerical policy must decide whether a
        // known event is safe to consume; a query must not silently skip it.
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            UnifiedCollisionEventScheduler.SweepParabolicFirst(
                Box(0, 0), Vector2D.Zero, Vector2D.Zero,
                20.0, 1e-18,
                Array.Empty<StaticAabbCollider>(),
                Array.Empty<MovingPlatformTrajectory>()));
    }

    [Fact]
    public void AnalyticalQueryFindsCurvedStaticContact()
    {
        // x(t)=t^2 reaches 0.25 displacement at t=0.5.
        MotionSample2D Curve(double t) =>
            new(new Vector2D(t * t, 0), new Vector2D(2 * t, 0));
        var hit = UnifiedCollisionEventScheduler.SweepAnalyticalFirst(
            Box(0, 0), Curve, 0, 1,
            [new StaticAabbCollider(4, Box(1.25, 0))],
            Array.Empty<MovingPlatformTrajectory>());
        Assert.True(hit.HasValue);
        Assert.False(hit.Value.IsMovingPlatform);
        Near(0.5, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
    }

    [Fact]
    public void AnalyticalQueryFindsCurvedContactAgainstMovingPlatform()
    {
        MotionSample2D Curve(double t) =>
            new(new Vector2D(t * t, 0), new Vector2D(2 * t, 0));
        var hit = UnifiedCollisionEventScheduler.SweepAnalyticalFirst(
            Box(0, 0), Curve, 0, 1,
            Array.Empty<StaticAabbCollider>(),
            [Moving(7, 1.25, 0, new Vector2D(0, 0.2))]);
        Assert.True(hit.HasValue);
        Assert.True(hit.Value.IsMovingPlatform);
        Near(0.5, hit.Value.TimeSeconds);
        Assert.Equal(7, hit.Value.ColliderId);
    }

    [Fact]
    public void AnalyticalMovingQueryDetectsPlatformApproachingStationaryBody()
    {
        MotionSample2D Stationary(double _) =>
            new(Vector2D.Zero, Vector2D.Zero);
        var hit = UnifiedCollisionEventScheduler.SweepAnalyticalFirst(
            Box(3, 0), Stationary, 0, 1.5,
            Array.Empty<StaticAabbCollider>(),
            [Moving(7, 0, 0, new Vector2D(4, 0))]);
        Assert.True(hit.HasValue);
        Near(0.5, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(1, 0), hit.Value.Normal);
    }

    [Fact]
    public void RelativeHorizontalTurnIsSplitWithoutRequiringCallerToPrecomputeIt()
    {
        // World x=t^2 is monotone. The platform moves right at 1 unit/s,
        // so relative vx=2t-1 changes sign at t=0.5.
        MotionSample2D Curve(double t) =>
            new(new Vector2D(t * t, 0), new Vector2D(2 * t, 0));
        var hit = UnifiedCollisionEventScheduler.SweepAnalyticalFirst(
            Box(0, 0), Curve, 0, 1,
            Array.Empty<StaticAabbCollider>(),
            [Moving(7, 5, 0, new Vector2D(1, 0))]);
        Assert.Null(hit);
    }

    [Fact]
    public void RelativeHorizontalTurnDetectsMovingPlatformCatchingBodyBeforeTurn()
    {
        MotionSample2D Curve(double t) =>
            new(new Vector2D(t * t, 0), new Vector2D(2 * t, 0));
        // Platform's right face starts 0.2 units left of the body.
        // It catches the accelerating body before relative vx reverses.
        var hit = UnifiedCollisionEventScheduler.SweepAnalyticalFirst(
            Box(0, 0), Curve, 0, 1,
            Array.Empty<StaticAabbCollider>(),
            [Moving(7, -1.2, 0, new Vector2D(1, 0))]);
        Assert.True(hit.HasValue);
        Assert.Equal(7, hit.Value.ColliderId);
        Assert.Equal(new Vector2D(1, 0), hit.Value.Normal);
        Near((1.0 - Math.Sqrt(0.2)) * 0.5, hit.Value.TimeSeconds);
    }

    [Fact]
    public void RisingElevatorCollisionIsDetectedBeforeRelativeVerticalTurn()
    {
        // vy=2-4t, elevator vy=1; relative vy reverses at t=0.25.
        // The body touches the elevator underside at relative dy=0.06,
        // before the turn, even though the body's world apex comes later.
        MotionSample2D Curve(double t) =>
            new(new Vector2D(0, 2 * t - 2 * t * t),
                new Vector2D(0, 2 - 4 * t));
        var hit = UnifiedCollisionEventScheduler.SweepAnalyticalFirst(
            Box(0, 0), Curve, 0, 0.7,
            Array.Empty<StaticAabbCollider>(),
            [Moving(7, 0, 1.06, new Vector2D(0, 1))]);
        Assert.True(hit.HasValue);
        Assert.True(hit.Value.IsMovingPlatform);
        Assert.Equal(new Vector2D(0, -1), hit.Value.Normal);
        Near((1.0 - Math.Sqrt(0.52)) / 4.0, hit.Value.TimeSeconds);
    }

    [Fact]
    public void RisingElevatorCanCatchDescendingCharacterAfterRelativeVerticalTurn()
    {
        // Initially the body rises faster than the elevator. Relative
        // velocity becomes negative, and the lift catches it from below.
        MotionSample2D Curve(double t) =>
            new(new Vector2D(0, 1.5 * t - 2 * t * t),
                new Vector2D(0, 1.5 - 4 * t));
        var hit = UnifiedCollisionEventScheduler.SweepAnalyticalFirst(
            Box(0, 2.02), Curve, 0, 0.5,
            Array.Empty<StaticAabbCollider>(),
            [Moving(7, 0, 1, new Vector2D(0, 1))]);
        Assert.True(hit.HasValue);
        Assert.True(hit.Value.IsMovingPlatform);
        Assert.Equal(new Vector2D(0, 1), hit.Value.Normal);
        Near((0.5 + Math.Sqrt(0.41)) / 4.0, hit.Value.TimeSeconds);
    }

    [Fact]
    public void MultipleRelativeTurnsPreserveEarliestStableContactOrder()
    {
        MotionSample2D Curve(double t) =>
            new(new Vector2D(t * t, 0), new Vector2D(2 * t, 0));
        // Two identical moving faces at different IDs. Relative velocity
        // turns within the segment, but stable ID still wins exact ties.
        var a = Moving(8, -1.2, 0, new Vector2D(1, 0));
        var b = Moving(3, -1.2, 0, new Vector2D(1, 0));
        var hit = UnifiedCollisionEventScheduler.SweepAnalyticalFirst(
            Box(0, 0), Curve, 0, 1,
            Array.Empty<StaticAabbCollider>(), [a, b]);
        Assert.True(hit.HasValue);
        Assert.Equal(3, hit.Value.ColliderId);
    }

    [Fact]
    public void AnalyticalQueryRejectsInvalidStartDisplacement()
    {
        MotionSample2D Bad(double _) =>
            new(new Vector2D(1, 0), Vector2D.Zero);
        Assert.Throws<ArgumentException>(() =>
            UnifiedCollisionEventScheduler.SweepAnalyticalFirst(
                Box(0, 0), Bad, 0, 1,
                Array.Empty<StaticAabbCollider>(),
                Array.Empty<MovingPlatformTrajectory>()));
    }
}
