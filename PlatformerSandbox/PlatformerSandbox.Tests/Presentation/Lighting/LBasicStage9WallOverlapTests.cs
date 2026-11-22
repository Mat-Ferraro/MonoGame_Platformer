using System;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>
/// CPU-side contract for the Stage-9 front / behind-rim wall-light policy.
/// GPU appearance and shader state must still be inspected in the running lab.
/// </summary>
public sealed class LBasicStage9WallOverlapTests
{
    [Fact]
    public void DefaultIsFrontAndChangingOneLightDoesNotChangeTheOther()
    {
        var state = new BasicLightingPreviewState();
        Assert.Equal(BasicLightWallOverlapMode.FrontOfWall, state.SelectedLightWallOverlapMode);
        state.SetSelectedLightWallOverlapMode(BasicLightWallOverlapMode.BehindWallWithRim);
        state.SelectLight(1);
        Assert.Equal(BasicLightWallOverlapMode.FrontOfWall, state.SelectedLightWallOverlapMode);
        state.SetSelectedLightWallOverlapMode(BasicLightWallOverlapMode.BehindWallWithRim);
        state.SelectLight(0);
        Assert.Equal(BasicLightWallOverlapMode.BehindWallWithRim, state.SelectedLightWallOverlapMode);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.SetSelectedLightWallOverlapMode((BasicLightWallOverlapMode)22));
    }

    [Fact]
    public void BackRimNeedsEnabledLightOriginInsideExactSilhouette()
    {
        var state = new BasicLightingPreviewState();
        state.SelectLight(0);
        state.SetLightPositionNormalized(.48, .5);
        state.SetSelectedLightWallOverlapMode(BasicLightWallOverlapMode.BehindWallWithRim);
        var wall = new BasicShadowShape(new BasicShadowRectangle(.4, .3, .2, .4),
            BasicShadowShapeKind.Triangle);
        Assert.True(state.GetWallLightOverlap(wall).Light1Behind);
        Assert.False(state.GetWallLightOverlap(wall).Light2Behind);

        // Inside the triangle's bounding rectangle, but outside its silhouette.
        state.SetLightPositionNormalized(.405, .305);
        Assert.True(wall.Bounds.Contains(new LightPoint(.405, .305)));
        Assert.False(wall.Contains(new LightPoint(.405, .305)));
        Assert.False(state.GetWallLightOverlap(wall).Light1Behind);
        state.SetLightPositionNormalized(.48, .5);
        state.SetLightEnabled(false);
        Assert.False(state.GetWallLightOverlap(wall).Light1Behind);
    }

    [Fact]
    public void RadiusIntersectionWithoutOriginOverlapIsNotSpecialCase()
    {
        var state = new BasicLightingPreviewState();
        state.SetSelectedLightWallOverlapMode(BasicLightWallOverlapMode.BehindWallWithRim);
        state.SetLightPositionNormalized(.299, .45);
        state.SetLightRadius(.90);
        var wall = new BasicShadowShape(new BasicShadowRectangle(.3, .3, .15, .3),
            BasicShadowShapeKind.Rectangle);
        Assert.False(state.GetWallLightOverlap(wall).Light1Behind);
        // Outside-origin light still casts through the wall to a receiver.
        Assert.True(wall.Blocks(new LightPoint(.299, .45), new LightPoint(.8, .45)));
    }

    [Theory]
    [InlineData(BasicShadowShapeKind.Rectangle)]
    [InlineData(BasicShadowShapeKind.Triangle)]
    [InlineData(BasicShadowShapeKind.Trapezoid)]
    public void OverlappingLightDoesNotCauseItsWallToCastButOtherWallStillBlocks(
        BasicShadowShapeKind kind)
    {
        var overlap = new BasicShadowShape(new BasicShadowRectangle(.20, .20, .20, .40), kind);
        var other = new BasicShadowShape(new BasicShadowRectangle(.60, .20, .1, .40),
            BasicShadowShapeKind.Rectangle);
        var light = new LightPoint(.30, .48);
        var receiver = new LightPoint(.90, .48);
        Assert.True(overlap.Contains(light));
        Assert.False(overlap.Blocks(light, receiver));
        Assert.True(other.Blocks(light, receiver));
    }

    [Fact]
    public void ComparisonPresetShowsOneFrontAndOneBehindWithIndependentWalls()
    {
        var state = new BasicLightingPreviewState();
        state.LoadWallOverlapComparison();
        Assert.Equal(3, state.ActiveShadowBlockerCount);
        Assert.Equal(BasicLightingDebugView.Final, state.DebugView);
        Assert.Equal(BasicLightWallOverlapMode.BehindWallWithRim, state.SelectedLightWallOverlapMode);
        Assert.Equal(2, state.SelectedShadowBlockerIndex);
        Assert.False(state.GetWallLightOverlap(state.GetShadowShape(1)).Light1Behind);
        Assert.True(state.GetWallLightOverlap(state.GetShadowShape(2)).Light2Behind);
        Assert.False(state.GetWallLightOverlap(state.GetShadowShape(0)).Light1Behind);
        Assert.False(state.GetWallLightOverlap(state.GetShadowShape(0)).Light2Behind);
        var lights = state.BuildMaterialLights(new LightingViewport(1280, 720,
            new LightPoint(0, 0), 1));
        Assert.Equal(2, lights.Count);
        Assert.All(lights, light => Assert.True(light.Light.CastShadows));
    }

    [Fact]
    public void LightOneAndTwoMayBothBeBehindSameWall()
    {
        var state = new BasicLightingPreviewState();
        var wall = new BasicShadowShape(new BasicShadowRectangle(.25, .3, .5, .4),
            BasicShadowShapeKind.Trapezoid);
        state.SetLightPositionNormalized(.40, .50);
        state.SetSelectedLightWallOverlapMode(BasicLightWallOverlapMode.BehindWallWithRim);
        state.SelectLight(1);
        state.SetLightEnabled(true);
        state.SetLightPositionNormalized(.60, .50);
        state.SetSelectedLightWallOverlapMode(BasicLightWallOverlapMode.BehindWallWithRim);
        Assert.Equal(new BasicWallLightOverlap(true, true), state.GetWallLightOverlap(wall));
        state.SetSelectedLightWallOverlapMode(BasicLightWallOverlapMode.FrontOfWall);
        Assert.Equal(new BasicWallLightOverlap(true, false), state.GetWallLightOverlap(wall));
    }

    [Fact]
    public void ClearAndLegacyPresetsRestoreFrontPolicyWithoutChangingLightingMaterial()
    {
        var state = new BasicLightingPreviewState();
        state.LoadWallOverlapComparison();
        state.SelectSurface(3);
        state.SetSelectedSurfaceOpacity(.33);
        state.ResetShadows();
        Assert.Equal(BasicLightWallOverlapMode.FrontOfWall, state.SelectedLightWallOverlapMode);
        Assert.Equal(.33, state.GetSurface(3).Opacity, 6);
        state.LoadWallOverlapComparison();
        state.LoadPolygonShadowComparison();
        state.SelectLight(1);
        Assert.Equal(BasicLightWallOverlapMode.FrontOfWall, state.SelectedLightWallOverlapMode);
        state.LoadWallOverlapComparison();
        state.Reset();
        state.SelectLight(1);
        Assert.Equal(BasicLightWallOverlapMode.FrontOfWall, state.SelectedLightWallOverlapMode);
    }
}
