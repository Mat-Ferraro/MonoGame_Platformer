using System;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>
/// Headless contract tests for the bounded 3-blocker Stage-7 lab.
/// GPU visibility and debug buffers still require visual device validation.
/// </summary>
public sealed class LBasicStage7MultipleBlockersTests
{
    private static readonly LightingViewport View =
        new(1280, 720, new LightPoint(0, 0), 1);

    [Fact]
    public void OriginalSingleBlockerBehaviorAndStageFiveDefaultsArePreserved()
    {
        var state = new BasicLightingPreviewState();
        Assert.Equal(3, BasicLightingPreviewState.ShadowBlockerCount);
        Assert.Equal(BasicLightingPreviewState.DefaultBlocker, state.ShadowBlocker);
        Assert.False(state.ShadowBlockerEnabled);
        Assert.Empty(state.ActiveShadowBlockers);
        Assert.Null(state.ActiveShadowBlocker);

        state.LoadShadowComparison();
        Assert.Single(state.ActiveShadowBlockers);
        Assert.True(state.ActiveShadowBlocker.HasValue);
        Assert.Equal(BasicLightingPreviewState.DefaultBlocker, state.ActiveShadowBlocker.Value);
        Assert.Equal(0, state.SelectedShadowBlockerIndex);
        Assert.True(state.SelectedShadowBlockerEnabled);
        Assert.False(state.GetShadowBlockerEnabled(1));
        Assert.False(state.GetShadowBlockerEnabled(2));
        Assert.Equal(2, state.BuildMaterialLights(View).Count);
    }

    [Fact]
    public void MultiBlockerPresetEnablesThreeIndependentShadowCasters()
    {
        var state = new BasicLightingPreviewState();
        state.LoadMultiShadowComparison();
        Assert.Equal(3, state.ActiveShadowBlockers.Count);
        for (int i = 0; i < 3; i++)
        {
            Assert.True(state.GetShadowBlockerEnabled(i));
            state.GetShadowBlocker(i).Validate();
        }
        Assert.Equal(1, state.SelectedShadowBlockerIndex);
        Assert.Equal(BasicLightingPreviewState.DefaultBlocker, state.GetShadowBlocker(0));
        Assert.Equal(BasicLightingPreviewState.DefaultBlocker2, state.GetShadowBlocker(1));
        Assert.Equal(BasicLightingPreviewState.DefaultBlocker3, state.GetShadowBlocker(2));
        Assert.All(state.BuildMaterialLights(View), light => Assert.True(light.Light.CastShadows));
    }

    [Fact]
    public void EditingSelectedBlockerNeverMutatesOtherSlots()
    {
        var state = new BasicLightingPreviewState();
        state.LoadMultiShadowComparison();
        var first = state.GetShadowBlocker(0);
        var third = state.GetShadowBlocker(2);
        state.SelectShadowBlocker(1);
        state.SetSelectedShadowBlockerPosition(.20, .25);
        state.SetSelectedShadowBlockerSize(.11, .22);
        Assert.Equal(.20, state.SelectedShadowBlocker.X, 10);
        Assert.Equal(.25, state.SelectedShadowBlocker.Y, 10);
        Assert.Equal(.11, state.SelectedShadowBlocker.Width, 10);
        Assert.Equal(.22, state.SelectedShadowBlocker.Height, 10);
        Assert.Equal(first, state.GetShadowBlocker(0));
        Assert.Equal(third, state.GetShadowBlocker(2));

        state.SetSelectedShadowBlockerEnabled(false);
        Assert.Equal(2, state.ActiveShadowBlockers.Count);
        Assert.True(state.GetShadowBlockerEnabled(0));
        Assert.True(state.GetShadowBlockerEnabled(2));
        Assert.False(state.GetShadowBlockerEnabled(1));
        state.SetSelectedShadowBlockerEnabled(true);
        Assert.Equal(3, state.ActiveShadowBlockers.Count);
    }

    [Fact]
    public void SecondOrThirdBlockerCanCastWithOriginalBlockerOff()
    {
        var state = new BasicLightingPreviewState();
        state.LoadMultiShadowComparison();
        state.SetShadowBlockerEnabled(false);
        Assert.Null(state.ActiveShadowBlocker); // old slot-1 compatibility accessor
        Assert.Equal(2, state.ActiveShadowBlockers.Count);
        Assert.Contains(BasicLightingPreviewState.DefaultBlocker2, state.ActiveShadowBlockers);
        Assert.Contains(BasicLightingPreviewState.DefaultBlocker3, state.ActiveShadowBlockers);
        Assert.All(state.BuildMaterialLights(View), light => Assert.True(light.Light.CastShadows));
    }

    [Fact]
    public void UnionOfShadowRectanglesIsPerLightAndAmbientIsNotMutated()
    {
        var state = new BasicLightingPreviewState();
        state.SetAmbient(.33);
        state.LoadMultiShadowComparison();
        state.SelectLight(0);
        LightPoint amber = state.LightPositionNormalized;
        state.SelectLight(1);
        LightPoint blue = state.LightPositionNormalized;
        LightPoint receiver = new(.684, .694);

        Assert.True(state.GetShadowBlocker(2).Blocks(amber, receiver));
        Assert.False(state.GetShadowBlocker(0).Blocks(amber, receiver));
        Assert.True(state.ActiveShadowBlockers.Any(r => r.Blocks(amber, receiver)));
        Assert.True(state.ActiveShadowBlockers.Any(r => r.Blocks(blue, receiver)));
        Assert.Equal(.33, state.Ambient, 10);
        state.SelectLight(0);
        state.SetSelectedLightCastsShadows(false);
        Assert.False(state.BuildMaterialLights(View)[0].Light.CastShadows);
        Assert.True(state.BuildMaterialLights(View)[1].Light.CastShadows);
    }

    [Fact]
    public void ResetAndSinglePresetDoNotLeaveHiddenExtraBlockersActive()
    {
        var state = new BasicLightingPreviewState();
        state.LoadMultiShadowComparison();
        state.SelectShadowBlocker(2);
        state.SetSelectedShadowBlockerPosition(.10, .10);
        state.SelectSurface(1);
        state.SetSelectedSurfaceOpacity(.24);
        state.SetSelectedSurfaceReceivesShadows(false);
        state.ResetShadows();
        Assert.Empty(state.ActiveShadowBlockers);
        Assert.Equal(BasicLightingPreviewState.DefaultBlocker3, state.GetShadowBlocker(2));
        Assert.Equal(0, state.SelectedShadowBlockerIndex);
        Assert.Equal(.24, state.GetSurface(1).Opacity);
        Assert.True(state.GetSurface(1).ReceivesShadows);
        state.LoadMultiShadowComparison();
        state.LoadShadowComparison();
        Assert.Single(state.ActiveShadowBlockers);
        state.Reset();
        Assert.Empty(state.ActiveShadowBlockers);
        Assert.Equal(BasicLightingDebugView.Final, state.DebugView);
        Assert.Equal(.55, state.GetSurface(3).Opacity);
    }

    [Fact]
    public void IndicesAndNonFiniteCoordinatesAreRejected()
    {
        var state = new BasicLightingPreviewState();
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SelectShadowBlocker(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SelectShadowBlocker(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.GetShadowBlocker(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.GetShadowBlockerEnabled(3));
        state.SelectShadowBlocker(2);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.SetSelectedShadowBlockerPosition(double.NaN, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            state.SetSelectedShadowBlockerSize(double.NegativeInfinity, .15));
        state.SetSelectedShadowBlockerPosition(42, 42);
        state.SetSelectedShadowBlockerSize(42, 42);
        state.SelectedShadowBlocker.Validate();
    }
}
