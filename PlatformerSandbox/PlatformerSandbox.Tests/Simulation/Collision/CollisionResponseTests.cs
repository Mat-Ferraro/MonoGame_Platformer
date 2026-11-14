using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Resolution;

namespace PlatformerSandbox.Tests.Simulation.Collision;

public sealed class CollisionResponseTests
{
    [Theory]
    [InlineData(4, 2, -1, 0, 0, 2)]
    [InlineData(-4, 2, 1, 0, 0, 2)]
    [InlineData(2, -4, 0, 1, 2, 0)]
    [InlineData(2, 4, 0, -1, 2, 0)]
    public void SlideRemovesOnlyInwardComponent(
        double vx, double vy, double nx, double ny, double outX, double outY)
    {
        var actual = CollisionResponse.Apply(new Vector2D(vx, vy),
            new Vector2D(nx, ny), CollisionResponseMode.Slide);
        Assert.Equal(new Vector2D(outX, outY), actual);
    }

    [Theory]
    [InlineData(-4, 2, -1, 0)]
    [InlineData(4, 2, 1, 0)]
    [InlineData(2, 4, 0, 1)]
    [InlineData(2, -4, 0, -1)]
    public void SlideDoesNotAffectVelocityMovingAwayFromSurface(
        double vx, double vy, double nx, double ny)
    {
        var incoming = new Vector2D(vx, vy);
        Assert.Equal(incoming, CollisionResponse.Apply(incoming,
            new Vector2D(nx, ny), CollisionResponseMode.Slide));
    }

    [Fact]
    public void PureTangentialVelocityIsUnchanged() =>
        Assert.Equal(new Vector2D(0, 7), CollisionResponse.Apply(
            new Vector2D(0, 7), new Vector2D(-1, 0), CollisionResponseMode.Slide));

    [Fact]
    public void StopRemovesBothVelocityComponents() =>
        Assert.Equal(Vector2D.Zero, CollisionResponse.Apply(
            new Vector2D(3, -9), new Vector2D(0, 1), CollisionResponseMode.Stop));

    [Fact]
    public void ZeroNormalIsRejected() =>
        Assert.Throws<ArgumentException>(() => CollisionResponse.Apply(
            new Vector2D(1, 2), Vector2D.Zero, CollisionResponseMode.Slide));

    [Fact]
    public void DiagonalNormalIsRejected() =>
        Assert.Throws<ArgumentException>(() => CollisionResponse.Apply(
            new Vector2D(1, 2), new Vector2D(1, 1), CollisionResponseMode.Slide));

    [Fact]
    public void UnknownPolicyIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CollisionResponse.Apply(
            new Vector2D(1, 2), new Vector2D(1, 0), (CollisionResponseMode)99));
}
