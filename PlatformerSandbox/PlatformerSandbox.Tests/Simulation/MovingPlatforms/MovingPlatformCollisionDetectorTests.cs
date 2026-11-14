using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Contacts;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.MovingPlatforms;

public sealed class MovingPlatformCollisionDetectorTests
{
    private static Aabb Box(double left, double bottom, double width = 1,
        double height = 1) =>
        new(new Vector2D(left, bottom),
            new Vector2D(left + width, bottom + height));

    private static MovingPlatformTrajectory Platform(
        int id = 7, Aabb? bounds = null, Vector2D? travel = null,
        double oneWay = 2) =>
        new(id, bounds ?? Box(0, -1, 4, 1),
            travel ?? new Vector2D(4, 0), oneWay);

    private static MovingPlatformSweepHit? Query(
        Aabb body, Vector2D velocity, Vector2D acceleration,
        double start, double duration, params MovingPlatformTrajectory[] paths) =>
        MovingPlatformCollisionDetector.SweepFirst(
            body, velocity, acceleration, start, duration, paths);

    private static void Near(double expected, double actual, double tolerance = 1e-8) =>
        Assert.InRange(Math.Abs(expected - actual), 0, tolerance);

    [Fact]
    public void HorizontalPlatformHitsStationaryBodyUsingRelativeVelocity()
    {
        var platform = Platform();
        var hit = Query(Box(5, -1), Vector2D.Zero, Vector2D.Zero, 0, 1, platform);
        Assert.True(hit.HasValue);
        Near(0.5, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(1, 0), hit.Value.Normal);
        Near(5, hit.Value.BodyBoundsAtImpact.Left);
        Near(5, hit.Value.PlatformBoundsAtImpact.Right);
        Near(2, hit.Value.PlatformVelocityAtImpact.X);
        Near(-2, hit.Value.RelativeVelocityAtImpact.X);
    }

    [Fact]
    public void MovingBodyCatchesMovingPlatformAtRelativeSpeed()
    {
        var hit = Query(Box(-2, -1), new Vector2D(6, 0),
            Vector2D.Zero, 0, 1, Platform());
        Assert.True(hit.HasValue);
        Near(0.25, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
        Near(0.5, hit.Value.BodyBoundsAtImpact.Right);
    }

    [Fact]
    public void AcceleratedBodyHitsMovingPlatformAtQuadraticRoot()
    {
        // Body's right edge: -1 + t^2 ; platform left: 2t.
        // First impact: t = 1 + sqrt(2), within 2.5 seconds.
        var platform = Platform(travel: new Vector2D(10, 0), oneWay: 5);
        var hit = Query(Box(-2, -1), Vector2D.Zero,
            new Vector2D(2, 0), 0, 2.5, platform);
        Assert.True(hit.HasValue);
        Near(1 + Math.Sqrt(2), hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(-1, 0), hit.Value.Normal);
    }

    [Fact]
    public void LandingOnMovingFloorIsDetectedAtExactTime()
    {
        var hit = Query(Box(2, 2), new Vector2D(0, -2),
            Vector2D.Zero, 0, 1.25, Platform());
        Assert.True(hit.HasValue);
        Near(1, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(0, 1), hit.Value.Normal);
        Near(hit.Value.PlatformBoundsAtImpact.Top,
            hit.Value.BodyBoundsAtImpact.Bottom);
        Near(-2, hit.Value.RelativeVelocityAtImpact.Y);
    }

    [Fact]
    public void PlatformRisingIntoStationaryBodyIsLandingContact()
    {
        var rising = Platform(bounds: Box(0, -3, 4),
            travel: new Vector2D(0, 4));
        var hit = Query(Box(1, 0), Vector2D.Zero,
            Vector2D.Zero, 0, 1.25, rising);
        Assert.True(hit.HasValue);
        Near(1, hit.Value.TimeSeconds);
        Assert.Equal(new Vector2D(0, 1), hit.Value.Normal);
        Near(-2, hit.Value.RelativeVelocityAtImpact.Y);
    }

    [Fact]
    public void PlatformMovingAwayFromBodyDoesNotCollide()
    {
        Assert.Null(Query(Box(-2, -1), Vector2D.Zero,
            Vector2D.Zero, 0, 1, Platform()));
    }

    [Fact]
    public void TouchingFloorWhileMovingAwayIsNotCollision()
    {
        Assert.Null(Query(Box(1, 0), new Vector2D(0, 3),
            Vector2D.Zero, 0, 1, Platform()));
    }

    [Fact]
    public void InitialRestingContactWithoutInwardMotionIsNotCollision()
    {
        Assert.Null(Query(Box(1, 0), new Vector2D(2, 0),
            Vector2D.Zero, 0, 0.5, Platform()));
    }

    [Fact]
    public void PlatformReversalDoesNotTunnelAcrossBody()
    {
        // Platform moves 0..4 then back. Body at x=4.5 is never reached.
        var platform = Platform(bounds: Box(0, -1),
            travel: new Vector2D(3, 0), oneWay: 1);
        Assert.Null(Query(Box(4.5, -1), Vector2D.Zero,
            Vector2D.Zero, 0, 2, platform));
    }

    [Fact]
    public void CollisionAfterReversalUsesTheInboundLeg()
    {
        // Outbound floor right: 1+3t, body left: 8.5-4t.
        // They do not meet before t=1. On the returning leg, the
        // floor right is 7-3t: they meet at t=1.5.
        var platform = Platform(bounds: Box(0, -1),
            travel: new Vector2D(3, 0), oneWay: 1);
        var hit = Query(Box(8.5, -1), new Vector2D(-4, 0),
            Vector2D.Zero, 0, 2, platform);
        Assert.True(hit.HasValue);
        Near(1.5, hit.Value.TimeSeconds);
        Near(2.5, hit.Value.BodyBoundsAtImpact.Left);
        Assert.Equal(new Vector2D(1, 0), hit.Value.Normal);
    }

    [Fact]
    public void CollisionQueryUsesAbsoluteStartTime()
    {
        var platform = Platform();
        // At t=0.5, platform is x=1..5 and moving right at 2/s.
        var hit = Query(Box(6, -1), Vector2D.Zero,
            Vector2D.Zero, 0.5, 1, platform);
        Assert.True(hit.HasValue);
        Near(0.5, hit.Value.TimeSeconds);
        Near(2, hit.Value.PlatformBoundsAtImpact.Left); // at t=1: x=2
    }

    [Fact]
    public void ExactEndContactIsReported()
    {
        var hit = Query(Box(6, -1), Vector2D.Zero,
            Vector2D.Zero, 0, 1, Platform());
        Assert.True(hit.HasValue);
        Near(1, hit.Value.TimeSeconds);
    }

    [Fact]
    public void ExactTurnaroundUsesRightContinuousPlatformVelocity()
    {
        var platform = Platform(bounds: Box(0, -1),
            travel: new Vector2D(2, 0), oneWay: 1);
        var hit = Query(Box(3, -1), Vector2D.Zero,
            Vector2D.Zero, 0, 1, platform);
        Assert.True(hit.HasValue);
        Near(1, hit.Value.TimeSeconds);
        Near(-2, hit.Value.PlatformVelocityAtImpact.X);
    }

    [Fact]
    public void CornerOnlyGrazingDoesNotCount()
    {
        var platform = Platform(bounds: Box(0, -1),
            travel: new Vector2D(2, 0));
        Assert.Null(Query(Box(2, 0), Vector2D.Zero,
            Vector2D.Zero, 0, 1, platform));
    }

    [Fact]
    public void EarliestHitWinsRegardlessOfPlatformEnumerationOrder()
    {
        var first = Platform(id: 4, bounds: Box(0, -1, 1),
            travel: new Vector2D(4, 0));
        var second = Platform(id: 2, bounds: Box(1, -1, 1),
            travel: new Vector2D(4, 0));
        var hit = Query(Box(4, -1), Vector2D.Zero,
            Vector2D.Zero, 0, 2, first, second);
        Assert.True(hit.HasValue);
        Assert.Equal(2, hit.Value.PlatformId);
    }

    [Fact]
    public void ExactTimeTieChoosesLowestStablePlatformId()
    {
        var low = Platform(id: 1);
        var high = Platform(id: 8);
        var a = Query(Box(5, -1), Vector2D.Zero,
            Vector2D.Zero, 0, 1, low, high);
        var b = Query(Box(5, -1), Vector2D.Zero,
            Vector2D.Zero, 0, 1, high, low);
        Assert.Equal(1, a!.Value.PlatformId);
        Assert.Equal(1, b!.Value.PlatformId);
    }

    [Fact]
    public void InitialPositivePenetrationIsRejected()
    {
        Assert.Throws<InvalidOperationException>(() => Query(
            Box(1, -0.5), Vector2D.Zero, Vector2D.Zero,
            0, 1, Platform()));
    }

    [Fact]
    public void DuplicateIdsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Query(
            Box(8, -1), Vector2D.Zero, Vector2D.Zero,
            0, 1, Platform(), Platform()));
    }

    [Fact]
    public void NullPlatformIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => Query(
            Box(8, -1), Vector2D.Zero, Vector2D.Zero,
            0, 1, null!));
    }

    [Theory]
    [InlineData(-1, 1)]
    [InlineData(0, -1)]
    [InlineData(double.NaN, 1)]
    [InlineData(0, double.PositiveInfinity)]
    public void InvalidTimeIsRejected(double start, double duration)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Query(
            Box(8, -1), Vector2D.Zero, Vector2D.Zero,
            start, duration, Platform()));
    }

    [Fact]
    public void ZeroDurationIsAnObservationalQuery()
    {
        Assert.Null(Query(Box(8, -1), Vector2D.Zero,
            Vector2D.Zero, 0, 0, Platform()));
    }

    [Fact]
    public void TurnEventLimitIsExplicit()
    {
        var p = Platform(oneWay: 0.1);
        Assert.Throws<InvalidOperationException>(() =>
            MovingPlatformCollisionDetector.SweepFirst(
                Box(20, -1), Vector2D.Zero, Vector2D.Zero,
                0, 1, [p], maxTurnEventsPerPlatform: 2));
    }
}
