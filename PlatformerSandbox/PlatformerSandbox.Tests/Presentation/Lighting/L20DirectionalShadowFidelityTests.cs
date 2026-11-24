using System;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L20DirectionalShadowFidelityTests
{
    private static string Find(string relative)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 14 && dir is not null; i++)
        {
            string path = Path.Combine(dir, relative);
            if (File.Exists(path)) return path;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException("Missing directional shadow test fixture: " + relative);
    }

    [Fact]
    public void FiniteProjectionIncludesCasterHullAndStopsAtAuthoredReach()
    {
        var rectangle = new LightingOccluder
        {
            Id = "rectangle",
            Vertices = [new LightPoint(0, 0), new LightPoint(1, 0),
                new LightPoint(1, 2), new LightPoint(0, 2)]
        };
        var result = DirectionalShadowProjection.Triangles(
            rectangle, new LightPoint(1, 0), 3);
        Assert.Equal(30, result.Count); // 2 source face triangles + 8 edge triangles.
        Assert.Equal(0, result.Min(v => v.X), 6);
        Assert.Equal(4, result.Max(v => v.X), 6);
        Assert.Equal(0, result.Min(v => v.Y), 6);
        Assert.Equal(2, result.Max(v => v.Y), 6);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DirectionalShadowProjection.Triangles(rectangle, new LightPoint(1, 0), 31));
    }

    [Fact]
    public void HeroShadowingAgreesWithFiniteWorldSpaceProjection()
    {
        var blocker = new LightingOccluder
        {
            Id = "stone",
            Vertices = [new LightPoint(0, 0), new LightPoint(1, 0),
                new LightPoint(1, 2), new LightPoint(0, 2)]
        };
        var sun = new RadialLight
        {
            Id = "sun", Position = new LightPoint(-1000, 1),
            Shape = LightShape.Directional, Direction = new LightPoint(1, 0),
            DirectionalShadowLength = 3, Intensity = .5, CastShadows = true
        };
        var art = new LightingArtPreset
        {
            Id = "ShadowFidelity", Ambient = new LightRgb(.15, .15, .15)
        };
        LightRgb near = LightingWorldSampler.Sample(new LightPoint(2, 1),
            LightChannel.Hero, true, null, art, [sun], [blocker]);
        LightRgb beyond = LightingWorldSampler.Sample(new LightPoint(7, 1),
            LightChannel.Hero, true, null, art, [sun], [blocker]);
        Assert.True(beyond.R > near.R + .3);
    }

    [Fact]
    public void LabSunAndMoonAreReadableAndObeliskHasPointedHull()
    {
        string json = File.ReadAllText(Find(Path.Combine("Content", "Levels", "LightTypesLab.ldtk")));
        LdtkLightingScene scene = LdtkLightingImporter.FromJson(json, GameLevelIds.LightTypesLab);
        RadialLight sun = Assert.Single(scene.Lights.Where(l => l.Tag == "SUN"));
        RadialLight moon = Assert.Single(scene.Lights.Where(l => l.Tag == "MOON"));
        Assert.Equal(LightShape.Directional, sun.Shape);
        Assert.True(sun.Intensity < 1);
        Assert.True(moon.Intensity < 1);
        Assert.InRange(sun.DirectionalShadowLength, 2, 7);
        Assert.InRange(moon.DirectionalShadowLength, 2, 7);
        Assert.Contains(scene.Occluders, caster => caster.Vertices.Length == 5);
        foreach (var caster in scene.Occluders)
        {
            var polygon = DirectionalShadowProjection.Triangles(
                caster, sun.Direction, sun.DirectionalShadowLength);
            Assert.NotEmpty(polygon);
            Assert.Equal(0, polygon.Count % 3);
        }
    }
}
