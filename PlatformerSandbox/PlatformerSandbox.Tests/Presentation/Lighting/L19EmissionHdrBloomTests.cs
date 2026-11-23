using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L19EmissionHdrBloomTests
{
    private static string Fixture(string relative)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            string found = Path.Combine(dir, relative);
            if (File.Exists(found)) return found;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException("L19 fixture not found: " + relative);
    }

    [Fact]
    public void EmissionHasExplicitIdentityAndDoesNotRequireAnyLight()
    {
        var option = new LightingSurfaceOptions
        {
            MaterialShading = ProductionSurfaceShading.Normal,
            EmissionColor = new LightRgb(0.2, 0.9, 1),
            EmissionIntensity = 3.5,
            EmissionMap = "TerrainArt/LightingTest/L19PanelEmission.png"
        };
        option.Validate();
        ProductionSurfaceSubmission result = ProductionSurfaceSubmission.From("panel", option);
        Assert.Equal(3.5, result.EmissionIntensity);
        Assert.Equal(option.EmissionColor, result.EmissionColor);
        Assert.Equal(option.EmissionMap, result.EmissionMap);
        Assert.True(result.ReceivesShadows);
        Assert.Equal(0, ProductionMaterialLightPlan.Limit(ProductionMaterialLightMode.EmissionOnly));
        Assert.Empty(ProductionMaterialLightPlan.Batches(0));
        // Even with no selected lights, Phase 18's one base pass remains.
        Assert.Equal(1, 1 + ProductionMaterialLightPlan.Batches(0).Count);
    }

    [Fact]
    public void InvalidOrUnroutedEmissionIsRejected()
    {
        Assert.Throws<NotSupportedException>(() => new LightingSurfaceOptions
        {
            MaterialShading = ProductionSurfaceShading.Legacy,
            EmissionIntensity = 1
        }.Validate());
        Assert.ThrowsAny<ArgumentException>(() => new LightingSurfaceOptions
        {
            MaterialShading = ProductionSurfaceShading.Flat,
            EmissionIntensity = 8.01
        }.Validate());
        Assert.ThrowsAny<ArgumentException>(() => new ProductionSurfaceSubmission
        {
            InstanceId = "panel", Shading = ProductionSurfaceShading.Flat,
            EmissionMap = "../private.png"
        }.Validate());
        new LightingSurfaceOptions().Validate(); // All old levels remain non-emissive.
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.5, 0.4511883639059736)]
    [InlineData(1.0, 0.6988057880877978)]
    public void ToneMappingIsBoundedAndMatchesExponentialReference(double radiance, double expected)
    {
        var policy = new L19PostProcessPolicy(1.2, 1.05, 0.45);
        Assert.Equal(expected, policy.Tone(radiance), 10);
        Assert.InRange(policy.Tone(100), 0.9999, 1);
        Assert.True(policy.Tone(1.5) > policy.Tone(1));
    }

    [Fact]
    public void BloomSoftKneeAndCostAreBounded()
    {
        var settings = new L19PostProcessPolicy(1.1, 1, 0.45);
        Assert.Equal(0, settings.BloomRatio(0));
        Assert.Equal(0, settings.BloomRatio(0.3));
        Assert.True(settings.BloomRatio(0.9) > 0);
        Assert.True(settings.BloomRatio(2) > settings.BloomRatio(0.9));
        Assert.Equal(4, L19PostProcessPolicy.BloomGpuPassCount(true, true));
        Assert.Equal(0, L19PostProcessPolicy.BloomGpuPassCount(false, true));
        Assert.Equal(0, L19PostProcessPolicy.BloomGpuPassCount(true, false));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new L19PostProcessPolicy(double.NaN, 1, .5).Validate());
    }

    [Fact]
    public void ActualPhase18FixtureRetainsDynamicShadowAndMultiLightAcceptance()
    {
        string json = File.ReadAllText(Fixture(Path.Combine("Content", "Levels", "LightingIntegrationTest.ldtk")));
        LdtkLightingScene scene = LdtkLightingImporter.FromJson(json, "LIGHTING_INTEGRATION_TEST");
        Assert.Equal(11, scene.Lights.Count);
        Assert.Equal(3, scene.OccluderMotions.Count);
        var samples = scene.Surfaces.Values
            .Where(s => s.MaterialShading != ProductionSurfaceShading.Legacy).ToArray();
        Assert.Equal(2, samples.Length);
        Assert.All(samples, s =>
        {
            Assert.True(s.EmissionIntensity >= 3);
            Assert.NotNull(s.EmissionMap);
            string map = Fixture(Path.Combine("Content", "Levels", s.EmissionMap!.Replace('/', Path.DirectorySeparatorChar)));
            Assert.True(new FileInfo(map).Length > 50);
            Assert.Equal("PLAYER", s.LayerId);
        });
        Assert.Contains(samples, s => s.MaterialShading == ProductionSurfaceShading.Normal);
        Assert.Contains(samples, s => s.MaterialShading == ProductionSurfaceShading.Hybrid);
    }
}
