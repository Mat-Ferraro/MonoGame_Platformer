using System;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class LBasicStage4MaterialParticipationTests
{
    private static LightingViewport View =>
        new(1280, 720, new LightPoint(0, 0), 1);

    [Fact]
    public void DefaultSamplesCoverBothLightsOneLightUnlitAndTransparency()
    {
        var state = new BasicLightingPreviewState();
        Assert.Equal(4, BasicLightingPreviewState.SurfaceCount);
        Assert.Equal(new BasicSurfaceMaterial(BasicSurfaceLightingMode.Lit,
            BasicLightReceiverChannels.Both, 1), state.GetSurface(0));
        Assert.Equal(new BasicSurfaceMaterial(BasicSurfaceLightingMode.Lit,
            BasicLightReceiverChannels.Light1, 1), state.GetSurface(1));
        Assert.Equal(new BasicSurfaceMaterial(BasicSurfaceLightingMode.Unlit,
            BasicLightReceiverChannels.None, 1), state.GetSurface(2));
        Assert.Equal(new BasicSurfaceMaterial(BasicSurfaceLightingMode.Lit,
            BasicLightReceiverChannels.Light2, .55), state.GetSurface(3));
    }

    [Fact]
    public void SelectionChangesOnlySelectedSurface()
    {
        var state = new BasicLightingPreviewState();
        state.SelectSurface(1);
        state.SetSelectedSurfaceMode(BasicSurfaceLightingMode.Unlit);
        state.SetSelectedSurfaceChannels(BasicLightReceiverChannels.Light2);
        state.SetSelectedSurfaceOpacity(.25);
        Assert.Equal(new BasicSurfaceMaterial(BasicSurfaceLightingMode.Unlit,
            BasicLightReceiverChannels.Light2, .25), state.GetSurface(1));
        Assert.Equal(BasicSurfaceLightingMode.Lit, state.GetSurface(0).Mode);
        Assert.Equal(1, state.GetSurface(0).Opacity);
        Assert.Equal(.55, state.GetSurface(3).Opacity);
        state.ResetSelectedSurface();
        Assert.Equal(new BasicSurfaceMaterial(BasicSurfaceLightingMode.Lit,
            BasicLightReceiverChannels.Light1, 1), state.GetSurface(1));
    }

    [Fact]
    public void ReceiverChannelsAreIndependentOfLightingModeAndAmbient()
    {
        var state = new BasicLightingPreviewState();
        state.SetAmbient(.23);
        state.SelectSurface(2);
        state.SetSelectedSurfaceChannels(BasicLightReceiverChannels.Both);
        Assert.Equal(BasicSurfaceLightingMode.Unlit, state.SelectedSurface.Mode);
        Assert.Equal(BasicLightReceiverChannels.Both, state.SelectedSurface.Receives);
        Assert.Equal(.23, state.Ambient);
        state.SetSelectedSurfaceMode(BasicSurfaceLightingMode.Lit);
        state.SetSelectedSurfaceChannels(BasicLightReceiverChannels.None);
        Assert.Equal(BasicLightReceiverChannels.None, state.SelectedSurface.Receives);
        Assert.Equal(BasicSurfaceLightingMode.Lit, state.SelectedSurface.Mode);
        Assert.True(state.SelectedSurface.ReceivesAmbient);
        Assert.False(state.SelectedSurface.ReceivesLight(BasicLightReceiverChannels.Light1));
        Assert.False(state.SelectedSurface.ReceivesLight(BasicLightReceiverChannels.Light2));
        state.SetSelectedSurfaceChannels(BasicLightReceiverChannels.Light2);
        Assert.False(state.SelectedSurface.ReceivesLight(BasicLightReceiverChannels.Light1));
        Assert.True(state.SelectedSurface.ReceivesLight(BasicLightReceiverChannels.Light2));
        state.SetSelectedSurfaceMode(BasicSurfaceLightingMode.Unlit);
        Assert.False(state.SelectedSurface.ReceivesAmbient);
        Assert.False(state.SelectedSurface.ReceivesLight(BasicLightReceiverChannels.Light2));
    }

    [Fact]
    public void StableAuthoredChannelsSurviveDisabledLights()
    {
        var state = new BasicLightingPreviewState();
        Assert.Single(state.BuildMaterialLights(View));
        Assert.Equal(BasicLightReceiverChannels.Light1, state.BuildMaterialLights(View)[0].Channel);
        state.SelectLight(1);
        state.SetLightEnabled(true);
        var both = state.BuildMaterialLights(View);
        Assert.Equal(2, both.Count);
        Assert.Equal(BasicLightReceiverChannels.Light1, both[0].Channel);
        Assert.Equal(BasicLightReceiverChannels.Light2, both[1].Channel);
        state.SelectLight(0);
        state.SetLightEnabled(false);
        Assert.Single(state.BuildMaterialLights(View));
        Assert.Equal("basics.2", state.BuildMaterialLights(View)[0].Light.Id);
        Assert.Equal(BasicLightReceiverChannels.Light2, state.BuildMaterialLights(View)[0].Channel);
    }

    [Fact]
    public void MaterialValidatesModeMaskOpacityAndLightChannel()
    {
        new BasicSurfaceMaterial(BasicSurfaceLightingMode.Lit, BasicLightReceiverChannels.None, 0).Validate();
        new BasicSurfaceMaterial(BasicSurfaceLightingMode.Unlit, BasicLightReceiverChannels.Both, 1).Validate();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BasicSurfaceMaterial((BasicSurfaceLightingMode)12, BasicLightReceiverChannels.Both, 1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BasicSurfaceMaterial(BasicSurfaceLightingMode.Lit, (BasicLightReceiverChannels)8, 1).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BasicSurfaceMaterial(BasicSurfaceLightingMode.Lit, BasicLightReceiverChannels.Both, double.NaN).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BasicSurfaceMaterial(BasicSurfaceLightingMode.Lit, BasicLightReceiverChannels.Both, 1.01).Validate());
        var state = new BasicLightingPreviewState();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SelectSurface(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SelectSurface(4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.SetSelectedSurfaceMode((BasicSurfaceLightingMode)45));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.SetSelectedSurfaceChannels((BasicLightReceiverChannels)8));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.SetSelectedSurfaceOpacity(double.PositiveInfinity));
        state.SetSelectedSurfaceOpacity(-9);
        Assert.Equal(0, state.SelectedSurface.Opacity);
        state.SetSelectedSurfaceOpacity(30);
        Assert.Equal(1, state.SelectedSurface.Opacity);
    }

    [Fact]
    public void GlobalResetRestoresLightsAndAllMaterials()
    {
        var state = new BasicLightingPreviewState();
        state.SelectSurface(3);
        state.SetSelectedSurfaceOpacity(.1);
        state.SelectLight(1);
        state.SetLightEnabled(true);
        state.SetAmbient(.9);
        state.Reset();
        Assert.Equal(0, state.SelectedSurfaceIndex);
        Assert.Equal(.55, state.GetSurface(3).Opacity);
        Assert.Single(state.BuildLights(View));
        Assert.Equal(BasicLightingPreviewState.DefaultAmbient, state.Ambient);
    }
}
