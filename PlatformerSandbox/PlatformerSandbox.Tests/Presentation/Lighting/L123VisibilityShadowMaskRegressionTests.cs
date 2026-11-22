using System;
using System.Collections.Generic;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>
/// Shadow-mask correctness for point lights near convex and concave geometry.
/// These tests need neither MonoGame nor a GPU.
/// </summary>
public sealed class L123VisibilityShadowMaskRegressionTests
{
    private static RadialLight Lamp(double x, double y, double radius = 12) => new()
    {
        Id = "movable", Position = new(x, y), Radius = radius,
        Intensity = 1, Color = new(1, .65, .3), CastShadows = true
    };

    private static LightingOccluder Square() => new()
    {
        Id = "square", Vertices = [new(-1, -1), new(1, -1),
            new(1, 1), new(-1, 1)]
    };

    private static LightingOccluder Alcove() => new()
    {
        Id = "alcove", Vertices = [new(0, 0), new(3, 0),
            new(3, 1), new(1, 1), new(1, 3), new(0, 3)]
    };

    [Fact]
    public void ConvexBlockerShadowsFarSideWithoutErasingNearSide()
    {
        RadialLight lamp = Lamp(-4, 0);
        LightingOccluder[] walls = [Square()];
        Assert.True(ShadowVisibility.CanIlluminate(lamp, new(-2, 0), walls));
        Assert.False(ShadowVisibility.CanIlluminate(lamp, new(4, 0), walls));
        Assert.True(ShadowVisibility.CanIlluminate(lamp, new(-3, 3), walls));
        Assert.True(ShadowVisibility.CanIlluminate(lamp, new(0, 5), walls));
    }

    [Fact]
    public void ConcaveNotchRemainsLitWhenRayDoesNotCrossSolidGeometry()
    {
        LightingOccluder[] walls = [Alcove()];
        // Light is above and to the right of the open notch.
        RadialLight lamp = Lamp(4, 4);
        Assert.True(ShadowVisibility.CanIlluminate(lamp, new(2, 2), walls));
        Assert.False(ShadowVisibility.CanIlluminate(lamp, new(.5, .5), walls));
        Assert.True(ShadowVisibility.CanIlluminate(lamp, new(4, 1), walls));
    }

    [Fact]
    public void DynamicLightCrossingAroundCornerProducesFiniteOrderedPolygon()
    {
        LightingOccluder[] walls = [Alcove(), Square() with {
            Id = "offset", Vertices = [new(-4, -3), new(-3, -3),
                new(-3, -1), new(-4, -1)]
        }];
        foreach (LightPoint position in new[] { new LightPoint(-2, 2),
            new LightPoint(-.01, 1.01), new LightPoint(3.01, 1.01),
            new LightPoint(2.9, -1.2), new LightPoint(4, 4) })
        {
            var rays = ShadowVisibility.BuildPolygon(Lamp(position.X, position.Y), walls);
            Assert.InRange(rays.Count, 192, 192 + 768);
            Assert.All(rays, p => { p.Validate(); });
            Assert.All(rays, p => Assert.InRange(Distance(p, position), 0, 12 * 1.02 + .0001));
            double prev = double.NegativeInfinity;
            foreach (LightPoint point in rays)
            {
                double angle = Math.Atan2(point.Y - position.Y, point.X - position.X);
                if (angle < 0) angle += 2 * Math.PI;
                Assert.True(angle > prev - 1e-9, "Mask boundary must be polar sorted.");
                prev = angle;
            }
        }
    }

    [Fact]
    public void VisibilityFanMatchesDirectRayOcclusionAtClearInteriorSamples()
    {
        var walls = new[] { Alcove() };
        var lamp = Lamp(4, 4);
        var boundary = ShadowVisibility.BuildPolygon(lamp, walls);
        foreach (LightPoint point in new[] { new LightPoint(5, 4),
            new LightPoint(4, 2), new LightPoint(2, 2),
            new LightPoint(-1, .5), new LightPoint(.5, .5),
            new LightPoint(2, -2), new LightPoint(5, -2) })
        {
            bool inMask = InPolygon(point, boundary);
            bool hasLineOfSight = ShadowVisibility.CanIlluminate(lamp, point, walls);
            Assert.Equal(hasLineOfSight, inMask);
        }
    }

    [Fact]
    public void NoOccluderLeavesEntireLightCircleVisible()
    {
        var lamp = Lamp(1, 2, 7);
        var mask = ShadowVisibility.BuildPolygon(lamp, Array.Empty<LightingOccluder>());
        Assert.Equal(192, mask.Count);
        Assert.All(mask, p => Assert.Equal(7 * 1.02, Distance(p, lamp.Position), 8));
        Assert.True(InPolygon(new LightPoint(1, 2), mask));
    }

    [Fact]
    public void LightInsideSolidHullStopsAtExitInsteadOfLeakingThrough()
    {
        var wall = Square();
        var lamp = Lamp(0, 0);
        // Local illumination can reach an interior surface, but not the
        // background beyond the solid crate's exit edge.
        Assert.True(ShadowVisibility.CanIlluminate(lamp, new(.5, 0), [wall]));
        Assert.False(ShadowVisibility.CanIlluminate(lamp, new(2, 0), [wall]));
        Assert.False(InPolygon(new LightPoint(2, 0),
            ShadowVisibility.BuildPolygon(lamp, [wall])));
    }

    [Fact]
    public void LongEdgeWithEndpointsOutsideRadiusStillOccludes()
    {
        var wall = new LightingOccluder
        {
            Id = "long-wall",
            Vertices = [new(1, -40), new(2, -40),
                new(2, 40), new(1, 40)]
        };
        var lamp = Lamp(0, 0, 6);
        Assert.False(ShadowVisibility.CanIlluminate(lamp, new(4, 0), [wall]));
        Assert.True(ShadowVisibility.CanIlluminate(lamp, new(-4, 0), [wall]));
        Assert.False(InPolygon(new LightPoint(4, 0),
            ShadowVisibility.BuildPolygon(lamp, [wall])));
    }

    [Fact]
    public void RejectsInvalidSamplingLimitsAndNullInputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ShadowVisibility.BuildPolygon(Lamp(0, 0), [Square()], 5));
        Assert.Throws<ArgumentNullException>(() =>
            ShadowVisibility.BuildPolygon(Lamp(0, 0), null!));
        Assert.Throws<ArgumentException>(() =>
            ShadowVisibility.BuildPolygon(Lamp(0, 0), [null!]));
    }

    private static double Distance(LightPoint a, LightPoint b) =>
        Math.Sqrt(ShadowGeometry.DistanceSquared(a, b));

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
