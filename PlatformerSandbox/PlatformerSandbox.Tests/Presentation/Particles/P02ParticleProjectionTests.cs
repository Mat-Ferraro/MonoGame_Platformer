using System;
using System.Linq;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation.Particles;

namespace PlatformerSandbox.Tests.Presentation.Particles;

public sealed class P02ParticleProjectionTests
{
    [Fact]
    public void WorldYAxisIsInvertedWhileUiPixelsAreNot()
    {
        var viewport = new ParticleViewport(800, 600, new Vector2D(10, 20), 40);
        Assert.Equal(new ParticleScreenPoint(400, 300),
            viewport.ToScreen(new Vector2D(10, 20), ParticleCoordinateSpace.World));
        Assert.Equal(new ParticleScreenPoint(480, 220),
            viewport.ToScreen(new Vector2D(12, 22), ParticleCoordinateSpace.World));
        Assert.Equal(new ParticleScreenPoint(12, 22),
            viewport.ToScreen(new Vector2D(12, 22), ParticleCoordinateSpace.Screen));
    }

    [Fact]
    public void WorldProjectionRoundTripsScreenCoordinates()
    {
        var viewport = new ParticleViewport(1280, 720, new Vector2D(-13, 4), 37.5);
        var world = new Vector2D(-10.25, 6.5);
        ParticleScreenPoint screen = viewport.ToScreen(world, ParticleCoordinateSpace.World);
        Vector2D result = viewport.ToWorld(screen.X, screen.Y);
        Assert.Equal(world.X, result.X, 10);
        Assert.Equal(world.Y, result.Y, 10);
    }

    [Fact]
    public void PanningMovesWorldButNotScreenSpace()
    {
        var a = new ParticleViewport(800, 600, new Vector2D(0, 0), 50);
        var b = a with { CameraCenter = new Vector2D(3, 2) };
        var world = new Vector2D(5, 7);
        ParticleScreenPoint wa = a.ToScreen(world, ParticleCoordinateSpace.World);
        ParticleScreenPoint wb = b.ToScreen(world, ParticleCoordinateSpace.World);
        Assert.Equal(-150, wb.X - wa.X);
        Assert.Equal(100, wb.Y - wa.Y);
        Assert.Equal(a.ToScreen(world, ParticleCoordinateSpace.Screen),
            b.ToScreen(world, ParticleCoordinateSpace.Screen));
    }

    [Fact]
    public void ZoomChangesWorldApparentSizeButNotUiPixels()
    {
        var a = new ParticleViewport(800, 600, new Vector2D(0, 0), 25);
        var b = a with { PixelsPerWorldUnit = 75 };
        Assert.Equal(10, a.SizePixels(.4, ParticleCoordinateSpace.World), 10);
        Assert.Equal(30, b.SizePixels(.4, ParticleCoordinateSpace.World), 10);
        Assert.Equal(16, a.SizePixels(16, ParticleCoordinateSpace.Screen));
        Assert.Equal(16, b.SizePixels(16, ParticleCoordinateSpace.Screen));
    }

    [Fact]
    public void VisibilityIncludesParticlesTouchingViewportEdges()
    {
        var viewport = new ParticleViewport(500, 300, new Vector2D(0, 0), 50);
        Assert.True(viewport.IsVisible(new ParticleScreenPoint(-4, 150), 8));
        Assert.True(viewport.IsVisible(new ParticleScreenPoint(504, 150), 8));
        Assert.False(viewport.IsVisible(new ParticleScreenPoint(-6, 150), 8));
        Assert.False(viewport.IsVisible(new ParticleScreenPoint(506, 150), 8));
        Assert.False(viewport.IsVisible(new ParticleScreenPoint(100, -20), 8));
        Assert.False(viewport.IsVisible(new ParticleScreenPoint(double.NaN, 150), 8));
    }

    [Fact]
    public void InvalidViewportAndInvalidSpaceFailStrictly()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ParticleViewport(0, 600, new Vector2D(0, 0), 40).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ParticleViewport(800, 600, new Vector2D(0, 0), double.NaN).Validate());
        var viewport = new ParticleViewport(800, 600, new Vector2D(0, 0), 40);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            viewport.ToScreen(new Vector2D(0, 0), (ParticleCoordinateSpace)100));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            viewport.SizePixels(-1, ParticleCoordinateSpace.World));
    }

    [Fact]
    public void RenderOrderingHasTwoPassesPerLayerAndUiIsLast()
    {
        int[] keys =
        [
            ParticleRenderOrdering.PassKey(ParticleVisualLayer.Background, ParticleBlendMode.Alpha),
            ParticleRenderOrdering.PassKey(ParticleVisualLayer.Background, ParticleBlendMode.Additive),
            ParticleRenderOrdering.PassKey(ParticleVisualLayer.World, ParticleBlendMode.Alpha),
            ParticleRenderOrdering.PassKey(ParticleVisualLayer.World, ParticleBlendMode.Additive),
            ParticleRenderOrdering.PassKey(ParticleVisualLayer.Foreground, ParticleBlendMode.Alpha),
            ParticleRenderOrdering.PassKey(ParticleVisualLayer.Foreground, ParticleBlendMode.Additive),
            ParticleRenderOrdering.PassKey(ParticleVisualLayer.Ui, ParticleBlendMode.Alpha),
            ParticleRenderOrdering.PassKey(ParticleVisualLayer.Ui, ParticleBlendMode.Additive)
        ];
        Assert.Equal(Enumerable.Range(0, 8), keys);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ParticleRenderOrdering.PassKey((ParticleVisualLayer)99, ParticleBlendMode.Alpha));
    }

    [Fact]
    public void PremultipliedColorRespectOpacityAndTransparentEndpoint()
    {
        var tint = ParticlePremultipliedRgba.From(new ParticleColor(1, .5, 0, .5));
        Assert.Equal((byte)128, tint.R);
        Assert.Equal((byte)64, tint.G);
        Assert.Equal((byte)0, tint.B);
        Assert.Equal((byte)128, tint.A);
        Assert.Equal(new ParticlePremultipliedRgba(0, 0, 0, 0),
            ParticlePremultipliedRgba.From(new ParticleColor(1, 1, 1, 0)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ParticlePremultipliedRgba.From(new ParticleColor(1.01, 0, 0, 1)));
    }

    [Fact]
    public void PlaygroundDefinitionsLoadAndSpawnWithinPoolCap()
    {
        // Verify the five playground configurations without requiring any
        // GPU or MonoGame dependency in PlatformerSandbox.Tests.
        string json = """
        {"version":1,"emitters":[
         {"id":"lab.test","burstCount":20,"maxSpawnsPerUpdate":20,
          "shape":"Point","lifetimeSeconds":{"min":0.5,"max":1.0},
          "speed":{"min":0,"max":2},"startSize":{"min":0.1,"max":0.2},
          "endSize":{"min":0,"max":0.1}}
        ]}
        """;
        ParticleEmitterDefinition def = ParticleEmitterLibrary.FromJson(json).Get("lab.test");
        var pool = new ParticleSystem(capacity: 12);
        var emitter = new ParticleEmitter(pool, def, seed: 84);
        Assert.Equal(12, emitter.Burst(new Vector2D(0, 0)));
        Assert.Equal(12, pool.ActiveCount);
        Assert.Equal(8, pool.Diagnostics.DroppedAtCapacity);
    }
}
