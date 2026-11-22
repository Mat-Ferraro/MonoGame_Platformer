using System;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>
/// Stage 12 GPU-contract reference: back-rims must be independently occluded
/// by other blockers, not their own wall, and cannot wrap onto away-facing edges.
/// </summary>
public sealed class LBasicStage12RimOcclusionTests
{
    private static readonly BasicShadowShape Wall = new(
        new BasicShadowRectangle(.60, .30, .20, .40), BasicShadowShapeKind.Rectangle);
    private static readonly BasicShadowShape Occluder = new(
        new BasicShadowRectangle(.38, .40, .10, .20), BasicShadowShapeKind.Rectangle);
    private static readonly LightPoint Light = new(.15, .50);
    private static readonly LightPoint LeftEdgePixel = new(.603, .50);

    [Fact]
    public void TheWallItselfCannotOccludeItsOwnBackRim()
    {
        var blockers = new[] { Wall };
        double rim = BasicPolygonBackRimReference.SampleVisible(Wall,
            LeftEdgePixel, Light, false, 1000, 800, blockers, 0, true, 6);
        Assert.True(rim > .05, $"Expected self-shadow exemption, received {rim}");
    }

    [Theory]
    [InlineData(BasicShadowShapeKind.Rectangle, .603)]
    [InlineData(BasicShadowShapeKind.Triangle, .653)]
    [InlineData(BasicShadowShapeKind.Trapezoid, .628)]
    public void OtherBlockerSuppressesBackRimOnEveryPolygonType(
        BasicShadowShapeKind kind, double pixelX)
    {
        var wall = new BasicShadowShape(Wall.Bounds, kind);
        var pixel = new LightPoint(pixelX, .50);
        Assert.True(wall.Contains(pixel));
        var blockers = new[] { wall, Occluder };
        double unoccluded = BasicPolygonBackRimReference.SampleVisible(wall,
            pixel, Light, false, 1000, 800, new[] { wall }, 0, true, 6);
        double occluded = BasicPolygonBackRimReference.SampleVisible(wall,
            pixel, Light, false, 1000, 800, blockers, 0, true, 6);
        Assert.True(unoccluded > 0, $"No baseline rim for {kind}: {unoccluded}");
        Assert.Equal(0, occluded);
    }

    [Fact]
    public void ALightWithShadowCastingDisabledDoesNotSuppressItsOwnRim()
    {
        var blockers = new[] { Wall, Occluder };
        Assert.Equal(0, BasicPolygonBackRimReference.SampleVisible(Wall,
            LeftEdgePixel, Light, false, 1000, 800, blockers, 0, true, 6));
        Assert.True(BasicPolygonBackRimReference.SampleVisible(Wall,
            LeftEdgePixel, Light, false, 1000, 800, blockers, 0, false, 6) > 0);
    }

    [Fact]
    public void ABlockerOnTheOtherSideDoesNotSuppressVisibleRim()
    {
        var remote = new BasicShadowShape(
            new BasicShadowRectangle(.85, .40, .08, .20), BasicShadowShapeKind.Rectangle);
        var blockers = new[] { Wall, remote };
        Assert.True(BasicPolygonBackRimReference.SampleVisible(Wall,
            LeftEdgePixel, Light, false, 1000, 800, blockers, 0, true, 6) > 0);
    }

    [Fact]
    public void CompactedSlotsExcludeOnlyTheIntendedWallEvenWithIdenticalGeometry()
    {
        // Both walls are geometrically identical: shape equality must not be
        // used to skip EVERY matching caster. Slot identity is authoritative.
        var blockers = new[] { Wall, Occluder, Wall };
        Assert.Equal(0, BasicPolygonBackRimReference.SampleVisible(Wall,
            LeftEdgePixel, Light, false, 1000, 800, blockers, 2, true, 6));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BasicPolygonBackRimReference.SampleVisible(Wall,
                LeftEdgePixel, Light, false, 1000, 800, blockers, 3, true));
    }

    [Fact]
    public void RearFacingEdgeHasNoRimFromAnExternalLight()
    {
        var blockers = new[] { Wall };
        Assert.Equal(0, BasicPolygonBackRimReference.SampleVisible(Wall,
            new LightPoint(.797, .50), Light, false, 1000, 800, blockers, 0, true, 6));
        Assert.Equal(0, BasicPolygonBackRimReference.SampleVisible(Wall,
            new LightPoint(.797, .50), new LightPoint(.15, .50), false, 1000, 800, blockers, 0, true, 6));
    }

    [Theory]
    [InlineData(BasicShadowShapeKind.Rectangle, .797)]
    [InlineData(BasicShadowShapeKind.Triangle, .747)]
    [InlineData(BasicShadowShapeKind.Trapezoid, .772)]
    public void AngledBackFacingSilhouetteEdgesNeverReceiveStrayRims(
        BasicShadowShapeKind kind, double edgePixelX)
    {
        var wall = new BasicShadowShape(Wall.Bounds, kind);
        var pixel = new LightPoint(edgePixelX, .50);
        Assert.True(wall.Contains(pixel));
        Assert.Equal(0, BasicPolygonBackRimReference.Sample(wall,
            pixel, Light, false, 1000, 800, 6));
    }

    [Fact]
    public void CenteredInsideLightDoesNotCreateUndirectedRing()
    {
        var light = new LightPoint(.70, .50);
        Assert.True(Wall.Contains(light));
        Assert.Equal(0, BasicPolygonBackRimReference.Sample(Wall,
            new LightPoint(.603, .50), light, true, 1000, 800, 6));
        Assert.Equal(0, BasicPolygonBackRimReference.Sample(Wall,
            new LightPoint(.797, .50), light, true, 1000, 800, 6));
    }

    [Fact]
    public void EachLightVisibilityIsEvaluatedSeparately()
    {
        var blockers = new[] { Wall, Occluder };
        double blocked = BasicPolygonBackRimReference.SampleVisible(Wall,
            LeftEdgePixel, Light, false, 1000, 800, blockers, 0, true, 6);
        double fromCloserUnobstructedLight = BasicPolygonBackRimReference.SampleVisible(Wall,
            LeftEdgePixel, new LightPoint(.54, .50), false,
            1000, 800, blockers, 0, true, 6);
        Assert.Equal(0, blocked);
        Assert.True(fromCloserUnobstructedLight > 0);
    }
}
