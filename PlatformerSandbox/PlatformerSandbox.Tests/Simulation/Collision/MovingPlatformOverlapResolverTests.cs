using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;
using PlatformerSandbox.Simulation.MovingPlatforms;

namespace PlatformerSandbox.Tests.Simulation.Collision;

public sealed class MovingPlatformOverlapResolverTests
{
    private static Aabb Box(double x, double y, double w, double h) =>
        new(new Vector2D(x, y), new Vector2D(x + w, y + h));

    private static MovingPlatformSample Platform =>
        new(7, Box(-1.0, 0.0, 4.0, 0.25), new Vector2D(2.0, 0), 0);

    [Fact]
    public void NonOverlappingOrTouchingBodyIsNotModified()
    {
        Assert.Null(MovingPlatformOverlapResolver.Resolve(
            Box(3, 0.1, 0.8, 1.6), Platform, []));
        Assert.Null(MovingPlatformOverlapResolver.Resolve(
            Box(5, 0.1, 0.8, 1.6), Platform, []));
    }

    [Fact]
    public void ShallowSidePenetrationUsesMinimumTranslation()
    {
        var result = MovingPlatformOverlapResolver.Resolve(
            Box(2.96, 0.1, 0.8, 1.6), Platform, []);
        Assert.NotNull(result);
        Assert.Equal(new Vector2D(1, 0), result.Value.Normal);
        Assert.InRange(Math.Abs(result.Value.Bounds.Left - 3.0), 0, 1e-12);
        Assert.InRange(result.Value.CorrectionDistance, 0.0399999999, 0.0400000001);
        Assert.False(AabbCollisionDetector.Overlaps(result.Value.Bounds, Platform.Bounds));
    }

    [Fact]
    public void ShallowFloorPenetrationMovesUpToTheSupportPlane()
    {
        var result = MovingPlatformOverlapResolver.Resolve(
            Box(0.0, 0.22, 0.8, 1.6), Platform, []);
        Assert.NotNull(result);
        Assert.Equal(new Vector2D(0, 1), result.Value.Normal);
        Assert.Equal(0.25, result.Value.Bounds.Bottom);
    }

    [Fact]
    public void DeepOverlapIsNotSilentlyTeleportedAway()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MovingPlatformOverlapResolver.Resolve(
                Box(2.65, 0.01, 0.8, 1.6), Platform, []));
    }

    [Fact]
    public void RecoveryMustNotPushBodyIntoStaticScenery()
    {
        var staticWall = new StaticAabbCollider(9,
            Box(3.0, -1.0, 0.2, 4.0));
        Assert.Throws<InvalidOperationException>(() =>
            MovingPlatformOverlapResolver.Resolve(
                Box(2.96, 0.1, 0.8, 1.6), Platform, [staticWall]));
    }

    [Fact]
    public void CorrectionBudgetIsExplicitAndValidated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MovingPlatformOverlapResolver.Resolve(
                Box(2.96, 0.1, 0.8, 1.6), Platform, [], 0));
        Assert.Throws<InvalidOperationException>(() =>
            MovingPlatformOverlapResolver.Resolve(
                Box(2.96, 0.1, 0.8, 1.6), Platform, [], 0.01));
    }
}
