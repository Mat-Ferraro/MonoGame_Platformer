using System;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>
/// Headless geometry and state acceptance for Stage 14. GPU sprite/occluder
/// alignment and actual shadow/rim appearance require visual device tests.
/// </summary>
public sealed class LBasicStage14RotatingBlockerTests
{
    private static void Close(double expected, double actual, double tolerance = 1e-9) =>
        Assert.InRange(Math.Abs(expected - actual), 0, tolerance);

    [Fact]
    public void ZeroAnglePreservesStage8AxisAlignedPolygonCoordinates()
    {
        var bounds = new BasicShadowRectangle(.31, .22, .13, .36);
        foreach (BasicShadowShapeKind kind in Enum.GetValues<BasicShadowShapeKind>())
        {
            LightPoint[] oldVertices = new BasicShadowShape(bounds, kind).Vertices();
            LightPoint[] newVertices = new BasicShadowShape(bounds, kind, 0, 16.0 / 9).Vertices();
            Assert.Equal(oldVertices, newVertices);
        }
    }

    [Fact]
    public void NinetyDegreeRotationIsMeasuredInScreenSpaceNotNormalizedUnits()
    {
        const int width = 1600, height = 900;
        var bounds = new BasicShadowRectangle(.35, .35, .20, .20);
        var original = new BasicShadowShape(bounds, BasicShadowShapeKind.Rectangle, 0, (double)width / height);
        var rotated = new BasicShadowShape(bounds, BasicShadowShapeKind.Rectangle, 90, (double)width / height);
        LightPoint[] before = original.Vertices(), after = rotated.Vertices();
        double cx = (bounds.X + bounds.Width * .5) * width;
        double cy = (bounds.Y + bounds.Height * .5) * height;
        for (int i = 0; i < before.Length; i++)
        {
            double dx = before[i].X * width - cx, dy = before[i].Y * height - cy;
            Close(cx - dy, after[i].X * width);
            Close(cy + dx, after[i].Y * height);
        }
    }

    [Theory]
    [InlineData(BasicShadowShapeKind.Rectangle)]
    [InlineData(BasicShadowShapeKind.Triangle)]
    [InlineData(BasicShadowShapeKind.Trapezoid)]
    public void RotationPreservesEdgeLengthAndConvexSilhouette(BasicShadowShapeKind kind)
    {
        const int width = 1280, height = 720;
        var box = new BasicShadowRectangle(.42, .30, .12, .30);
        LightPoint[] before = new BasicShadowShape(box, kind).Vertices();
        var rotated = new BasicShadowShape(box, kind, 47, (double)width / height);
        LightPoint[] after = rotated.Vertices();
        Assert.Equal(before.Length, after.Length);
        for (int i = 0; i < before.Length; i++)
        {
            int j = (i + 1) % before.Length;
            double beforeLength = Math.Sqrt(Math.Pow((before[i].X - before[j].X) * width, 2) +
                Math.Pow((before[i].Y - before[j].Y) * height, 2));
            double afterLength = Math.Sqrt(Math.Pow((after[i].X - after[j].X) * width, 2) +
                Math.Pow((after[i].Y - after[j].Y) * height, 2));
            Close(beforeLength, afterLength);
        }
        double centroidX = 0, centroidY = 0;
        foreach (LightPoint v in after) { centroidX += v.X; centroidY += v.Y; }
        Assert.True(rotated.Contains(new LightPoint(centroidX / after.Length,
            centroidY / after.Length)));
    }

    [Fact]
    public void RotatedRectangleChangesSegmentOcclusionAndClickSelection()
    {
        var state = new BasicLightingPreviewState();
        state.SetShadowViewportAspectRatio(16.0 / 9);
        state.SetShadowBlockerEnabled(true);
        state.SetShadowBlockerPosition(.35, .45);
        // Stay within the editor limits (width <= .20, height >= .08).
        // The former .30 x .06 was clamped to .20 x .08, making the
        // original Y=.25 ray miss the wall even after 90-degree rotation.
        state.SetShadowBlockerSize(.20, .08);
        Assert.Equal(.20, state.ShadowBlocker.Width, 12);
        Assert.Equal(.08, state.ShadowBlocker.Height, 12);
        var light = new LightPoint(.2, .40);
        var receiver = new LightPoint(.8, .40);
        Assert.False(state.GetShadowShape(0).Blocks(light, receiver));
        state.SetSelectedBlockerRotationAngle(90);
        Assert.True(state.GetShadowShape(0).Blocks(light, receiver));
        Assert.True(state.SelectShadowBlockerAt(.45, .40));
        // Inside the unrotated bounds, but outside the rotated silhouette.
        Assert.False(state.SelectShadowBlockerAt(.36, .46));
    }

    [Theory]
    [InlineData(BasicShadowShapeKind.Rectangle)]
    [InlineData(BasicShadowShapeKind.Triangle)]
    [InlineData(BasicShadowShapeKind.Trapezoid)]
    public void RotatedLightFacingRimsRemainVisibleOnlyWhenUnobstructed(BasicShadowShapeKind kind)
    {
        const int width = 1280, height = 720;
        var wall = new BasicShadowShape(new BasicShadowRectangle(.43, .37, .14, .26),
            kind, 32, (double)width / height);
        var vertices = wall.Vertices();
        // The midpoint of the first sloping/horizontal edge avoids corner
        // ambiguity. Sample 1.5 screen pixels inward along its outward normal.
        LightPoint a = vertices[0], b = vertices[1];
        double ax = a.X * width, ay = a.Y * height;
        double bx = b.X * width, by = b.Y * height;
        double dx = bx - ax, dy = by - ay;
        double len = Math.Sqrt(dx * dx + dy * dy);
        double outwardX = dy / len, outwardY = -dx / len;
        double mx = (ax + bx) * .5, my = (ay + by) * .5;
        var pixel = new LightPoint((mx - 1.5 * outwardX) / width,
            (my - 1.5 * outwardY) / height);
        var light = new LightPoint((mx + 110 * outwardX) / width,
            (my + 110 * outwardY) / height);
        Assert.True(wall.Contains(pixel));
        double visible = BasicPolygonBackRimReference.SampleVisible(wall, pixel, light,
            false, width, height, new[] { wall }, 0, true);
        Assert.True(visible > .05, $"Expected visible rim, got {visible}");

        var obstacle = new BasicShadowShape(new BasicShadowRectangle(
            (mx + 55 * outwardX) / width - .02,
            (my + 55 * outwardY) / height - .02,
            .04, .04), BasicShadowShapeKind.Rectangle);
        Assert.True(obstacle.Blocks(light, pixel));
        Close(0, BasicPolygonBackRimReference.SampleVisible(wall, pixel, light,
            false, width, height, new[] { wall, obstacle }, 0, true));
        Close(visible, BasicPolygonBackRimReference.SampleVisible(wall, pixel, light,
            false, width, height, new[] { wall, obstacle }, 0, false));
    }

    [Fact]
    public void RotationIsIndependentFromExistingTranslationAndPauseControls()
    {
        var state = new BasicLightingPreviewState();
        state.SetShadowBlockerEnabled(true);
        state.SetSelectedBlockerRotationAngle(30);
        state.SetSelectedBlockerRotationMode(BasicBlockerRotationMode.Clockwise);
        state.SetSelectedBlockerAngularSpeed(60);
        state.SetSelectedBlockerMotionMode(BasicBlockerMotionMode.Horizontal);
        state.SetSelectedBlockerMotionDistance(.10);
        state.AdvanceBlockerMotion(.5);
        Close(60, state.SelectedBlockerRotationAngle);
        Assert.NotEqual(BasicLightingPreviewState.DefaultBlocker, state.ShadowBlocker);
        var frozenAngle = state.SelectedBlockerRotationAngle;
        var frozenPosition = state.ShadowBlocker;
        state.SetBlockerMotionPlaying(false);
        state.AdvanceBlockerMotion(9);
        Close(frozenAngle, state.SelectedBlockerRotationAngle);
        Assert.Equal(frozenPosition, state.ShadowBlocker);
        state.SetBlockerMotionPlaying(true);
        state.AdvanceBlockerMotion(.5);
        Close(90, state.SelectedBlockerRotationAngle);
    }

    [Fact]
    public void OscillatingRotationIsReversibleAndFramePartitionIndependent()
    {
        BasicLightingPreviewState Setup()
        {
            var state = new BasicLightingPreviewState();
            state.SetShadowBlockerEnabled(true);
            state.SetSelectedBlockerRotationAngle(20);
            state.SetSelectedBlockerRotationMode(BasicBlockerRotationMode.Oscillate);
            state.SetSelectedBlockerAngularSpeed(60);
            state.SetSelectedBlockerRotationSweep(30);
            return state;
        }
        var a = Setup();
        var b = Setup();
        a.AdvanceBlockerMotion(.5); // 30 degrees / 60 degrees per second
        for (int i = 0; i < 5; i++) b.AdvanceBlockerMotion(.1);
        Close(50, a.SelectedBlockerRotationAngle);
        Close(a.SelectedBlockerRotationAngle, b.SelectedBlockerRotationAngle);
        a.AdvanceBlockerMotion(.5);
        Close(20, a.SelectedBlockerRotationAngle);
        a.AdvanceBlockerMotion(.5);
        Close(350, a.SelectedBlockerRotationAngle);
        a.AdvanceBlockerMotion(.5);
        Close(20, a.SelectedBlockerRotationAngle);
    }

    [Fact]
    public void ResetAndOlderPresetsClearRotationWithoutResettingOtherFeatures()
    {
        var state = new BasicLightingPreviewState();
        state.LoadRotatingShadowComparison();
        Assert.Equal(3, state.ActiveShadowBlockerCount);
        Assert.Equal(BasicBlockerRotationMode.Clockwise, state.GetBlockerRotationMode(1));
        Assert.Equal(BasicBlockerRotationMode.Oscillate, state.GetBlockerRotationMode(2));
        Assert.Equal(BasicBlockerMotionMode.Horizontal, state.GetBlockerMotionMode(2));
        Assert.Equal(BasicShadowShapeKind.Trapezoid, state.GetShadowShape(2).Kind);
        var shape = state.GetShadowShape(2);
        state.AdvanceBlockerMotion(.25);
        Assert.NotEqual(shape, state.GetShadowShape(2));
        state.LoadPolygonShadowComparison();
        for (int i = 0; i < BasicLightingPreviewState.ShadowBlockerCount; i++)
        {
            Assert.Equal(BasicBlockerRotationMode.Static, state.GetBlockerRotationMode(i));
            Close(0, state.GetBlockerRotationAngle(i));
        }
        state.SelectShadowBlocker(1);
        state.SetSelectedBlockerRotationAngle(45);
        var position = state.GetShadowBlocker(1);
        state.ResetSelectedBlockerRotation();
        Close(0, state.SelectedBlockerRotationAngle);
        Assert.Equal(position, state.GetShadowBlocker(1));
    }

    [Fact]
    public void InvalidRotationInputsAreRejected()
    {
        var state = new BasicLightingPreviewState();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetSelectedBlockerRotationAngle(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetSelectedBlockerAngularSpeed(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetSelectedBlockerRotationSweep(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetSelectedBlockerRotationMode((BasicBlockerRotationMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetShadowViewportAspectRatio(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BasicShadowShape(
            BasicLightingPreviewState.DefaultBlocker, BasicShadowShapeKind.Rectangle,
            double.NaN).Validate());
    }
}
