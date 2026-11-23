using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.Presentation;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>Headless regressions for Stage 16D dynamic wall geometry and 16E cache safety.</summary>
public sealed class L16DEProductionDynamicOccluderTests
{
    private const string Level = "LIGHTING_INTEGRATION_TEST";
    private static string Fixture(string name = "LightingIntegrationTest.ldtk")
    {
        string? path = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && path is not null; i++)
        {
            string found = Path.Combine(path, "Content", "Levels", name);
            if (File.Exists(found)) return File.ReadAllText(found);
            path = Directory.GetParent(path)?.FullName;
        }
        throw new FileNotFoundException(name);
    }
    private static LdtkLightingScene Scene() => LdtkLightingImporter.FromJson(Fixture(), Level);

    [Fact]
    public void ThreeProductionAnimatedCastersHaveValidGeometryAndOriginalLevelsStayStatic()
    {
        var scene = Scene();
        Assert.Equal(3, scene.OccluderMotions.Count);
        Assert.Contains(scene.OccluderMotions.Values, m =>
            m.Axis == PresentationOccluderMotionAxis.Horizontal &&
            m.Rotation == PresentationOccluderRotationMode.Swing);
        Assert.Contains(scene.OccluderMotions.Values, m =>
            m.Axis == PresentationOccluderMotionAxis.Vertical);
        Assert.Contains(scene.OccluderMotions.Values, m =>
            m.Rotation == PresentationOccluderRotationMode.Clockwise);
        Assert.All(scene.OccluderMotions.Keys, id =>
            Assert.Contains(scene.Occluders, o => o.Id == id));
        var sprites = LdtkTerrainArtworkImporter.FromJson(Fixture(), Level).Artworks;
        Assert.Contains(sprites, s => s.ImagePath.EndsWith("ShadowTriangle.png"));
        Assert.Contains(sprites, s => s.ImagePath.EndsWith("ShadowTrapezoid.png"));
        Assert.Empty(LdtkLightingImporter.FromJson(Fixture("LightingFlatTest.ldtk"),
            "LIGHTING_FLAT_TEST").OccluderMotions);
    }

    [Fact]
    public void AnimationOnlyRebuildsDynamicHullsAndSharesOneSnapshotPerFrame()
    {
        var scene = Scene();
        var presenter = new GameplayLightingPresenter();
        presenter.EnterWorld(17, scene);
        Assert.Equal(scene.Occluders.Count, presenter.CurrentScene()!.Occluders.Count);
        presenter.Update(.1, false);
        var frame = presenter.CurrentScene()!;
        Assert.Equal(3, presenter.LastDynamicHullsUpdated);
        Assert.Equal(scene.Occluders.Count - 3, presenter.LastStaticHullsReused);
        foreach (var original in scene.Occluders)
        {
            var transformed = Assert.Single(frame.Occluders.Where(o => o.Id == original.Id));
            if (scene.OccluderMotions.ContainsKey(original.Id))
                Assert.NotSame(original, transformed);
            else Assert.Same(original, transformed);
            transformed.Validate();
        }
        Assert.Contains(scene.OccluderMotions.Keys, id =>
            !scene.Occluders.First(o => o.Id == id).Vertices.SequenceEqual(
                frame.Occluders.First(o => o.Id == id).Vertices));
        Assert.Same(frame, presenter.CurrentScene());
        presenter.WallMotionEnabled = false;
        presenter.Update(.1, false);
        Assert.Same(frame, presenter.CurrentScene()); // explicit UI pause freezes walls only
        presenter.WallMotionEnabled = true;
        presenter.Update(.1, true);
        Assert.Same(frame, presenter.CurrentScene());
        presenter.EnterWorld(18, scene);
        var reset = presenter.CurrentScene()!;
        foreach (var original in scene.Occluders)
            for (int v = 0; v < original.Vertices.Length; v++)
            {
                var now = reset.Occluders.First(o => o.Id == original.Id).Vertices[v];
                Assert.Equal(original.Vertices[v].X, now.X, 10);
                Assert.Equal(original.Vertices[v].Y, now.Y, 10);
            }
    }

    [Fact]
    public void RotationUsesArtworkClockwiseDirectionAndSlopedPolygonsRemainValid()
    {
        var motion = new PresentationOccluderMotion(new(0, 0),
            PresentationOccluderMotionAxis.Static, 0, 0,
            PresentationOccluderRotationMode.Clockwise, 90, 0);
        var point = motion.At(1).Transform(new LightPoint(1, 0), motion.Anchor);
        Assert.Equal(0, point.X, 10);
        Assert.Equal(-1, point.Y, 10);
        var scene = Scene();
        foreach (double seconds in new[] { 0, .7, 3.2, 15.0 })
            foreach (var hull in scene.Occluders)
            {
                var transformed = scene.OccluderMotions.TryGetValue(hull.Id, out var m)
                    ? m.Apply(hull, seconds) : hull;
                Assert.Equal(hull.Vertices.Length, transformed.Vertices.Length);
                transformed.Validate();
            }
    }

    [Fact]
    public void ShadowPruningAndRayOcclusionUseCurrentHullNotAuthoredBounds()
    {
        var original = new LightingOccluder { Id = "moving", LayerId = "PLAYER",
            Vertices = [new(-.5,-.5),new(.5,-.5),new(.5,.5),new(-.5,.5)] };
        var motion = new PresentationOccluderMotion(new(0,0),
            PresentationOccluderMotionAxis.Horizontal, 3, .25,
            PresentationOccluderRotationMode.Static, 0, 0);
        var moved = motion.Apply(original, 1);
        Assert.True(ShadowGeometry.IsOccluded(new(0,0), new(1,0), [original]));
        Assert.False(ShadowGeometry.IsOccluded(new(0,0), new(1,0), [moved]));
        var light = new RadialLight { Id = "L", Owner = "test", Position = new(0,0),
            Radius = 1, Intensity = 1, CastShadows = true };
        Assert.Single(ShadowCasterPruning.Filter(light, [original]));
        Assert.Empty(ShadowCasterPruning.Filter(light, [moved]));
    }

    [Fact]
    public void BadAuthoredMotionIsRejected()
    {
        var document = JsonNode.Parse(Fixture())!;
        var layers = document["levels"]![0]!["layerInstances"]!.AsArray();
        var entities = layers.SelectMany(l =>
            l!["entityInstances"] is JsonArray a ? a : new JsonArray());
        var animated = entities.First(e => e!["__identifier"]!.GetValue<string>() ==
            "TerrainArtwork" && e["fieldInstances"]!.AsArray().Any(f =>
                f!["__identifier"]!.GetValue<string>() == "BlockerMotionAxis" &&
                f["__value"]!.GetValue<string>() == "Horizontal"))!;
        animated["fieldInstances"]!.AsArray().First(f =>
            f!["__identifier"]!.GetValue<string>() == "BlockerTravelWorld")!["__value"] = -1;
        Assert.ThrowsAny<ArgumentException>(() =>
            LdtkLightingImporter.FromJson(document.ToJsonString(), Level));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new PresentationOccluderMotion(new(0,0),
                PresentationOccluderMotionAxis.Vertical, 2, 0,
                PresentationOccluderRotationMode.Static, 0, 0).Validate());
    }
}
