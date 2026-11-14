using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Tests.Foundation.Geometry;

public sealed class AabbTests
{
    private static Aabb Box(double left, double bottom, double right, double top) =>
        new(new Vector2D(left, bottom), new Vector2D(right, top));

    [Fact]
    public void ConstructsBoxFromMinMax()
    {
        Aabb box = Box(-2, 1, 4, 9);
        Assert.Equal(-2, box.Left);
        Assert.Equal(4, box.Right);
        Assert.Equal(1, box.Bottom);
        Assert.Equal(9, box.Top);
        Assert.Equal(6, box.Width);
        Assert.Equal(8, box.Height);
        Assert.Equal(new Vector2D(1, 5), box.Center);
    }

    [Fact]
    public void DefaultAabbIsInvalid()
    {
        Assert.False(default(Aabb).IsValid);
        Assert.True(Box(0, 0, 1, 1).IsValid);
    }

    [Fact]
    public void ConstructsBoxFromCenterAndHalfExtents()
    {
        Aabb box = Aabb.FromCenterAndHalfExtents(new Vector2D(10, 20), new Vector2D(2, 3));
        Assert.Equal(Box(8, 17, 12, 23), box);
    }

    [Fact]
    public void TranslationPreservesDimensionsAndDoesNotChangeOriginal()
    {
        Aabb original = Box(0, 1, 2, 3);
        Aabb translated = original.Translated(new Vector2D(5, -2));
        Assert.Equal(Box(5, -1, 7, 1), translated);
        Assert.Equal(Box(0, 1, 2, 3), original);
    }

    [Fact]
    public void ZeroWidthAndHeightAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Box(1, 0, 1, 2));
        Assert.Throws<ArgumentException>(() => Box(0, 1, 2, 1));
    }

    [Fact]
    public void InvertedExtentsAreRejected()
    {
        Assert.Throws<ArgumentException>(() => Box(2, 0, 1, 2));
        Assert.Throws<ArgumentException>(() => Box(0, 2, 2, 1));
    }

    [Fact]
    public void NonpositiveHalfExtentsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Aabb.FromCenterAndHalfExtents(Vector2D.Zero, new Vector2D(0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Aabb.FromCenterAndHalfExtents(Vector2D.Zero, new Vector2D(1, -1)));
    }

    [Fact]
    public void OverflowedBoxWidthIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Box(-double.MaxValue, 0, double.MaxValue, 1));

    [Fact]
    public void OverflowedConstructionIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Aabb.FromCenterAndHalfExtents(new Vector2D(double.MaxValue * 0.75, 0), new Vector2D(double.MaxValue * 0.5, 1)));

    [Fact]
    public void OverflowedTranslationIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Box(double.MaxValue * 0.5, 0, double.MaxValue * 0.75, 1)
                .Translated(new Vector2D(double.MaxValue, 0)));
}
