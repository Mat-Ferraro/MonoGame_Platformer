using System;
using System.Collections.Generic;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>
/// A shadow-casting sprite must not cease occluding light just because a
/// gameplay flash originates inside or overlaps its 2D footprint.
/// </summary>
public sealed class L030EmbeddedLightOcclusionTests
{
    private static LightingOccluder Box() => new()
    {
        Id = "box", LayerId = "PLAYER",
        Vertices = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)]
    };

    private static RadialLight Flash(double x, double y = 0) => new()
    {
        Id = "dash", Owner = "flash", Position = new(x, y),
        Color = new(.2, .75, 1), Radius = 5,
        Intensity = 1.2, CastShadows = true,
        RevealsSilhouettes = true,
        AffectsLayers = ["PLAYER"], OccluderLayers = ["PLAYER"]
    };

    [Theory]
    [InlineData(-1.5)]
    [InlineData(-.999)]
    [InlineData(-.5)]
    [InlineData(0)]
    [InlineData(.5)]
    public void CrateDoesNotDisappearFromShadowMaskWhenFlashOverlaps(double x)
    {
        RadialLight flash = Flash(x);
        LightingOccluder[] blockers = [Box()];
        Assert.False(ShadowVisibility.CanIlluminate(flash, new(2, 0), blockers));
        Assert.True(ShadowGeometry.IsOccluded(flash.Position, new(2, 0), blockers));
        var mask = ShadowVisibility.BuildPolygon(flash, blockers);
        Assert.True(mask.Count >= 128);
        Assert.All(mask, point => point.Validate());
        Assert.False(InPolygon(new(2, 0), mask));
    }

    [Fact]
    public void LightOutsideCrateStillLightsNearSideAndShadowsFarSide()
    {
        RadialLight flash = Flash(-2);
        LightingOccluder[] blockers = [Box()];
        Assert.True(ShadowVisibility.CanIlluminate(flash, new(-1.4, 0), blockers));
        Assert.False(ShadowVisibility.CanIlluminate(flash, new(2, 0), blockers));
    }

    [Fact]
    public void EmbeddedLightDoesNotBreakIndependentFrontFaceShading()
    {
        // Forward sprite rendering excludes its own occluder, so the front
        // surface can be revealed even while the wall behind remains blocked.
        var layer = SceneLightingLayerSet.Standard11().Gameplay;
        var art = new LightingArtPreset { Id = "test" };
        var flash = Flash(0);
        var surface = new LightingSurfaceOptions
        {
            LayerId = "PLAYER", LightingMode = LightingSurfaceMode.Silhouette,
            CastsShadows = true
        };
        LightRgb reveal = LightingWorldSampler.SampleSilhouetteReveal(new(0, 0),
            LightChannel.Terrain, true, "box", art, [flash], [Box()], layer);
        LightRgb tint = LightingSurfaceShading.ResolveTint(surface, layer,
            LightRgb.Black, reveal);
        Assert.True(tint.B > .13, "Pulse should still reveal the crate's visible face.");
        Assert.True(ShadowGeometry.IsOccluded(flash.Position, new(2, 0), [Box()]));
    }

    private static bool InPolygon(LightPoint p, IReadOnlyList<LightPoint> boundary)
    {
        bool inside = false;
        for (int i = 0, j = boundary.Count - 1; i < boundary.Count; j = i++)
        {
            LightPoint a = boundary[i], b = boundary[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }
}
