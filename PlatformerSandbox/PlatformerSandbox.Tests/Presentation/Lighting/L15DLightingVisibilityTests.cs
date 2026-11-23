using System;
using System.Collections.Generic;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L15DLightingVisibilityTests
{
    private static readonly LightingViewport View =
        new(1280, 720, new LightPoint(0, 0), 64);
    private static RadialLight Red(double x = 0) => new()
    {
        Id = "warm_red", Owner = "level", Position = new LightPoint(x, 0),
        Radius = 3, Intensity = 1, Color = new LightRgb(1, .4, .3)
    };

    [Fact]
    public void ScreenActiveAndBudgetDroppedAreDifferent()
    {
        RadialLight light = Red();
        Assert.Equal(AuthoredLightVisibility.Active,
            LightingVisibilityDiagnostics.Classify(light, View,
                new[] { light }, enabled: true));
        Assert.Equal(AuthoredLightVisibility.BudgetCulled,
            LightingVisibilityDiagnostics.Classify(light, View,
                Array.Empty<RadialLight>(), enabled: true));
    }

    [Fact]
    public void OffscreenLightIsNotAnUnexpectedRenderingFailure()
    {
        RadialLight light = Red(150);
        Assert.Equal(AuthoredLightVisibility.OutsideView,
            LightingVisibilityDiagnostics.Classify(light, View,
                Array.Empty<RadialLight>(), enabled: true));
    }

    [Fact]
    public void DisabledLightingIsReportedEvenForOnScreenLight()
    {
        RadialLight light = Red();
        Assert.Equal(AuthoredLightVisibility.Disabled,
            LightingVisibilityDiagnostics.Classify(light, View,
                new[] { light }, enabled: false));
    }
}
