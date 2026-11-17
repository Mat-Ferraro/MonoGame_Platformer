using System;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>Regression tests for L01 light receivers and occluder hull composition.</summary>
public sealed class L123ShadowCompositionRegressionTests
{
    private static LightingOccluder Rectangle() => new()
    {
        Id = "wall",
        Vertices = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)]
    };

    private static double Area(LightTriangle t) =>
        Math.Abs((t.B.X - t.A.X) * (t.C.Y - t.A.Y) -
                 (t.B.Y - t.A.Y) * (t.C.X - t.A.X)) / 2.0;

    [Fact]
    public void ConvexOccluderTriangulatesIntoCompleteSolidSurface()
    {
        LightingOccluder hull = Rectangle();
        var triangles = ShadowGeometry.Triangulate(hull);
        Assert.Equal(2, triangles.Count);
        Assert.Equal(Math.Abs(ShadowGeometry.SignedDoubleArea(hull.Vertices)) / 2,
            triangles.Sum(Area), 10);
    }

    [Fact]
    public void ReversedWindingTriangulatesToSameCoveredArea()
    {
        LightingOccluder cw = Rectangle() with { Vertices = Rectangle().Vertices.Reverse().ToArray() };
        var triangles = ShadowGeometry.Triangulate(cw);
        Assert.Equal(2, triangles.Count);
        Assert.Equal(4.0, triangles.Sum(Area), 10);
    }

    [Fact]
    public void ConcaveOccluderFillsOnlyTheLShapeNotTheEmptyNotch()
    {
        var hull = new LightingOccluder
        {
            Id = "alcove",
            Vertices = [new(0, 0), new(3, 0), new(3, 1),
                new(1, 1), new(1, 3), new(0, 3)]
        };
        var triangles = ShadowGeometry.Triangulate(hull);
        Assert.Equal(hull.Vertices.Length - 2, triangles.Count);
        Assert.Equal(Math.Abs(ShadowGeometry.SignedDoubleArea(hull.Vertices)) / 2,
            triangles.Sum(Area), 10);
        Assert.False(triangles.Any(t => Inside(new LightPoint(2, 2), t)));
        Assert.True(triangles.Any(t => Inside(new LightPoint(.5, 2), t)));
    }

    [Fact]
    public void ShadowReceiverPolicyDoesNotStopDirectLightingOfForegroundArt()
    {
        LightingOccluder wall = Rectangle();
        var light = new RadialLight
        {
            Id = "torch", Position = new LightPoint(-4, 0), Radius = 12,
            Intensity = 1, Color = new LightRgb(1, .5, .1),
            CastShadows = true, Channels = LightChannel.Terrain
        };
        var receiver = new LightingMaterialDefinition
        {
            Id = "tile", Albedo = "Slate.png", Width = 2, Height = 2,
            ReceivesOccluderShadows = true
        };
        LightPoint sample = new(3, 0);
        LightRgb baseColor = new(.55, .55, .55);
        LightRgb ambient = new(.1, .1, .1);
        LightRgb behind = LightingMaterialSampler.Shade(receiver, baseColor,
            null, null, 1, sample, ambient, [light], [wall]);
        LightRgb inFront = LightingMaterialSampler.Shade(receiver with
        {
            ReceivesOccluderShadows = false
        }, baseColor, null, null, 1, sample, ambient, [light], [wall]);
        Assert.True(inFront.R > behind.R);
        Assert.True(inFront.G > behind.G);
        Assert.True(inFront.B > behind.B);
    }

    [Fact]
    public void OptingOutOfOccluderShadowsDoesNotMakeMaterialUnlit()
    {
        var light = new RadialLight
        {
            Id = "torch", Position = new LightPoint(0, 0), Radius = 10,
            Intensity = 1, CastShadows = true,
            Color = new LightRgb(.9, .5, .3)
        };
        var mat = new LightingMaterialDefinition
        {
            Id = "foreground", Albedo = "Slate.png",
            ReceivesOccluderShadows = false
        };
        LightRgb ambient = new(.1, .1, .1);
        LightRgb albedo = new(.5, .5, .5);
        LightRgb unlit = LightingMaterialSampler.Shade(mat, albedo, null, null,
            1, new LightPoint(0, 0), ambient, Array.Empty<RadialLight>());
        LightRgb directlyLit = LightingMaterialSampler.Shade(mat, albedo, null, null,
            1, new LightPoint(0, 0), ambient, [light]);
        Assert.True(directlyLit.R > unlit.R);
    }

    [Fact]
    public void AuthoredMaterialProxiesExplicitlyOptOutOfBackWallShadows()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        string? manifest = null;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "Content", "Lighting", "L123_LightingLab.json");
            if (File.Exists(candidate)) { manifest = candidate; break; }
            dir = dir.Parent;
        }
        Assert.NotNull(manifest);
        LightingCatalog catalog = LightingCatalog.FromFile(manifest);
        foreach (string scene in catalog.SceneIds)
        {
            LightingEnvironment environment = catalog.Get(scene);
            Assert.NotEmpty(environment.Occluders);
            Assert.NotEmpty(environment.Materials);
            Assert.All(environment.Materials, mat => Assert.False(mat.ReceivesOccluderShadows));
        }
    }

    [Fact]
    public void InvalidOccludersCannotGenerateWallOrShadowMeshes()
    {
        var crossed = Rectangle() with
        {
            Vertices = [new(0, 0), new(2, 2), new(0, 2), new(2, 0)]
        };
        Assert.Throws<ArgumentException>(() => ShadowGeometry.Triangulate(crossed));
    }

    private static bool Inside(LightPoint p, LightTriangle t)
    {
        static double Cross(LightPoint a, LightPoint b, LightPoint c) =>
            (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        double a = Cross(t.A, t.B, p);
        double b = Cross(t.B, t.C, p);
        double c = Cross(t.C, t.A, p);
        return (a >= -1e-10 && b >= -1e-10 && c >= -1e-10) ||
               (a <= 1e-10 && b <= 1e-10 && c <= 1e-10);
    }
}
