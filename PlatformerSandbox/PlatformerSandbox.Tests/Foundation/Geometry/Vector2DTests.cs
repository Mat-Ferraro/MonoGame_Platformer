using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Tests.Foundation.Geometry;

public sealed class Vector2DTests
{
    [Fact]
    public void StoresFiniteComponents() =>
        Assert.Equal(new Vector2D(3, -4), new Vector2D(3, -4));

    [Fact]
    public void DefaultVectorIsZero() =>
        Assert.Equal(Vector2D.Zero, default(Vector2D));

    [Fact]
    public void AdditionAndSubtractionAreComponentwise()
    {
        Vector2D a = new(2, -3);
        Vector2D b = new(-4, 6);
        Assert.Equal(new Vector2D(-2, 3), a + b);
        Assert.Equal(new Vector2D(6, -9), a - b);
    }

    [Fact]
    public void MultiplicationAndNegationAreComponentwise()
    {
        Vector2D a = new(2, -3);
        Assert.Equal(new Vector2D(5, -7.5), a * 2.5);
        Assert.Equal(new Vector2D(5, -7.5), 2.5 * a);
        Assert.Equal(new Vector2D(-2, 3), -a);
    }

    [Fact]
    public void NonfiniteXIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Vector2D(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Vector2D(double.PositiveInfinity, 0));
    }

    [Fact]
    public void NonfiniteYIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Vector2D(0, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Vector2D(0, double.NegativeInfinity));
    }

    [Fact]
    public void OverflowDuringVectorOperationIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new Vector2D(double.MaxValue, 0) + new Vector2D(double.MaxValue, 0));
}
