using System;
using System.Collections.Generic;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L16AShadowCasterPruningTests
{
    private static RadialLight Light(double x = 0, double y = 0,
        double radius = 3) => new()
    {
        Id = "fixture_light", Owner = "test", Position = new(x, y),
        Radius = radius, Intensity = 1, CastShadows = true
    };

    private static LightingOccluder Rect(string id, double x0, double y0,
        double x1, double y1) => new()
    {
        Id = id, LayerId = "PLAYER",
        Vertices = [new(x0, y0), new(x1, y0),
            new(x1, y1), new(x0, y1)]
    };

    [Fact]
    public void DistantCastersAreDiscardedInOriginalOrder()
    {
        var near0 = Rect("near0", 1, -1, 2, 1);
        var near1 = Rect("near1", -2, -1, -1, 1);
        var distant = Rect("far", 40, 40, 42, 42);
        var candidates = new[] { distant, near0, near1, distant };
        Assert.Equal(new[] { "near0", "near1" },
            ShadowCasterPruning.Filter(Light(), candidates).Select(x => x.Id));
        Assert.Equal(4, candidates.Length); // Does not mutate caller's collection.
    }

    [Fact]
    public void LongEdgeWithEndpointsBeyondRadiusIsNeverDiscarded()
    {
        // Endpoint-only pruning would incorrectly reject this wall.
        var crossing = Rect("crossing", -100, -.2, 100, .2);
        Assert.Single(ShadowCasterPruning.Filter(Light(), [crossing]));
        Assert.False(ShadowVisibility.CanIlluminate(Light(),
            new LightPoint(0, 2), [crossing]));
    }

    [Fact]
    public void ConservativeMarginPreservesRimOfShadowVisibilityDisk()
    {
        // Exact fan extends 2% beyond nominal falloff. Near-tangent blockers
        // inside that expansion must survive the broad phase.
        var nearBoundary = Rect("edge", 3.057, -.04, 3.1, .04);
        var outside = Rect("outside", 3.5, -.04, 3.6, .04);
        var result = ShadowCasterPruning.Filter(Light(), [nearBoundary, outside]);
        Assert.Equal("edge", Assert.Single(result).Id);
        Assert.Empty(ShadowCasterPruning.Filter(Light(), [outside]));
    }

    [Fact]
    public void OverlappingOriginAndChangingPositionsRespectCurrentGeometry()
    {
        var crossing = Rect("moving", -1, -1, 1, 1);
        var distant = Rect("moving", 20, 20, 22, 22);
        Assert.Single(ShadowCasterPruning.Filter(Light(), [crossing]));
        Assert.Empty(ShadowCasterPruning.Filter(Light(), [distant]));
        var translatedLight = Light(20, 20);
        Assert.Single(ShadowCasterPruning.Filter(translatedLight, [distant]));
    }

    [Fact]
    public void FilteredAndUnfilteredVisibilityFansMatchForMovingLights()
    {
        // Exact visibility engine internally applies an AABB broad phase.
        // Compare numerical fan output, not just whether sample endpoints
        // remain blocked, across static and moving positions and two radii.
        var casters = new List<LightingOccluder>
        {
            Rect("near-a", -1.25, -1.6, -.9, 1.4),
            Rect("near-b", 2.4, -.3, 3.4, .2),
            Rect("long", -25, -2, 25, -1.8),
            Rect("remote-x", 100, 40, 102, 42),
            Rect("remote-y", -70, -55, -67, -52),
            Rect("remote-z", 200, -120, 205, -115)
        };
        foreach (double radius in new[] { 1.5, 3.0, 5.0 })
            for (int step = 0; step < 12; step++)
            {
                RadialLight light = Light(-3 + step * .6, Math.Sin(step * .4), radius);
                IReadOnlyList<LightPoint> reference =
                    ShadowVisibility.BuildPolygon(light, casters, 48);
                IReadOnlyList<LightingOccluder> candidates =
                    ShadowCasterPruning.Filter(light, casters);
                IReadOnlyList<LightPoint> optimized =
                    ShadowVisibility.BuildPolygon(light, candidates, 48);
                Assert.Equal(reference.Count, optimized.Count);
                for (int i = 0; i < reference.Count; i++)
                {
                    Assert.Equal(reference[i].X, optimized[i].X, 10);
                    Assert.Equal(reference[i].Y, optimized[i].Y, 10);
                }
            }
    }

    [Fact]
    public void BadInputStillFailsFastInsteadOfBeingSilentlySkipped()
    {
        var bad = new LightingOccluder
        {
            Id = "invalid", LayerId = "PLAYER",
            Vertices = [new(100, 100), new(101, 100), new(102, 100)]
        };
        Assert.Throws<ArgumentException>(() =>
            ShadowCasterPruning.Filter(Light(), [bad]));
        Assert.Throws<ArgumentNullException>(() =>
            ShadowCasterPruning.Filter(Light(), null!));
    }
}
