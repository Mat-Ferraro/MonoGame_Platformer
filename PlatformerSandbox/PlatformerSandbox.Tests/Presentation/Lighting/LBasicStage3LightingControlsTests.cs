using System;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class LBasicStage3LightingControlsTests
{
    private static LightingViewport View =>
        new(1280, 720, new LightPoint(0, 0), 1);

    [Fact]
    public void TwoLightsHaveIndependentEnabledStateAndPosition()
    {
        var preview = new BasicLightingPreviewState();
        Assert.Equal(2, preview.LightCount);
        Assert.Single(preview.BuildLights(View));
        Assert.Equal("basics.1", preview.BuildLights(View)[0].Id);
        preview.SelectLight(1);
        Assert.False(preview.LightEnabled);
        preview.SetLightEnabled(true);
        preview.SetLightPositionNormalized(.72, .8);
        var both = preview.BuildLights(View);
        Assert.Equal(2, both.Count);
        Assert.Equal("basics.1", both[0].Id);
        Assert.Equal("basics.2", both[1].Id);
        Assert.Equal(1280 * .72, View.Project(both[1].Position).X, 7);
        Assert.Equal(720 * .8, View.Project(both[1].Position).Y, 7);
        preview.SelectLight(0);
        Assert.Equal(new LightPoint(.40, .45), preview.LightPositionNormalized);
        preview.ToggleLight();
        Assert.Single(preview.BuildLights(View));
        Assert.Equal("basics.2", preview.BuildLights(View)[0].Id);
    }

    [Theory]
    [InlineData(BasicLightColorPreset.Red, 1, 0, 0)]
    [InlineData(BasicLightColorPreset.Green, 0, 1, 0)]
    [InlineData(BasicLightColorPreset.Blue, 0, 0, 1)]
    [InlineData(BasicLightColorPreset.Magenta, 1, 0, 1)]
    [InlineData(BasicLightColorPreset.Cyan, 0, 1, 1)]
    public void ColorSelectionChangesLightRgbNotTheUnderlyingSurface(
        BasicLightColorPreset preset, double r, double g, double b)
    {
        var preview = new BasicLightingPreviewState();
        preview.SetLightColor(preset);
        var light = Assert.Single(preview.BuildLights(View));
        Assert.Equal(new LightRgb(r, g, b), light.Color);
        Assert.Equal(BasicLightingPreviewState.DefaultAmbient, preview.Ambient);
    }

    [Fact]
    public void IntensityAndRadiusAreClampedPerLightAndRestoredByReset()
    {
        var preview = new BasicLightingPreviewState();
        preview.SetLightIntensity(-3);
        preview.SetLightRadius(-7);
        Assert.Equal(0, Assert.Single(preview.BuildLights(View)).Intensity);
        Assert.Equal(BasicLightingPreviewState.MinimumRadius, preview.LightRadius);
        preview.SelectLight(1);
        preview.SetLightIntensity(20);
        preview.SetLightRadius(20);
        preview.SetLightEnabled(true);
        var second = preview.BuildLights(View).Single(l => l.Id == "basics.2");
        Assert.Equal(BasicLightingPreviewState.MaximumIntensity, second.Intensity);
        Assert.Equal(BasicLightingPreviewState.MaximumRadius * 720, second.Radius);
        Assert.Throws<ArgumentOutOfRangeException>(() => preview.SelectLight(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => preview.SetLightIntensity(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => preview.SetLightRadius(double.PositiveInfinity));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            preview.SetLightColor((BasicLightColorPreset)1234));
        preview.SetDebugView(BasicLightingDebugView.SelectedLight);
        preview.Reset();
        Assert.Single(preview.BuildLights(View));
        Assert.Equal(0, preview.SelectedLightIndex);
        Assert.Equal(BasicLightColorPreset.White, preview.LightColor);
        Assert.Equal(BasicLightingDebugView.Final, preview.DebugView);
    }

    [Fact]
    public void TwoColoredLightsAndNeutralAmbientStayIndependent()
    {
        var preview = new BasicLightingPreviewState();
        preview.SetAmbient(.08);
        preview.SetLightColor(BasicLightColorPreset.Red);
        preview.SelectLight(1);
        preview.SetLightColor(BasicLightColorPreset.Blue);
        preview.SetLightEnabled(true);
        var lights = preview.BuildLights(View);
        Assert.Equal(new LightRgb(1, 0, 0), lights[0].Color);
        Assert.Equal(new LightRgb(0, 0, 1), lights[1].Color);
        Assert.Equal(new LightRgb(.08, .08, .08), preview.AmbientRgb);
        Assert.All(lights, l => Assert.False(l.CastShadows));
    }
}
