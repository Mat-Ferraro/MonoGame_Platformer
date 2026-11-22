using System;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class LBasicStage5NormalAndRimTests
{
    [Fact]
    public void StageFourDefaultsRemainFlatUntilComparisonIsRequested()
    {
        var state = new BasicLightingPreviewState();
        for (int i = 0; i < BasicLightingPreviewState.SurfaceCount; i++)
        {
            Assert.Equal(BasicSurfaceShadingMode.Flat, state.GetSurface(i).Shading);
            Assert.Equal(1, state.GetSurface(i).NormalStrength);
            Assert.Equal(1, state.GetSurface(i).RimStrength);
        }
        Assert.False(state.GetSurface(2).ReceivesAmbient);
    }

    [Fact]
    public void EachSurfaceCanChangeShadingWithoutChangingOtherSurfaceOrLightRouting()
    {
        var state = new BasicLightingPreviewState();
        state.SelectSurface(1);
        var original = state.SelectedSurface;
        state.SetSelectedSurfaceShading(BasicSurfaceShadingMode.Hybrid);
        state.SetSelectedSurfaceNormalStrength(.35);
        state.SetSelectedSurfaceRimStrength(1.4);
        Assert.Equal(BasicSurfaceShadingMode.Hybrid, state.GetSurface(1).Shading);
        Assert.Equal(.35, state.GetSurface(1).NormalStrength);
        Assert.Equal(1.4, state.GetSurface(1).RimStrength);
        Assert.Equal(original.Mode, state.GetSurface(1).Mode);
        Assert.Equal(original.Opacity, state.GetSurface(1).Opacity);
        Assert.Equal(original.Receives, state.GetSurface(1).Receives);
        Assert.Equal(BasicSurfaceShadingMode.Flat, state.GetSurface(0).Shading);
    }

    [Fact]
    public void UnlitAlwaysBypassesLightingRegardlessOfSurfaceShading()
    {
        var state = new BasicLightingPreviewState();
        state.SelectSurface(2);
        state.SetSelectedSurfaceShading(BasicSurfaceShadingMode.Hybrid);
        state.SetSelectedSurfaceNormalStrength(1);
        state.SetSelectedSurfaceRimStrength(2);
        Assert.False(state.SelectedSurface.ReceivesAmbient);
        Assert.False(state.SelectedSurface.ReceivesLight(BasicLightReceiverChannels.Light1));
        state.SetSelectedSurfaceChannels(BasicLightReceiverChannels.Both);
        Assert.False(state.SelectedSurface.ReceivesLight(BasicLightReceiverChannels.Light2));
    }

    [Fact]
    public void StrengthsClampButRejectNanAndInfinity()
    {
        var state = new BasicLightingPreviewState();
        state.SetSelectedSurfaceNormalStrength(-10);
        Assert.Equal(0, state.SelectedSurface.NormalStrength);
        state.SetSelectedSurfaceNormalStrength(50);
        Assert.Equal(1, state.SelectedSurface.NormalStrength);
        state.SetSelectedSurfaceRimStrength(-10);
        Assert.Equal(0, state.SelectedSurface.RimStrength);
        state.SetSelectedSurfaceRimStrength(50);
        Assert.Equal(2, state.SelectedSurface.RimStrength);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetSelectedSurfaceNormalStrength(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetSelectedSurfaceRimStrength(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetSelectedSurfaceShading((BasicSurfaceShadingMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (new BasicSurfaceMaterial(BasicSurfaceLightingMode.Lit, BasicLightReceiverChannels.Both, 1)
                with { Shading = (BasicSurfaceShadingMode)77 }).Validate());
    }

    [Fact]
    public void ComparisonPresetAndResetAreDeterministic()
    {
        var state = new BasicLightingPreviewState();
        state.LoadDirectionalComparison();
        Assert.Equal(BasicSurfaceShadingMode.Flat, state.GetSurface(0).Shading);
        Assert.Equal(BasicSurfaceShadingMode.Normal, state.GetSurface(1).Shading);
        Assert.Equal(BasicSurfaceShadingMode.Flat, state.GetSurface(2).Shading);
        Assert.Equal(BasicSurfaceShadingMode.Rim, state.GetSurface(3).Shading);
        Assert.Equal(.55, state.GetSurface(3).Opacity);
        Assert.Equal(BasicLightColorPreset.Amber, state.LightColor);
        state.SelectLight(1);
        Assert.True(state.LightEnabled);
        Assert.Equal(BasicLightColorPreset.Blue, state.LightColor);
        state.Reset();
        Assert.Equal(BasicLightColorPreset.White, state.LightColor);
        Assert.Equal(BasicSurfaceShadingMode.Flat, state.GetSurface(1).Shading);
        Assert.Equal(BasicSurfaceShadingMode.Flat, state.GetSurface(3).Shading);
    }
}
