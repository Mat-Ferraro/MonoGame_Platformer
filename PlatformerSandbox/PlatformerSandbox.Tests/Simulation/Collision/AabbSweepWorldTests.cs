using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;

namespace PlatformerSandbox.Tests.Simulation.Collision;

public sealed class AabbSweepWorldTests
{
    private static Aabb Box(double x0, double y0, double x1, double y1) =>
        new(new Vector2D(x0, y0), new Vector2D(x1, y1));

    private static readonly Aabb Moving = Box(0, 0, 1, 1);
    private static readonly Vector2D Right = new(10, 0);

    [Fact]
    public void PicksClosestObstacleNotFirstInList()
    {
        var far = new StaticAabbCollider(1, Box(7, 0, 8, 1));
        var near = new StaticAabbCollider(99, Box(3, 0, 4, 1));
        var hit = AabbCollisionDetector.SweepFirst(Moving, Right, [far, near]);
        Assert.Equal(99, hit!.Value.ColliderId);
        Assert.InRange(Math.Abs(0.2 - hit.Value.TimeOfImpact), 0, 1e-11);
    }

    [Fact]
    public void ExactTieChoosesLowerId()
    {
        var high = new StaticAabbCollider(8, Box(3, 0, 4, 1));
        var low = new StaticAabbCollider(2, Box(3, 0, 4, 1));
        Assert.Equal(2, AabbCollisionDetector.SweepFirst(Moving, Right, [high, low])!.Value.ColliderId);
        Assert.Equal(2, AabbCollisionDetector.SweepFirst(Moving, Right, [low, high])!.Value.ColliderId);
    }

    [Fact]
    public void EmptyWorldReturnsNoHit() =>
        Assert.Null(AabbCollisionDetector.SweepFirst(Moving, Right, []));

    [Fact]
    public void WorldWithNoHitReturnsNone() =>
        Assert.Null(AabbCollisionDetector.SweepFirst(Moving, Right,
            [new StaticAabbCollider(3, Box(3, 5, 4, 6))]));

    [Fact]
    public void InitialOverlapDoesNotHideOtherSweptHit()
    {
        var overlapping = new StaticAabbCollider(3, Box(0.5, 0, 1.5, 1));
        var future = new StaticAabbCollider(4, Box(3, 0, 4, 1));
        var hit = AabbCollisionDetector.SweepFirst(Moving, Right, [overlapping, future]);
        Assert.Equal(4, hit!.Value.ColliderId);
    }

    [Fact]
    public void DuplicateColliderIdsAreRejected()
    {
        var a = new StaticAabbCollider(3, Box(3, 0, 4, 1));
        var b = new StaticAabbCollider(3, Box(7, 0, 8, 1));
        Assert.Throws<ArgumentException>(() =>
            AabbCollisionDetector.SweepFirst(Moving, Right, [a, b]));
    }

    [Fact]
    public void NegativeColliderIdIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StaticAabbCollider(-1, Box(3, 0, 4, 1)));

    [Fact]
    public void DefaultEmptyBoundsAreRejected() =>
        Assert.Throws<ArgumentException>(() =>
            new StaticAabbCollider(1, default));

    [Fact]
    public void NullWorldIsRejected() =>
        Assert.Throws<ArgumentNullException>(() =>
            AabbCollisionDetector.SweepFirst(Moving, Right, null!));

    [Fact]
    public void NullColliderEntryIsRejected() =>
        Assert.Throws<ArgumentNullException>(() =>
            AabbCollisionDetector.SweepFirst(Moving, Right, [null!]));
}
