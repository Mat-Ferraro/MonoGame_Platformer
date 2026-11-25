using System;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class PO04CpuLightingTests
{
    [Fact]
    public void ValidationDetectsMutationAfterSuccessfulValidation()
    {
        var hull = new LightingOccluder { Id = "wall", Vertices = new LightPoint[] { new(0,0), new(2,0), new(2,2), new(0,2) } };
        hull.Validate(); hull.Validate();
        hull.Vertices[1] = hull.Vertices[0];
        Assert.Throws<ArgumentException>(() => hull.Validate());
    }
    [Fact]
    public void RoutingStillRejectsDuplicateLayerIds()
    {
        var light = new RadialLight { Id = "light", Radius = 5, AffectsLayers = new[] { "PLAYER", "PLAYER" } };
        Assert.Throws<ArgumentException>(() => light.Validate());
    }
    [Fact]
    public void PreparedSamplesMatchReferenceForTubeAndPointLights()
    {
        var lights = new[] { new RadialLight { Id = "tube", Radius = 8, Shape = LightShape.Tube, Direction = new(1,0), TubeLength = 4, CastShadows = true },
            new RadialLight { Id = "point", Radius = 5, Position = new(2, 1) } };
        var art = new LightingArtPreset { Id = "test", LightRadiusScale = 1.5 };
        var blockers = new[] { new LightingOccluder { Id = "wall", Vertices = new LightPoint[] { new(1,-1),new(2,-1),new(2,1),new(1,1) } } };
        var prepared = LightingWorldSampler.Prepare(lights, art);
        for (int x = -5; x <= 5; x++)
        {
            var point = new LightPoint(x, 0);
            Assert.Equal(LightingWorldSampler.Sample(point, LightChannel.Hero, true, null, art, lights, blockers),
                LightingWorldSampler.Sample(point, LightChannel.Hero, true, null, art, lights, blockers, prepared: prepared));
        }
    }
    [Fact]
    public void PercentilesRefreshAfterAddAndClear()
    {
        var window = new LightingCpuTimeWindow(100);
        for (int i = 1; i <= 100; i++) window.Add(i);
        Assert.Equal(50d, window.P50); Assert.Equal(95d, window.P95); Assert.Equal(99d, window.P99);
        window.Add(101); Assert.Equal(51d, window.P50); Assert.Equal(100d, window.P99);
        window.Clear(); Assert.Equal(0d, window.P50); Assert.Equal(0d, window.P99);
    }
}
