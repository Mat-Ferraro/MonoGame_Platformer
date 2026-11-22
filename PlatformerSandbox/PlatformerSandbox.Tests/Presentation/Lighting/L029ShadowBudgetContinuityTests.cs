using System;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>Transient Pulse flashes must not steal authored light shadow masks.</summary>
public sealed class L029ShadowBudgetContinuityTests
{
    private static readonly LightingViewport View =
        new(1280, 720, new LightPoint(0, 0), 32);

    private static RadialLight Lamp(string id, string owner, int priority) => new()
    {
        Id = id,
        Owner = owner,
        Position = new LightPoint(0, 0),
        Color = LightRgb.White,
        Radius = 8,
        Intensity = 1,
        Priority = priority,
        CastShadows = true,
        Channels = LightChannel.All
    };

    [Fact]
    public void BalancedReservesAnAdditionalShadowMaskForDash()
    {
        Assert.Equal(0, LightingFrameBudget.For(LightingQuality.Low).MaxShadowLights);
        Assert.Equal(3, LightingFrameBudget.For(LightingQuality.Balanced).MaxShadowLights);
        Assert.Equal(5, LightingFrameBudget.For(LightingQuality.High).MaxShadowLights);
    }

    [Fact]
    public void DashDoesNotTurnAnAuthoredShadowLightIntoAnUnshadowedFloodlight()
    {
        RadialLight[] lights =
        [
            Lamp("warm", "level", 10), Lamp("blue", "level", 9),
            Lamp("pulse", "flash", 15)
        ];
        var selected = LightingFramePlanner.Select(lights, View,
            LightingFrameBudget.For(LightingQuality.Balanced), false);
        Assert.Equal(3, selected.Count);
        Assert.All(selected, x => Assert.True(x.CastShadows));
        Assert.Equal(2, selected.Count(x => x.Owner == "level"));
        Assert.Contains(selected, x => x.Id == "pulse" && x.CastShadows);
    }

    [Fact]
    public void CrateKeepsAllThreeLightContributionsShadowedDuringPulse()
    {
        RadialLight[] lights =
        [
            Lamp("warm", "level", 10), Lamp("blue", "level", 9),
            Lamp("pulse", "flash", 15)
        ];
        var selected = LightingFramePlanner.Select(lights, View,
            LightingFrameBudget.For(LightingQuality.Balanced), false);
        var blocker = new LightingOccluder
        {
            Id = "crate", LayerId = "PLAYER",
            Vertices =
            [
                new LightPoint(1, -1), new LightPoint(2, -1),
                new LightPoint(2, 1), new LightPoint(1, 1)
            ]
        };
        var art = new LightingArtPreset
        {
            Id = "test", Ambient = new LightRgb(.03, .03, .03)
        };
        var layer = SceneLightingLayerSet.Standard11().Gameplay;
        LightRgb behind = LightingWorldSampler.Sample(new LightPoint(3, 0),
            LightChannel.Terrain, true, null, art, selected, [blocker], layer);
        LightRgb exposed = LightingWorldSampler.Sample(new LightPoint(0, 1),
            LightChannel.Terrain, true, null, art, selected, [blocker], layer);
        Assert.Equal(.03, behind.R, 8);
        Assert.True(exposed.R > behind.R + .05);
    }

    [Fact]
    public void ExtraShadowDependentFlashesAreDroppedRatherThanLeakingThroughCrates()
    {
        var lights = new[] { Lamp("warm", "level", 10), Lamp("blue", "level", 9) }
            .Concat(Enumerable.Range(0, 5).Select(i => Lamp("flash_" + i, "flash", 50 - i)));
        var selected = LightingFramePlanner.Select(lights, View,
            LightingFrameBudget.For(LightingQuality.Balanced), false);
        Assert.Equal(2, selected.Count(x => x.Owner == "level" && x.CastShadows));
        Assert.Equal(1, selected.Count(x => x.Owner == "flash" && x.CastShadows));
        Assert.DoesNotContain(selected, x => x.Owner == "flash" && !x.CastShadows);
    }

    [Fact]
    public void HighPriorityFlashSaturationDoesNotCrowdAuthoredShadowLights()
    {
        var lights = new[] { Lamp("warm", "level", 10), Lamp("blue", "level", 9) }
            .Concat(Enumerable.Range(0, 20).Select(i => Lamp("flash_" + i, "flash", 100 - i)));
        var selected = LightingFramePlanner.Select(lights, View,
            LightingFrameBudget.For(LightingQuality.Balanced), false);
        Assert.Contains(selected, x => x.Id == "warm" && x.CastShadows);
        Assert.Contains(selected, x => x.Id == "blue" && x.CastShadows);
        Assert.DoesNotContain(selected, x => x.Owner == "flash" && !x.CastShadows);
        Assert.True(selected.Count <= LightingFrameBudget.For(LightingQuality.Balanced).MaxLights);
    }

    [Fact]
    public void LowQualityStillSupportsItsDeliberatelyShadowlessMode()
    {
        var selected = LightingFramePlanner.Select([Lamp("pulse", "flash", 15)], View,
            LightingFrameBudget.For(LightingQuality.Low), false);
        Assert.False(Assert.Single(selected).CastShadows);
    }
}
