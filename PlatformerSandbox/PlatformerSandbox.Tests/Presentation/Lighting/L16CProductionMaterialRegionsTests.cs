using System;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L16CProductionMaterialRegionsTests
{
    [Fact]
    public void UnrotatedCenteredSpriteUsesOnlyItsPaddedScreenFootprint()
    {
        var region = ProductionMaterialRegionPlanner.ForSprite(
            100, 100, 40, 20, .5, .5, 0, 320, 200, paddingPixels: 2);
        Assert.Equal(new ProductionMaterialRegion(78, 88, 44, 24), region);
        Assert.Equal(44 * 24, region.Area);
        Assert.True(region.Area < 320 * 200);
    }

    [Fact]
    public void NinetyDegreeRotationSwapsCoverageWithoutCuttingCorners()
    {
        var region = ProductionMaterialRegionPlanner.ForSprite(
            100, 100, 40, 20, .5, .5, 90, 320, 200, paddingPixels: 2);
        Assert.Equal(new ProductionMaterialRegion(88, 78, 24, 44), region);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(27.5)]
    [InlineData(45)]
    [InlineData(89.99)]
    [InlineData(90)]
    [InlineData(135)]
    [InlineData(179)]
    [InlineData(270)]
    [InlineData(359.5)]
    public void ConservativeRegionIncludesEachVisibleRotatedCorner(double degrees)
    {
        const double px = .27, py = .81, w = 90, h = 31, x = 102, y = 87;
        var r = ProductionMaterialRegionPlanner.ForSprite(x, y, w, h,
            px, py, degrees, 320, 200);
        Assert.False(r.IsEmpty);
        double cos = Math.Cos(degrees * Math.PI / 180);
        double sin = Math.Sin(degrees * Math.PI / 180);
        foreach (double cx in new[] { -px * w, (1 - px) * w })
        foreach (double cy in new[] { -py * h, (1 - py) * h })
        {
            double sx = x + cos * cx - sin * cy;
            double sy = y + sin * cx + cos * cy;
            if (sx >= 0 && sx < 320 && sy >= 0 && sy < 200)
            {
                Assert.InRange(sx, r.X, r.X + r.Width);
                Assert.InRange(sy, r.Y, r.Y + r.Height);
            }
        }
    }

    [Fact]
    public void CroppingToViewportKeepsLongPartiallyVisibleArtwork()
    {
        var r = ProductionMaterialRegionPlanner.ForSprite(
            -150, 120, 400, 20, 0, 0, 0, 300, 200);
        Assert.Equal(0, r.X);
        Assert.True(r.Width > 200);
        Assert.True(r.Height > 20);
        Assert.True(r.X + r.Width <= 300);
        Assert.True(r.Y + r.Height <= 200);
    }

    [Fact]
    public void OffscreenSurfaceAvoidsAllMaterialPasses()
    {
        var r = ProductionMaterialRegionPlanner.ForSprite(
            -10000, 50, 100, 100, .5, .5, 30, 640, 480);
        Assert.True(r.IsEmpty);
        Assert.Equal(0, r.Area);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidProjectionFailsOpenToUnclippedViewport(double nonFinite)
    {
        var r = ProductionMaterialRegionPlanner.ForSprite(
            nonFinite, 50, 60, 20, .5, .5, 0, 640, 480);
        Assert.Equal(ProductionMaterialRegion.Full(640, 480), r);
    }

    [Fact]
    public void OverflowingRemoteCoordinatesCannotWrapToVisiblePixels()
    {
        var r = ProductionMaterialRegionPlanner.ForSprite(
            double.MaxValue / 2, double.MaxValue / 2, 64, 64, .5, .5,
            0, 640, 480);
        Assert.True(r.IsEmpty);
    }

    [Fact]
    public void ZeroAreaSpriteIsEmptyAndInvalidPaddingIsRejected()
    {
        Assert.True(ProductionMaterialRegionPlanner.ForSprite(
            50, 50, 0, 20, .5, .5, 0, 100, 100).IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProductionMaterialRegionPlanner.ForSprite(
                50, 50, 30, 20, .5, .5, 0, 100, 100, -1));
    }

    [Fact]
    public void MirrorAndNegativeRotationDoNotExpandTheSilhouette()
    {
        var clockwise = ProductionMaterialRegionPlanner.ForSprite(
            180, 130, 66, 32, .5, .5, 45, 400, 300);
        var counterclockwise = ProductionMaterialRegionPlanner.ForSprite(
            180, 130, 66, 32, .5, .5, -45, 400, 300);
        Assert.Equal(clockwise.Width, counterclockwise.Width);
        Assert.Equal(clockwise.Height, counterclockwise.Height);
    }
}
