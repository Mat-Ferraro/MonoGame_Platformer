using System;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>Headless GPU/CPU convex-shadow contract; GPU screenshots are still required.</summary>
public sealed class LBasicStage8PolygonShadowTests
{
    private static readonly BasicShadowRectangle Bounds = new(.3, .2, .3, .5);

    [Theory]
    [InlineData(BasicShadowShapeKind.Rectangle, 4)]
    [InlineData(BasicShadowShapeKind.Triangle, 3)]
    [InlineData(BasicShadowShapeKind.Trapezoid, 4)]
    public void ShapeVerticesAreFiniteInsideAuthoredBoundsAndConvex(BasicShadowShapeKind kind, int count)
    {
        var shape = new BasicShadowShape(Bounds, kind);
        LightPoint[] vertices = shape.Vertices();
        Assert.Equal(count, vertices.Length);
        Assert.All(vertices, v =>
        {
            Assert.InRange(v.X, Bounds.X, Bounds.Right);
            Assert.InRange(v.Y, Bounds.Y, Bounds.Bottom);
            Assert.True(shape.Contains(v));
        });
        Assert.True(shape.Contains(new LightPoint(.45, .55)));
    }

    [Theory]
    [InlineData(BasicShadowShapeKind.Triangle)]
    [InlineData(BasicShadowShapeKind.Trapezoid)]
    public void PolygonIntersectsRayThroughBodyButNotOutsideRay(BasicShadowShapeKind kind)
    {
        var shape = new BasicShadowShape(Bounds, kind);
        Assert.True(shape.Blocks(new LightPoint(.1, .50), new LightPoint(.8, .50)));
        Assert.False(shape.Blocks(new LightPoint(.1, .12), new LightPoint(.8, .12)));
        Assert.False(shape.Contains(new LightPoint(.31, .21)));
        Assert.True(Bounds.Contains(new LightPoint(.31, .21))); // silhouette != bounds
    }

    [Theory]
    [InlineData(BasicShadowShapeKind.Triangle)]
    [InlineData(BasicShadowShapeKind.Trapezoid)]
    public void LightOrReceiverInsideDoesNotSelfShadow(BasicShadowShapeKind kind)
    {
        var shape = new BasicShadowShape(Bounds, kind);
        Assert.False(shape.Blocks(new LightPoint(.45, .5), new LightPoint(.8, .5)));
        Assert.False(shape.Blocks(new LightPoint(.1, .5), new LightPoint(.45, .5)));
    }

    [Theory]
    [InlineData(BasicShadowShapeKind.Triangle)]
    [InlineData(BasicShadowShapeKind.Trapezoid)]
    public void OpenSegmentGlidingAlongHorizontalEdgeDoesNotShadow(BasicShadowShapeKind kind)
    {
        var shape = new BasicShadowShape(Bounds, kind);
        Assert.False(shape.Blocks(new LightPoint(.1, Bounds.Bottom),
            new LightPoint(.8, Bounds.Bottom)));
    }

    [Fact]
    public void DifferentLightsGetDifferentVisibilityThroughSameTriangle()
    {
        var shape = new BasicShadowShape(Bounds, BasicShadowShapeKind.Triangle);
        LightPoint receiver = new(.8, .5);
        Assert.True(shape.Blocks(new LightPoint(.1, .5), receiver));
        Assert.False(shape.Blocks(new LightPoint(.8, .12), receiver));
    }

    [Fact]
    public void PolygonPresetPreservesRectanglePresetAndIndependentLightRules()
    {
        var state = new BasicLightingPreviewState();
        state.LoadPolygonShadowComparison();
        Assert.Equal(3, state.ActiveShadowBlockerCount);
        Assert.Equal(new[] { BasicShadowShapeKind.Rectangle, BasicShadowShapeKind.Triangle,
            BasicShadowShapeKind.Trapezoid }, state.ActiveShadowShapes.Select(s => s.Kind));
        state.SelectLight(0);
        state.SetSelectedLightCastsShadows(false);
        var view = new LightingViewport(1280, 720, new LightPoint(0, 0), 1);
        Assert.False(state.BuildMaterialLights(view)[0].Light.CastShadows);
        Assert.True(state.BuildMaterialLights(view)[1].Light.CastShadows);
        state.LoadShadowComparison();
        Assert.Single(state.ActiveShadowShapes);
        Assert.Equal(BasicShadowShapeKind.Rectangle, state.ActiveShadowShapes[0].Kind);
    }

    [Fact]
    public void AddRemoveReusesSlotsAndReportsExplicitGpuBudget()
    {
        var state = new BasicLightingPreviewState();
        Assert.True(state.AddShadowBlocker());
        Assert.Equal(0, state.SelectedShadowBlockerIndex);
        Assert.True(state.AddShadowBlocker(BasicShadowShapeKind.Triangle));
        Assert.True(state.AddShadowBlocker(BasicShadowShapeKind.Trapezoid));
        Assert.False(state.AddShadowBlocker());
        Assert.Equal(3, state.ActiveShadowBlockerCount);
        state.SelectShadowBlocker(1);
        Assert.True(state.RemoveSelectedShadowBlocker());
        Assert.Equal(2, state.ActiveShadowBlockerCount);
        Assert.Equal(BasicShadowShapeKind.Trapezoid, state.GetShadowShape(2).Kind);
        Assert.True(state.AddShadowBlocker(BasicShadowShapeKind.Triangle));
        Assert.Equal(1, state.SelectedShadowBlockerIndex);
        Assert.Equal(3, state.ActiveShadowBlockerCount);
        Assert.Equal(BasicShadowShapeKind.Triangle, state.SelectedShadowBlockerKind);
    }

    [Fact]
    public void ClickSelectionUsesActualPolygonRatherThanRectangularBounds()
    {
        var state = new BasicLightingPreviewState();
        state.LoadPolygonShadowComparison();
        state.SelectShadowBlocker(0);
        var triangle = state.GetShadowShape(1);
        var outsideTriangle = new LightPoint(triangle.Bounds.X + .002,
            triangle.Bounds.Y + .002);
        Assert.False(triangle.Contains(outsideTriangle));
        Assert.False(state.SelectShadowBlockerAt(outsideTriangle.X, outsideTriangle.Y));
        Assert.Equal(0, state.SelectedShadowBlockerIndex);
        LightPoint insideTriangle = new(triangle.Bounds.X + triangle.Bounds.Width / 2,
            triangle.Bounds.Y + triangle.Bounds.Height * .75);
        Assert.True(state.SelectShadowBlockerAt(insideTriangle.X, insideTriangle.Y));
        Assert.Equal(1, state.SelectedShadowBlockerIndex);
        Assert.False(state.SelectShadowBlockerAt(double.NaN, .3));
    }

    [Fact]
    public void ClearAndFullResetRemoveAllPolygonGeometryButNotMaterialOpacity()
    {
        var state = new BasicLightingPreviewState();
        state.LoadPolygonShadowComparison();
        state.SelectSurface(3);
        state.SetSelectedSurfaceOpacity(.31);
        state.ResetShadows();
        Assert.Empty(state.ActiveShadowShapes);
        Assert.Equal(0, state.SelectedShadowBlockerIndex);
        Assert.Equal(BasicShadowShapeKind.Rectangle, state.GetShadowShape(1).Kind);
        Assert.Equal(.31, state.GetSurface(3).Opacity, 8);
        state.AddShadowBlocker(BasicShadowShapeKind.Triangle);
        state.Reset();
        Assert.Empty(state.ActiveShadowShapes);
        Assert.Equal(.55, state.GetSurface(3).Opacity, 8);
    }

    [Fact]
    public void MalformedKindAndGeometryFailFast()
    {
        var state = new BasicLightingPreviewState();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.AddShadowBlocker((BasicShadowShapeKind)20));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetSelectedShadowBlockerKind((BasicShadowShapeKind)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BasicShadowShape(
            new BasicShadowRectangle(.3, .1, -.2, .4), BasicShadowShapeKind.Triangle).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new BasicShadowShape(Bounds,
            BasicShadowShapeKind.Rectangle).Blocks(new LightPoint(double.NaN, .1), new LightPoint(.7, .3)));
    }
}
