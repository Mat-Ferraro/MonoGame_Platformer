using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L026DramaticLightEnergyTests
{
    private static string ReadLightingConfig()
    {
        string? directory = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && directory is not null; i++)
        {
            string path = Path.Combine(directory, "Content", "Lighting", "L456_GameplayLighting.json");
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new FileNotFoundException("Lighting configuration not found for L02.6 regression test.");
    }

    [Fact]
    public void GainAboveOneIsNotClampedBeforeTheRadialFalloff()
    {
        var tint = new LightRgb(1, .72, .24);
        const double gain = 4.24;
        var passes = LightingEnergyPasses.Split(tint, gain);
        Assert.Equal(5, passes.Count);
        Assert.All(passes, p =>
        {
            p.Validate();
            Assert.InRange(p.R, 0, 1);
        });
        Assert.Equal(tint.R * gain, passes.Sum(p => p.R), 10);
        Assert.Equal(tint.G * gain, passes.Sum(p => p.G), 10);
        Assert.Equal(tint.B * gain, passes.Sum(p => p.B), 10);
        // At half the light radius, quadratic falloff is 0.25. The old path
        // incorrectly delivered just 0.25 red even with a 4.24 gain.
        double redAtHalfRadius = Math.Min(1, passes.Sum(p => p.R) * .25);
        Assert.Equal(1, redAtHalfRadius, 10);
    }

    [Fact]
    public void SubUnitGainUsesOnePassAndZeroGainUsesNone()
    {
        Assert.Empty(LightingEnergyPasses.Split(LightRgb.White, 0));
        var passes = LightingEnergyPasses.Split(new LightRgb(.8, .5, .25), .5);
        Assert.Single(passes);
        Assert.Equal(.4, passes[0].R, 10);
        Assert.Equal(.25, passes[0].G, 10);
        Assert.Equal(.125, passes[0].B, 10);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    [InlineData(12.01)]
    public void InvalidGainsFailInsteadOfProducingUndefinedGpuColors(double gain)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LightingEnergyPasses.Split(LightRgb.White, gain));
    }

    [Fact]
    public void UnitPassesAgreeWithTheForwardWorldSamplerAtHalfRadius()
    {
        var light = new RadialLight
        {
            Id = "test", Position = new LightPoint(0, 0), Radius = 10,
            Intensity = 1.6, Color = new LightRgb(.95, .7, .4)
        };
        var art = new LightingArtPreset
        {
            Id = "test", Ambient = LightRgb.Black,
            LightMultiplier = 2.65, LightRadiusScale = 1
        };
        var sample = new LightPoint(5, 0);
        var forward = LightingWorldSampler.Sample(sample, LightChannel.Terrain,
            false, null, art, new[] { light }, Array.Empty<LightingOccluder>());
        var passes = LightingEnergyPasses.Split(light.Color, light.Intensity * art.LightMultiplier);
        Assert.Equal(forward.R, Math.Min(1, passes.Sum(p => p.R) * .25), 10);
        Assert.Equal(forward.G, Math.Min(1, passes.Sum(p => p.G) * .25), 10);
        Assert.Equal(forward.B, Math.Min(1, passes.Sum(p => p.B) * .25), 10);
    }

    [Fact]
    public void OccluderRemovesTheLocalContributionWithoutRemovingAmbient()
    {
        var art = new LightingArtPreset
        {
            Id = "test", Ambient = new LightRgb(.09, .1, .15),
            LightMultiplier = 2.65
        };
        var light = new RadialLight
        {
            Id = "test", Position = new LightPoint(0, 0), Radius = 10,
            Intensity = 1.6, Color = LightRgb.White, CastShadows = true
        };
        var occluder = new LightingOccluder
        {
            Id = "wall", Vertices = new[] {
                new LightPoint(2, -2), new LightPoint(3, -2),
                new LightPoint(3, 2), new LightPoint(2, 2)
            }
        };
        var blocked = LightingWorldSampler.Sample(new LightPoint(5, 0), LightChannel.Terrain,
            true, null, art, new[] { light }, new[] { occluder });
        var open = LightingWorldSampler.Sample(new LightPoint(0, 5), LightChannel.Terrain,
            true, null, art, new[] { light }, new[] { occluder });
        Assert.Equal(art.Ambient.R, blocked.R, 10);
        Assert.Equal(art.Ambient.G, blocked.G, 10);
        Assert.True(open.R > blocked.R + .6);
    }

    [Fact]
    public void ExposureBoostsBrightLitSurfacesButLeavesDarkAmbientLow()
    {
        var dramatic = new LightingArtPreset
        {
            Id = "dramatic", Ambient = new LightRgb(.09, .115, .16),
            LightMultiplier = 2.65, LightExposure = 2.3
        };
        var light = new RadialLight
        {
            Id = "warm", Position = new LightPoint(0, 0), Radius = 10,
            Intensity = 1.6, Color = new LightRgb(1, .8, .45)
        };
        LightRgb dark = LightingWorldSampler.Sample(new LightPoint(15, 0),
            LightChannel.Terrain, false, null, dramatic, new[] { light },
            Array.Empty<LightingOccluder>());
        LightRgb lit = LightingWorldSampler.Sample(new LightPoint(5, 0),
            LightChannel.Terrain, false, null, dramatic, new[] { light },
            Array.Empty<LightingOccluder>());
        Assert.True(dark.R < .25);
        Assert.True(lit.R > .95);
        Assert.True(lit.G > .80);
    }

    [Fact]
    public void InvalidExposuresAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LightingArtPreset { Id = "bad", LightExposure = .99 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LightingArtPreset { Id = "bad", LightExposure = 3.01 }.Validate());
        new LightingArtPreset { Id = "okay", LightExposure = 2.3 }.Validate();
    }

    [Fact]
    public void FlatDramaticRemainsDarkBetweenLampsButRaisesPeakLightEnergy()
    {
        var catalog = LightingGameCatalog.FromJson(ReadLightingConfig());
        var dramatic = catalog.GetPreset("FlatDramatic");
        var readable = catalog.GetPreset("FlatReadable");
        Assert.True(dramatic.LightMultiplier >= 2.5);
        Assert.True(dramatic.LightExposure >= 2.0);
        Assert.Equal(1, readable.LightExposure, 10);
        Assert.True(dramatic.LightRadiusScale < readable.LightRadiusScale);
        Assert.True(dramatic.Ambient.R < readable.Ambient.R * .3);
        Assert.Equal("FlatDramatic", catalog.GetDefaultPresetForScene("LIGHTING_FLAT_TEST"));
        Assert.Equal(new[] { "FlatDramatic", "FlatReadable" },
            catalog.GetPresetIdsForScene("LIGHTING_FLAT_TEST"));
    }
}
