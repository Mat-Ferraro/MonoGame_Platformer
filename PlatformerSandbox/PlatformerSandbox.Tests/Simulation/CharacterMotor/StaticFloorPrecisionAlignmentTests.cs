using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Collision.Resolution;
using Xunit;

namespace PlatformerSandbox.Tests.Simulation.CharacterMotor;

public sealed class StaticFloorPrecisionAlignmentTests
{
    private static Aabb Box(double l, double b, double r, double t) =>
        new(new Vector2D(l, b), new Vector2D(r, t));

    [Fact]
    public void GridFloor_UlpPenetration_AlignsToExactSurface()
    {
        StaticAabbCollider floor = new(11794, Box(33, 17, 35, 18));
        double bottom = Math.BitDecrement(Math.BitDecrement(18.0));
        Aabb initial = Box(34.8149, bottom, 35.6149, bottom + 1.6);
        Aabb aligned = StaticFloorPrecisionAlignment.Align(initial, [floor]);
        Assert.Equal(18.0, aligned.Bottom);
        Assert.Equal(initial.Left, aligned.Left);
        Assert.Equal(initial.Width, aligned.Width);
        Assert.Equal(initial.Height, aligned.Height);
        Assert.False(AabbCollisionDetector.Overlaps(aligned, floor.Bounds));
    }

    [Fact]
    public void RealFloorPenetration_IsNotNormalized()
    {
        StaticAabbCollider floor = new(11794, Box(33, 17, 35, 18));
        Aabb body = Box(34.5, 18 - 1e-5, 35.3, 19.6 - 1e-5);
        Assert.Equal(body, StaticFloorPrecisionAlignment.Align(body, [floor]));
        Assert.True(AabbCollisionDetector.Overlaps(body, floor.Bounds));
    }

    [Fact]
    public void SidePenetration_IsNotNormalized()
    {
        StaticAabbCollider wall = new(25, Box(35, 17, 36, 20));
        double right = Math.BitIncrement(35.0);
        Aabb body = Box(right - 0.8, 18, right, 19.6);
        Assert.Equal(body, StaticFloorPrecisionAlignment.Align(body, [wall]));
    }

    [Fact]
    public void AlreadyFlushAndClearBodies_DoNotMove()
    {
        StaticAabbCollider floor = new(11794, Box(33, 17, 35, 18));
        Aabb flush = Box(34, 18, 34.8, 19.6);
        Aabb clear = Box(34, 18.2, 34.8, 19.8);
        Assert.Equal(flush, StaticFloorPrecisionAlignment.Align(flush, [floor]));
        Assert.Equal(clear, StaticFloorPrecisionAlignment.Align(clear, [floor]));
    }

    [Fact]
    public void AmbiguousCorner_DoesNotEscapeTheOtherCollider()
    {
        var floor = new StaticAabbCollider(1, Box(33, 17, 35, 18));
        var wall = new StaticAabbCollider(2, Box(35, 17, 36, 20));
        double bottom = Math.BitDecrement(18.0);
        double right = Math.BitIncrement(35.0);
        Aabb body = Box(right - 0.8, bottom, right, bottom + 1.6);
        Assert.Equal(body, StaticFloorPrecisionAlignment.Align(body, [floor, wall]));
    }
}
