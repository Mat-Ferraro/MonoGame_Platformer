using System;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class LBasicStage6HardShadowTests
{
    private static readonly LightingViewport View =
        new(1280, 720, new LightPoint(0, 0), 1);
    private static readonly BasicShadowRectangle Blocker = new(.45, .35, .10, .30);

    [Fact]
    public void DefaultLabRemainsIdenticalUntilShadowComparisonRequested()
    {
        var state = new BasicLightingPreviewState();
        Assert.False(state.ShadowBlockerEnabled);
        Assert.Null(state.ActiveShadowBlocker);
        Assert.False(state.SelectedLightCastsShadows);
        Assert.All(state.BuildMaterialLights(View), light => Assert.False(light.Light.CastShadows));
        for (int i = 0; i < BasicLightingPreviewState.SurfaceCount; i++)
            Assert.True(state.GetSurface(i).ReceivesShadows);
        Assert.Equal(BasicLightingDebugView.Final, state.DebugView);
    }

    [Fact]
    public void RectangleBlocksOnlyTheOpenSegmentBehindTheCaster()
    {
        Blocker.Validate();
        Assert.True(Blocker.Blocks(new LightPoint(.20, .50), new LightPoint(.80, .50)));
        Assert.True(Blocker.Blocks(new LightPoint(.80, .50), new LightPoint(.20, .50)));
        Assert.False(Blocker.Blocks(new LightPoint(.20, .20), new LightPoint(.20, .80)));
        Assert.False(Blocker.Blocks(new LightPoint(.20, .50), new LightPoint(.35, .50)));
        Assert.False(Blocker.Blocks(new LightPoint(.50, .50), new LightPoint(.80, .50)));
        Assert.False(Blocker.Blocks(new LightPoint(.20, .50), new LightPoint(.50, .50)));
        Assert.False(Blocker.Blocks(new LightPoint(.20, .35), new LightPoint(.80, .35)));
        Assert.False(Blocker.Blocks(new LightPoint(.20, .35), new LightPoint(.45, .35)));
    }

    [Fact]
    public void ShadowCastingIsIndependentPerLightAndDoesNotChangeReceiverChannels()
    {
        var state = new BasicLightingPreviewState();
        state.LoadShadowComparison();
        Assert.True(state.ActiveShadowBlocker.HasValue);
        Assert.Equal(BasicLightingPreviewState.DefaultBlocker, state.ActiveShadowBlocker.Value);
        Assert.Equal(2, state.BuildMaterialLights(View).Count);
        Assert.All(state.BuildMaterialLights(View), light => Assert.True(light.Light.CastShadows));
        state.SelectLight(0);
        state.SetSelectedLightCastsShadows(false);
        Assert.False(state.BuildMaterialLights(View)[0].Light.CastShadows);
        Assert.True(state.BuildMaterialLights(View)[1].Light.CastShadows);
        Assert.Equal(BasicLightReceiverChannels.Light1, state.BuildMaterialLights(View)[0].Channel);
        Assert.Equal(BasicLightReceiverChannels.Light2, state.BuildMaterialLights(View)[1].Channel);
        state.SelectLight(1);
        state.SetLightEnabled(false);
        Assert.Single(state.BuildMaterialLights(View));
        Assert.Equal("basics.1", state.BuildMaterialLights(View)[0].Light.Id);
        state.SetLightEnabled(true);
        Assert.True(state.SelectedLightCastsShadows);
    }

    [Fact]
    public void ComparisonPresetPlacesBothShadowSamplesBehindTheirAssignedLight()
    {
        var state = new BasicLightingPreviewState();
        state.LoadShadowComparison();
        state.SelectLight(0);
        var light1 = state.LightPositionNormalized;
        state.SelectLight(1);
        var light2 = state.LightPositionNormalized;
        Assert.True(state.ShadowBlocker.Blocks(light1, new LightPoint(.315, .694)));
        Assert.True(state.ShadowBlocker.Blocks(light2, new LightPoint(.684, .694)));
        Assert.Equal(BasicLightReceiverChannels.Light1, state.GetSurface(1).Receives);
        Assert.Equal(BasicLightReceiverChannels.Light2, state.GetSurface(3).Receives);
        Assert.False(state.ShadowBlocker.Blocks(light1, new LightPoint(.684, .694)));
        Assert.False(state.ShadowBlocker.Blocks(light2, new LightPoint(.315, .694)));
    }

    [Fact]
    public void MaterialReceivesShadowsIndependentlyOfLightingOrOpacity()
    {
        var state = new BasicLightingPreviewState();
        state.SelectSurface(1);
        var prior = state.SelectedSurface;
        state.SetSelectedSurfaceReceivesShadows(false);
        Assert.False(state.GetSurface(1).ReceivesShadows);
        Assert.True(state.GetSurface(0).ReceivesShadows);
        Assert.Equal(prior.Mode, state.GetSurface(1).Mode);
        Assert.Equal(prior.Receives, state.GetSurface(1).Receives);
        Assert.Equal(prior.Opacity, state.GetSurface(1).Opacity);
        state.SetSelectedSurfaceShading(BasicSurfaceShadingMode.Hybrid);
        Assert.False(state.SelectedSurface.ReceivesShadows);
        state.ResetSelectedSurface();
        Assert.True(state.SelectedSurface.ReceivesShadows);
    }

    [Fact]
    public void BlockerControlsClampStayInsideViewportAndRejectNonFinite()
    {
        var state = new BasicLightingPreviewState();
        state.SetShadowBlockerSize(20, 20);
        Assert.Equal(.20, state.ShadowBlocker.Width, 10);
        Assert.Equal(.55, state.ShadowBlocker.Height, 10);
        state.SetShadowBlockerPosition(90, 90);
        Assert.Equal(.80, state.ShadowBlocker.X, 10);
        Assert.Equal(.45, state.ShadowBlocker.Y, 10);
        state.ShadowBlocker.Validate();
        state.SetShadowBlockerSize(-3, -3);
        Assert.Equal(.025, state.ShadowBlocker.Width, 10);
        Assert.Equal(.08, state.ShadowBlocker.Height, 10);
        state.ShadowBlocker.Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetShadowBlockerPosition(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetShadowBlockerSize(.1, double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BasicShadowRectangle(-.1, 0, .2, .2).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new BasicShadowRectangle(.9, .9, .2, .2).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new BasicShadowRectangle(0, 0, 0, .2).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => Blocker.Blocks(new LightPoint(double.NaN, 0),
            new LightPoint(.8, .8)));
    }

    [Fact]
    public void ShadowOnlyResetPreservesLightsAndMaterialsButGlobalResetRestoresDefaults()
    {
        var state = new BasicLightingPreviewState();
        state.LoadShadowComparison();
        state.SelectSurface(3);
        state.SetSelectedSurfaceReceivesShadows(false);
        state.SetSelectedSurfaceOpacity(.22);
        state.ResetShadows();
        Assert.False(state.ShadowBlockerEnabled);
        Assert.False(state.SelectedLightCastsShadows);
        state.SelectLight(1);
        Assert.False(state.SelectedLightCastsShadows);
        Assert.Equal(.22, state.GetSurface(3).Opacity);
        Assert.True(state.GetSurface(3).ReceivesShadows);
        Assert.Equal(2, state.BuildMaterialLights(View).Count);
        state.SetShadowBlockerEnabled(true);
        state.SetDebugView(BasicLightingDebugView.ShadowMask);
        state.Reset();
        Assert.False(state.ShadowBlockerEnabled);
        Assert.Equal(BasicLightingPreviewState.DefaultBlocker, state.ShadowBlocker);
        Assert.Equal(BasicLightingDebugView.Final, state.DebugView);
        Assert.Equal(.55, state.GetSurface(3).Opacity);
        Assert.Single(state.BuildMaterialLights(View));
    }
}
