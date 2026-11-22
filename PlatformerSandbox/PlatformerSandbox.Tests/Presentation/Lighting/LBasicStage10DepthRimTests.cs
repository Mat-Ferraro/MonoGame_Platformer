using System;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>
/// CPU contract for the Stage 10 2.5D light-to-blocker depth rule.
/// The GPU appearance and alpha-gradient rim remain visual acceptance tests.
/// </summary>
public sealed class LBasicStage10DepthRimTests
{
    private static readonly BasicShadowShape Wall = new(
        new BasicShadowRectangle(.30, .30, .2, .4), BasicShadowShapeKind.Rectangle);

    [Fact]
    public void AllLayersStartInWorldWithEditorOutlinesHidden()
    {
        var state = new BasicLightingPreviewState();
        Assert.Equal(BasicLightingDepthLayer.World, state.SelectedLightDepthLayer);
        Assert.Equal(BasicLightingDepthLayer.World, state.SelectedShadowBlockerDepthLayer);
        Assert.False(state.ShowBlockerOutlines);
        state.ToggleBlockerOutlines();
        Assert.True(state.ShowBlockerOutlines);
        state.ToggleBlockerOutlines();
        Assert.False(state.ShowBlockerOutlines);
    }

    [Theory]
    [InlineData(BasicShadowShapeKind.Rectangle)]
    [InlineData(BasicShadowShapeKind.Triangle)]
    [InlineData(BasicShadowShapeKind.Trapezoid)]
    public void LowerLayerNeverIlluminatesFaceEvenWithoutOriginOverlap(BasicShadowShapeKind kind)
    {
        var state = new BasicLightingPreviewState();
        var wall = new BasicShadowShape(new BasicShadowRectangle(.30, .30, .2, .4), kind);
        state.SetLightPositionNormalized(.20, .45); // Outside polygon, radius may still overlap.
        Assert.False(wall.Contains(state.LightPositionNormalized));
        state.SetSelectedLightDepthLayer(BasicLightingDepthLayer.Back);
        Assert.True(state.GetWallLightOverlap(wall, BasicLightingDepthLayer.World).Light1Behind);
        Assert.True(state.GetWallLightOverlap(wall, BasicLightingDepthLayer.Front).Light1Behind);
        Assert.False(state.GetWallLightOverlap(wall, BasicLightingDepthLayer.Back).Light1Behind);
        Assert.False(state.GetWallLightOverlap(wall, BasicLightingDepthLayer.World).Light2Behind);
    }

    [Fact]
    public void SameLayerPreservesExistingFrontAndBehindOriginOverlapPolicies()
    {
        var state = new BasicLightingPreviewState();
        state.SetLightPositionNormalized(.4, .5);
        state.SetSelectedLightWallOverlapMode(BasicLightWallOverlapMode.FrontOfWall);
        Assert.Equal(new BasicWallLightOverlap(false, false),
            state.GetWallLightOverlap(Wall, BasicLightingDepthLayer.World));
        state.SetSelectedLightWallOverlapMode(BasicLightWallOverlapMode.BehindWallWithRim);
        Assert.True(state.GetWallLightOverlap(Wall, BasicLightingDepthLayer.World).Light1Behind);
        state.SetLightPositionNormalized(.2, .5);
        Assert.False(state.GetWallLightOverlap(Wall, BasicLightingDepthLayer.World).Light1Behind);
        state.SetSelectedLightDepthLayer(BasicLightingDepthLayer.Front);
        state.SetLightPositionNormalized(.4, .5);
        Assert.False(state.GetWallLightOverlap(Wall, BasicLightingDepthLayer.World).Light1Behind);
    }

    [Fact]
    public void DepthRoutingOnlyChangesTheParticularLightAndWall()
    {
        var state = new BasicLightingPreviewState();
        state.SetSelectedLightDepthLayer(BasicLightingDepthLayer.Back);
        state.SelectLight(1);
        state.SetLightEnabled(true);
        state.SetSelectedLightDepthLayer(BasicLightingDepthLayer.World);
        Assert.Equal(new BasicWallLightOverlap(true, false),
            state.GetWallLightOverlap(Wall, BasicLightingDepthLayer.World));
        Assert.Equal(new BasicWallLightOverlap(false, false),
            state.GetWallLightOverlap(Wall, BasicLightingDepthLayer.Back));
        Assert.Equal(new BasicWallLightOverlap(true, true),
            state.GetWallLightOverlap(Wall, BasicLightingDepthLayer.Front));
    }

    [Fact]
    public void BacklightRimContextIdentifiesInsideOriginsAndWallCenter()
    {
        var state = new BasicLightingPreviewState();
        state.SetLightPositionNormalized(.4, .5);
        var inside = state.GetWallRimContext(Wall);
        Assert.True(inside.Light1Inside);
        Assert.False(inside.Light2Inside);
        // A polygon vertex average can differ from the exact decimal coordinates
        // by a few ULPs (for example 0.49999999999999994 vs 0.5).
        // Verify the geometric position within numerical precision, not bit-for-bit.
        Assert.Equal(.4, inside.WallCenter.X, precision: 12);
        Assert.Equal(.5, inside.WallCenter.Y, precision: 12);
        state.SetLightPositionNormalized(.2, .5);
        Assert.False(state.GetWallRimContext(Wall).Light1Inside);
    }

    [Fact]
    public void NewDepthDemoShowsExternalBehindLightWithoutChangingOldDemo()
    {
        var state = new BasicLightingPreviewState();
        state.LoadDepthRimComparison();
        Assert.Equal(BasicLightingDepthLayer.Back, state.SelectedLightDepthLayer);
        Assert.Equal(BasicLightingDepthLayer.Front, state.SelectedShadowBlockerDepthLayer);
        Assert.Equal(BasicLightWallOverlapMode.FrontOfWall, state.SelectedLightWallOverlapMode);
        var wall = state.GetShadowShape(2);
        Assert.False(wall.Contains(state.LightPositionNormalized));
        Assert.True(state.GetWallLightOverlap(wall, state.SelectedShadowBlockerDepthLayer).Light2Behind);

        state.LoadWallOverlapComparison();
        Assert.Equal(BasicLightingDepthLayer.World, state.SelectedLightDepthLayer);
        Assert.Equal(BasicLightingDepthLayer.World, state.SelectedShadowBlockerDepthLayer);
        Assert.True(state.GetWallLightOverlap(state.GetShadowShape(2)).Light2Behind);
    }

    [Fact]
    public void ResetPreservesExpectedDefaultsAndRejectsInvalidDepths()
    {
        var state = new BasicLightingPreviewState();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.SetSelectedLightDepthLayer((BasicLightingDepthLayer)77));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.SetSelectedShadowBlockerDepthLayer((BasicLightingDepthLayer)(-1)));
        state.LoadDepthRimComparison();
        state.ToggleBlockerOutlines();
        state.ResetShadows();
        Assert.False(state.ShowBlockerOutlines);
        Assert.Equal(BasicLightingDepthLayer.World, state.SelectedLightDepthLayer);
        Assert.Equal(BasicLightingDepthLayer.World, state.SelectedShadowBlockerDepthLayer);
        state.LoadDepthRimComparison();
        state.Reset();
        Assert.Equal(BasicLightingDepthLayer.World, state.SelectedLightDepthLayer);
        Assert.Equal(BasicLightingDepthLayer.World, state.SelectedShadowBlockerDepthLayer);
    }
}
