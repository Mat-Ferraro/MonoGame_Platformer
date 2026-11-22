using System;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>
/// Deterministic Stage-13 presentation-motion contract. GPU shadow and rim
/// appearance still requires the moving-scene visual acceptance checklist.
/// </summary>
public sealed class LBasicStage13DynamicBlockerTests
{
    private static void Close(double expected, double actual, double tolerance = 1e-10) =>
        Assert.InRange(Math.Abs(expected - actual), 0, tolerance);

    [Fact]
    public void ExistingShadowPresetsRemainStaticByDefault()
    {
        var state = new BasicLightingPreviewState();
        for (int i = 0; i < BasicLightingPreviewState.ShadowBlockerCount; i++)
            Assert.Equal(BasicBlockerMotionMode.Static, state.GetBlockerMotionMode(i));
        state.LoadPolygonShadowComparison();
        BasicShadowRectangle before = state.GetShadowBlocker(1);
        state.AdvanceBlockerMotion(10);
        Assert.Equal(before, state.GetShadowBlocker(1));
        Close(.40, state.SelectedBlockerMotionSpeed);
    }

    [Fact]
    public void HorizontalPingPongReturnsToItsOriginalAnchorWithoutDrift()
    {
        var state = new BasicLightingPreviewState();
        state.SetShadowBlockerEnabled(true);
        state.SetShadowBlockerPosition(.30, .35);
        state.SetSelectedBlockerMotionMode(BasicBlockerMotionMode.Horizontal);
        state.SetSelectedBlockerMotionSpeed(.5); // Two seconds per round trip.
        state.SetSelectedBlockerMotionDistance(.2);
        state.AdvanceBlockerMotion(.5);
        Close(.40, state.ShadowBlocker.X);
        Close(.35, state.ShadowBlocker.Y);
        state.AdvanceBlockerMotion(.5);
        Close(.50, state.ShadowBlocker.X);
        state.AdvanceBlockerMotion(.5);
        Close(.40, state.ShadowBlocker.X);
        state.AdvanceBlockerMotion(.5);
        Close(.30, state.ShadowBlocker.X);
        // Thousands of trips are evaluated from the anchor, not accumulated.
        state.AdvanceBlockerMotion(2_000);
        Close(.30, state.ShadowBlocker.X);
    }

    [Fact]
    public void FrameChunkingProducesTheSamePositionAndPausedMotionDoesNotStep()
    {
        BasicLightingPreviewState Setup()
        {
            var s = new BasicLightingPreviewState();
            s.SetShadowBlockerEnabled(true);
            s.SetShadowBlockerPosition(.40, .20);
            s.SetSelectedBlockerMotionMode(BasicBlockerMotionMode.Vertical);
            s.SetSelectedBlockerMotionSpeed(.8);
            s.SetSelectedBlockerMotionDistance(.25);
            return s;
        }
        var single = Setup();
        var split = Setup();
        single.AdvanceBlockerMotion(.3125);
        for (int i = 0; i < 10; i++) split.AdvanceBlockerMotion(.03125);
        Close(single.ShadowBlocker.Y, split.ShadowBlocker.Y);
        Close(.40, split.ShadowBlocker.X);
        split.SetBlockerMotionPlaying(false);
        var frozen = split.ShadowBlocker;
        split.AdvanceBlockerMotion(30);
        Assert.Equal(frozen, split.ShadowBlocker);
        split.SetBlockerMotionPlaying(true);
        split.AdvanceBlockerMotion(.3125);
        Assert.NotEqual(frozen, split.ShadowBlocker);
    }

    [Fact]
    public void DraggingReanchorsInsteadOfSnappingBackToPreviousAnimationPath()
    {
        var state = new BasicLightingPreviewState();
        state.SetShadowBlockerEnabled(true);
        state.SetSelectedBlockerMotionMode(BasicBlockerMotionMode.Horizontal);
        state.AdvanceBlockerMotion(.5);
        state.SetSelectedShadowBlockerPosition(.12, .26); // Editor drag.
        Close(.12, state.ShadowBlocker.X);
        state.AdvanceBlockerMotion(0); // Release frame.
        Close(.12, state.ShadowBlocker.X);
        state.AdvanceBlockerMotion(.5);
        Assert.True(state.ShadowBlocker.X > .12);
        state.ResetSelectedBlockerMotion();
        var stopped = state.ShadowBlocker;
        state.AdvanceBlockerMotion(2);
        Assert.Equal(stopped, state.ShadowBlocker);
        Assert.Equal(BasicBlockerMotionMode.Static, state.SelectedBlockerMotionMode);
    }

    [Fact]
    public void BlockersAtTheViewportBoundaryTravelInwardsAndStayInBounds()
    {
        var state = new BasicLightingPreviewState();
        state.SetShadowBlockerEnabled(true);
        state.SetShadowBlockerPosition(.92, .27);
        double start = state.ShadowBlocker.X;
        state.SetSelectedBlockerMotionMode(BasicBlockerMotionMode.Horizontal);
        state.SetSelectedBlockerMotionDistance(.20);
        state.SetSelectedBlockerMotionSpeed(.5);
        state.AdvanceBlockerMotion(1);
        Assert.True(state.ShadowBlocker.X < start); // There is no room on right.
        state.ShadowBlocker.Validate();
        state.AdvanceBlockerMotion(1);
        Close(start, state.ShadowBlocker.X);
    }

    [Fact]
    public void MovingOnlyOneEnabledSlotLeavesOthersUntouched()
    {
        var state = new BasicLightingPreviewState();
        state.LoadMultiShadowComparison();
        var first = state.GetShadowBlocker(0);
        var third = state.GetShadowBlocker(2);
        state.SelectShadowBlocker(1);
        state.SetSelectedBlockerMotionMode(BasicBlockerMotionMode.Vertical);
        state.SetSelectedBlockerMotionSpeed(.5);
        state.AdvanceBlockerMotion(.5);
        Assert.Equal(first, state.GetShadowBlocker(0));
        Assert.Equal(third, state.GetShadowBlocker(2));
        Assert.NotEqual(BasicLightingPreviewState.DefaultBlocker2, state.GetShadowBlocker(1));
    }

    [Fact]
    public void MovingGeometryImmediatelyChangesOcclusionAndRimReferencePosition()
    {
        var state = new BasicLightingPreviewState();
        state.SetShadowBlockerEnabled(true);
        state.SetShadowBlockerSize(.1, .2);
        state.SetShadowBlockerPosition(.4, .35);
        var light = new LightPoint(.15, .45);
        var receiver = new LightPoint(.55, .45);
        Assert.True(state.ActiveShadowShapes[0].Blocks(light, receiver));
        var centerBefore = state.GetWallRimContext(state.GetShadowShape(0)).WallCenter;
        state.SetSelectedBlockerMotionMode(BasicBlockerMotionMode.Horizontal);
        state.SetSelectedBlockerMotionSpeed(.5);
        state.SetSelectedBlockerMotionDistance(.30);
        state.AdvanceBlockerMotion(1);
        var moved = state.ActiveShadowShapes[0];
        Assert.False(moved.Blocks(light, receiver));
        var centerAfter = state.GetWallRimContext(moved).WallCenter;
        Assert.True(centerAfter.X > centerBefore.X);
    }

    [Fact]
    public void MotionPresetRetainsTwoColoredLightsAndUsesAllThreePolygonTypes()
    {
        var state = new BasicLightingPreviewState();
        state.LoadMovingShadowComparison();
        Assert.Equal(3, state.ActiveShadowBlockerCount);
        Assert.True(state.BlockerMotionPlaying);
        Assert.Equal(BasicBlockerMotionMode.Vertical, state.GetBlockerMotionMode(0));
        Assert.Equal(BasicBlockerMotionMode.Horizontal, state.GetBlockerMotionMode(1));
        Assert.Equal(BasicBlockerMotionMode.Horizontal, state.GetBlockerMotionMode(2));
        Assert.Equal(BasicShadowShapeKind.Triangle, state.GetShadowShape(1).Kind);
        Assert.Equal(BasicShadowShapeKind.Trapezoid, state.GetShadowShape(2).Kind);
        Assert.Equal(BasicLightingDepthLayer.Front, state.GetShadowBlockerDepthLayer(2));
        var before = state.GetShadowShape(2);
        state.AdvanceBlockerMotion(.4);
        var after = state.GetShadowShape(2);
        Assert.NotEqual(before, after);
        Assert.True(state.GetWallLightOverlap(after, BasicLightingDepthLayer.Front).Light2Behind);
        state.LoadPolygonShadowComparison(); // Old preset resets motion completely.
        Assert.Equal(BasicBlockerMotionMode.Static, state.GetBlockerMotionMode(2));
    }

    [Fact]
    public void InvalidElapsedTimeAndMotionParametersAreRejected()
    {
        var state = new BasicLightingPreviewState();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.AdvanceBlockerMotion(-.01));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.AdvanceBlockerMotion(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetSelectedBlockerMotionSpeed(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetSelectedBlockerMotionDistance(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.SetSelectedBlockerMotionMode((BasicBlockerMotionMode)99));
    }
}
