using System;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class LBasicLightingPreviewTests
{
    [Fact]
    public void DefaultsAreAnUnshadowedSingleWhiteLightAndLowNeutralAmbient()
    {
        var state = new BasicLightingPreviewState();
        var viewport = new LightingViewport(1280, 720, new LightPoint(0, 0), 1);
        RadialLight light = Assert.IsType<RadialLight>(state.BuildLight(viewport));

        Assert.Equal(.15, state.Ambient, 10);
        Assert.Equal(new LightRgb(.15, .15, .15), state.AmbientRgb);
        Assert.Equal(LightRgb.White, light.Color);
        Assert.Equal(1, light.Intensity);
        Assert.False(light.CastShadows);
        Assert.Equal(BasicLightingDebugView.Final, state.DebugView);
        Assert.Equal(1280 * .4, viewport.Project(light.Position).X, 8);
        Assert.Equal(720 * .45, viewport.Project(light.Position).Y, 8);
    }

    [Fact]
    public void ZeroAmbientAndDisabledLightProvideZeroReceivedIllumination()
    {
        var state = new BasicLightingPreviewState();
        state.SetAmbient(0);
        state.ToggleLight();
        Assert.Equal(LightRgb.Black, state.AmbientRgb);
        Assert.Null(state.BuildLight(new LightingViewport(800, 600, new LightPoint(0, 0), 1)));
    }

    [Fact]
    public void LightUsesExistingQuadraticFalloffAtEveryViewportSize()
    {
        var state = new BasicLightingPreviewState();
        state.SetLightPositionNormalized(.25, .75);
        foreach ((int width, int height) in new[] { (640, 480), (1280, 720), (1920, 1080) })
        {
            var view = new LightingViewport(width, height, new LightPoint(0, 0), 1);
            RadialLight light = Assert.IsType<RadialLight>(state.BuildLight(view));
            LightPoint center = view.Project(light.Position);
            Assert.Equal(width * .25, center.X, 7);
            Assert.Equal(height * .75, center.Y, 7);
            Assert.Equal(1, RadialLighting.Attenuation(light, light.Position), 8);
            Assert.Equal(.25, RadialLighting.Attenuation(light,
                new LightPoint(light.Position.X + light.Radius / 2, light.Position.Y)), 8);
            Assert.Equal(0, RadialLighting.Attenuation(light,
                new LightPoint(light.Position.X + light.Radius, light.Position.Y)), 8);
        }
    }

    [Fact]
    public void AmbientBoundsAndDebugModesAreExplicitAndResettable()
    {
        var state = new BasicLightingPreviewState();
        state.SetAmbient(-.1);
        Assert.Equal(0, state.Ambient);
        state.SetAmbient(2);
        Assert.Equal(1, state.Ambient);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetAmbient(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetLightPositionNormalized(double.PositiveInfinity, 0));
        state.SetLightPositionNormalized(-5, 7);
        Assert.Equal(new LightPoint(0, 1), state.LightPositionNormalized);
        state.SetDebugView(BasicLightingDebugView.Illumination);
        Assert.Throws<ArgumentOutOfRangeException>(() => state.SetDebugView((BasicLightingDebugView)99));
        state.Reset();
        Assert.Equal(BasicLightingPreviewState.DefaultAmbient, state.Ambient);
        Assert.Equal(BasicLightingDebugView.Final, state.DebugView);
        Assert.True(state.LightEnabled);
    }
}
