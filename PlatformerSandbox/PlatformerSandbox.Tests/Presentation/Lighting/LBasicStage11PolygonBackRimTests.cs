using System;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>
/// Stage 11: math contract for true segment-distance, light-facing-only rims.
/// Separate DesktopGL visual approval is still required for GPU shader output.
/// </summary>
public sealed class LBasicStage11PolygonBackRimTests
{
    private static BasicShadowShape Shape(BasicShadowShapeKind kind) =>
        new(new BasicShadowRectangle(.30, .25, .30, .50), kind);

    [Theory]
    [InlineData(BasicShadowShapeKind.Rectangle, .598, .302)]
    [InlineData(BasicShadowShapeKind.Triangle, .522, .378)]
    [InlineData(BasicShadowShapeKind.Trapezoid, .559, .341)]
    public void ExternalRightBacklightIlluminatesOnlyRightFacingEdge(
        BasicShadowShapeKind kind, double rightX, double leftX)
    {
        var shape = Shape(kind);
        var light = new LightPoint(.90, .50);
        double right = BasicPolygonBackRimReference.Sample(shape,
            new LightPoint(rightX, .50), light, false, 1000, 800, 6.0);
        double left = BasicPolygonBackRimReference.Sample(shape,
            new LightPoint(leftX, .50), light, false, 1000, 800, 6.0);
        Assert.True(right > 0.10, $"Expected right-facing rim on {kind}, got {right}");
        Assert.Equal(0, left);
    }

    [Theory]
    [InlineData(BasicShadowShapeKind.Rectangle)]
    [InlineData(BasicShadowShapeKind.Triangle)]
    [InlineData(BasicShadowShapeKind.Trapezoid)]
    public void ExternalLeftBacklightCannotIlluminateShadowFacingRightSide(BasicShadowShapeKind kind)
    {
        var shape = Shape(kind);
        var vertices = shape.Vertices();
        // Test the midpoint of the rightmost edge, moved slightly toward the
        // shape center. This covers sloped normals and axis-aligned walls.
        int rightEdge = kind == BasicShadowShapeKind.Rectangle ? 1 :
            kind == BasicShadowShapeKind.Triangle ? 0 : 1;
        var a = vertices[rightEdge];
        var b = vertices[(rightEdge + 1) % vertices.Length];
        double midX = (a.X + b.X) * .5, midY = (a.Y + b.Y) * .5;
        // Midpoint-to-center gives a point guaranteed to be just inside.
        var interior = new LightPoint(midX + (.45 - midX) * .02,
            midY + (.50 - midY) * .02);
        Assert.True(shape.Contains(interior));
        Assert.Equal(0, BasicPolygonBackRimReference.Sample(shape,
            interior, new LightPoint(.1, .5), false, 1000, 800, 10));
    }

    [Fact]
    public void TriangleSlopedEdgeHasScreenPixelWidthNotHorizontalSampleWidth()
    {
        var shape = Shape(BasicShadowShapeKind.Triangle);
        var light = new LightPoint(.9, .5);
        // Right diagonal: from (.45,.25) to (.60,.75).
        // Compute offsets perpendicular to the edge in SCREEN pixels.
        double ex = 150, ey = 400;
        double length = Math.Sqrt(ex * ex + ey * ey);
        double inwardX = -ey / length, inwardY = ex / length;
        var midpoint = new LightPoint(.525, .5);
        LightPoint PointAtPixels(double d) => new(midpoint.X + d * inwardX / 1000,
            midpoint.Y + d * inwardY / 800);
        double near = BasicPolygonBackRimReference.Sample(shape, PointAtPixels(1), light,
            false, 1000, 800, 6);
        double far = BasicPolygonBackRimReference.Sample(shape, PointAtPixels(5), light,
            false, 1000, 800, 6);
        Assert.True(near > far && far > 0, $"Near={near}, far={far}");
        Assert.Equal(0, BasicPolygonBackRimReference.Sample(shape, PointAtPixels(7), light,
            false, 1000, 800, 6));
    }

    [Fact]
    public void InternalOffCenterLightHasNoWrongSideMinimum()
    {
        var shape = Shape(BasicShadowShapeKind.Rectangle);
        var light = new LightPoint(.54, .5); // interior, toward the right
        Assert.True(shape.Contains(light));
        Assert.True(BasicPolygonBackRimReference.Sample(shape, new LightPoint(.598, .5),
            light, true, 1000, 800, 6) > .1);
        Assert.Equal(0, BasicPolygonBackRimReference.Sample(shape, new LightPoint(.302, .5),
            light, true, 1000, 800, 6));
    }

    [Fact]
    public void NearestEdgeOverridesOtherLightFacingEdgesAtCorners()
    {
        var shape = Shape(BasicShadowShapeKind.Rectangle);
        // Right edge faces the light; bottom edge does not. A pixel just inside
        // bottom-right but closer to bottom must not inherit the right edge rim.
        var light = new LightPoint(.90, .40);
        var pixel = new LightPoint(.597, .749);
        Assert.True(shape.Contains(pixel));
        Assert.Equal(0, BasicPolygonBackRimReference.Sample(shape, pixel, light,
            false, 1000, 800, 6));
    }

    [Fact]
    public void SlopedWallRimOriginUsesPolygonCentroidRatherThanBoundingBox()
    {
        var state = new BasicLightingPreviewState();
        var triangle = Shape(BasicShadowShapeKind.Triangle);
        var center = state.GetWallRimContext(triangle).WallCenter;
        Assert.Equal(.45, center.X, 12);
        Assert.Equal((.25 + .75 + .75) / 3, center.Y, 12);
    }

    [Fact]
    public void DepthRimPresetIsolatesAngledRimsFromProjectedShadowWedges()
    {
        var state = new BasicLightingPreviewState();
        state.LoadDepthRimComparison();
        Assert.Equal(BasicShadowShapeKind.Triangle, state.GetShadowShape(1).Kind);
        Assert.Equal(BasicShadowShapeKind.Trapezoid, state.GetShadowShape(2).Kind);
        Assert.False(state.SelectedLightCastsShadows);
        state.SelectLight(0);
        Assert.False(state.SelectedLightCastsShadows);
        state.LoadWallOverlapComparison();
        Assert.True(state.SelectedLightCastsShadows); // Other presets unchanged.
    }

    [Fact]
    public void BacklightRimCannotAppearOutsideTheWallSilhouette()
    {
        var shape = Shape(BasicShadowShapeKind.Trapezoid);
        Assert.Equal(0, BasicPolygonBackRimReference.Sample(shape,
            new LightPoint(.7, .5), new LightPoint(.9, .5), false, 1000, 800));
    }
}
