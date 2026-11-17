using System;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L123LightingFoundationMaterialsShadowTests
{
    private static RadialLight Light(string id = "lamp", double x = 0, double y = 0,
        int priority = 0, string owner = "scene") => new()
    {
        Id = id, Position = new LightPoint(x, y), Radius = 4,
        Intensity = 1, Priority = priority, Owner = owner,
        Color = new LightRgb(1, .4, .2)
    };

    private static LightingMaterialDefinition Material(LightingMaterialMode mode = LightingMaterialMode.Lit) =>
        new() { Id = "slate", Mode = mode, Albedo = "Slate.png",
            Channel = LightChannel.Terrain, Width = 1, Height = 1 };

    private static LightingOccluder Blocker() => new()
    {
        Id = "blocker",
        Vertices = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)]
    };

    [Fact]
    public void ProjectionAndUnprojectionPreserveWorldCoordinatesAtTwoCameraScales()
    {
        foreach (double scale in new[] { 24.0, 80.0 })
        {
            var view = new LightingViewport(1280, 720, new LightPoint(3, -7), scale);
            LightPoint point = new(10.25, 1.75);
            LightPoint result = view.Unproject(view.Project(point));
            Assert.Equal(point.X, result.X, 10);
            Assert.Equal(point.Y, result.Y, 10);
            Assert.Equal(new LightPoint(640, 360), view.Project(view.Focus));
        }
    }

    [Fact]
    public void OffscreenCullingIncludesLightsTouchingViewportBorders()
    {
        var view = new LightingViewport(800, 600, new LightPoint(0, 0), 50);
        Assert.True(view.InfluencesViewport(Light(x: 11)));
        Assert.False(view.InfluencesViewport(Light(x: 13)));
        Assert.True(view.InfluencesViewport(Light(y: -9)));
        Assert.False(view.InfluencesViewport(Light(y: -11)));
    }

    [Fact]
    public void PrioritiesAreStableAndBudgeted()
    {
        var world = new LightWorld(capacity: 3);
        Assert.True(world.TryAdd(Light("z", priority: 1)));
        Assert.True(world.TryAdd(Light("c", priority: 5)));
        Assert.True(world.TryAdd(Light("b", priority: 5)));
        Assert.False(world.TryAdd(Light("overflow")));
        Assert.Equal(1, world.RejectedAtCapacity);
        var view = new LightingViewport(800, 600, new LightPoint(0, 0), 40);
        Assert.Equal(new[] { "b", "c" }, world.Visible(view, budget: 2).Select(x => x.Id));
    }

    [Fact]
    public void SceneOwnershipRemovesOnlyRequestedLights()
    {
        var world = new LightWorld();
        world.TryAdd(Light("scene0", owner: "scene-a"));
        world.TryAdd(Light("scene1", owner: "scene-a"));
        world.TryAdd(Light("other", owner: "scene-b"));
        Assert.Equal(2, world.RemoveOwner("scene-a"));
        Assert.Equal("other", Assert.Single(world.Lights).Id);
        world.Replace("other", Light("other", x: 9, owner: "scene-b"));
        Assert.Equal(9, Assert.Single(world.Lights).Position.X);
        world.Clear();
        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void DuplicateLightIdAndInvalidWorldCapacityFail()
    {
        var world = new LightWorld();
        world.TryAdd(Light());
        Assert.Throws<ArgumentException>(() => world.TryAdd(Light()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LightWorld(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Visible(
            new LightingViewport(800, 600, new LightPoint(0, 0), 50), 0));
    }

    [Fact]
    public void RadialFalloffIsSquaredAndZeroOutsideRadius()
    {
        RadialLight l = Light();
        Assert.Equal(1, RadialLighting.Attenuation(l, new LightPoint(0, 0)), 10);
        Assert.Equal(.25, RadialLighting.Attenuation(l, new LightPoint(2, 0)), 10);
        Assert.Equal(0, RadialLighting.Attenuation(l, new LightPoint(4, 0)));
        Assert.Equal(0, RadialLighting.Attenuation(l, new LightPoint(40, 0)));
    }

    [Fact]
    public void RejectsNonfiniteAndIllegalLightParameters()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Light() with { Radius = double.NaN }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Light() with { Intensity = -1 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Light() with { Color = new LightRgb(1.2, 0, 0) }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            (Light() with { Channels = (LightChannel)128 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new LightingViewport(0, 600, new LightPoint(0, 0), 55).Validate());
    }

    [Fact]
    public void NeutralNormalIsEquivalentToOmittingTheMap()
    {
        var material = Material();
        RadialLight[] lights = [Light(x: 1, y: 1)];
        LightRgb a = LightingMaterialSampler.Shade(material, new LightRgb(.4, .5, .6),
            null, null, 1, new LightPoint(0, 0), new LightRgb(.15, .2, .3), lights);
        LightRgb b = LightingMaterialSampler.Shade(material, new LightRgb(.4, .5, .6),
            LightingMaterialSampler.NeutralNormal, null, 1, new LightPoint(0, 0),
            new LightRgb(.15, .2, .3), lights);
        Assert.Equal(a, b);
    }

    [Fact]
    public void TangentSpaceMirroringAndRotationChangeTheNormalDirection()
    {
        LightRgb texel = new(1, .5, .5);
        var forward = LightingMaterialSampler.DecodeNormal(texel, 0, false);
        var mirrored = LightingMaterialSampler.DecodeNormal(texel, 0, true);
        var rotated = LightingMaterialSampler.DecodeNormal(texel, 90, false);
        Assert.Equal(1, forward.X, 10);
        Assert.Equal(-1, mirrored.X, 10);
        Assert.Equal(0, rotated.X, 10);
        Assert.Equal(1, rotated.Y, 10);
    }

    [Fact]
    public void UnlitMaterialIgnoresAmbientAndNearbyLights()
    {
        var material = Material(LightingMaterialMode.Unlit);
        LightRgb albedo = new(.35, .42, .5);
        LightRgb a = LightingMaterialSampler.Shade(material, albedo, null, null,
            1, new LightPoint(0, 0), LightRgb.Black, Array.Empty<RadialLight>());
        LightRgb b = LightingMaterialSampler.Shade(material, albedo, null, null,
            1, new LightPoint(0, 0), LightRgb.White, [Light()]);
        Assert.Equal(albedo, a);
        Assert.Equal(a, b);
    }

    [Fact]
    public void EmissionIsIndependentOfWhetherLightsExist()
    {
        var mat = Material();
        LightRgb result = LightingMaterialSampler.Shade(mat, LightRgb.Black,
            null, new LightRgb(.35, .2, 0), 0, new LightPoint(0, 0),
            LightRgb.Black, Array.Empty<RadialLight>());
        Assert.Equal(new LightRgb(.35, .2, 0), result);
    }

    [Fact]
    public void MaskAndLightingChannelsGateDynamicIllumination()
    {
        var material = Material();
        var light = Light() with { Channels = LightChannel.Hero };
        var point = new LightPoint(0, 0);
        LightRgb albedo = new(.7, .7, .7);
        LightRgb ambient = new(.1, .1, .1);
        LightRgb noChannel = LightingMaterialSampler.Shade(material, albedo, null, null,
            1, point, ambient, [light]);
        LightRgb maskOff = LightingMaterialSampler.Shade(material, albedo, null, null,
            0, point, ambient, [light with { Channels = LightChannel.Terrain }]);
        LightRgb maskOn = LightingMaterialSampler.Shade(material, albedo, null, null,
            1, point, ambient, [light with { Channels = LightChannel.Terrain }]);
        Assert.Equal(noChannel, maskOff);
        Assert.True(maskOn.R > maskOff.R);
    }

    [Fact]
    public void UnsafeMaterialAssetPathsAndInvalidSizeAreRejected()
    {
        Assert.Throws<ArgumentException>(() => (Material() with { Normal = "../../secret.png" }).Validate());
        Assert.Throws<ArgumentException>(() => (Material() with { Albedo = "C:/bad.png" }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (Material() with { Width = -2 }).Validate());
    }

    [Fact]
    public void ValidPolygonCastsFacingEdgeShadow()
    {
        var light = Light(x: -4) with { Radius = 10 };
        LightingOccluder obstacle = Blocker();
        obstacle.Validate();
        var quads = ShadowGeometry.Project(light, obstacle);
        Assert.NotEmpty(quads);
        Assert.All(quads, quad =>
        {
            quad.A.Validate(); quad.B.Validate(); quad.FarA.Validate(); quad.FarB.Validate();
            Assert.True(ShadowGeometry.DistanceSquared(quad.FarA, light.Position) >
                ShadowGeometry.DistanceSquared(quad.A, light.Position));
        });
    }

    [Fact]
    public void LightInsidePolygonUsesSafeNoProjectionFallback()
    {
        var quads = ShadowGeometry.Project(Light(), Blocker());
        Assert.Empty(quads);
    }

    [Fact]
    public void WorldRayBehindBlockerIsOccludedButNearSideIsNot()
    {
        LightPoint lamp = new(-4, 0);
        LightingOccluder[] hulls = [Blocker()];
        Assert.True(ShadowGeometry.IsOccluded(lamp, new LightPoint(3, 0), hulls));
        Assert.False(ShadowGeometry.IsOccluded(lamp, new LightPoint(-3, 0), hulls));
    }

    [Fact]
    public void ConcaveHullCanBeImportedAndProjected()
    {
        var concave = new LightingOccluder
        {
            Id = "l_shape", Vertices = [new(0, 0), new(3, 0), new(3, 1),
                new(1, 1), new(1, 3), new(0, 3)]
        };
        concave.Validate();
        Assert.NotEmpty(ShadowGeometry.Project(Light(x: -5) with { Radius = 12 }, concave));
    }

    [Fact]
    public void DegenerateAndCrossedOccludersFailValidation()
    {
        Assert.Throws<ArgumentException>(() => (Blocker() with
        {
            Vertices = [new(0, 0), new(1, 0), new(2, 0)]
        }).Validate());
        Assert.Throws<ArgumentException>(() => (Blocker() with
        {
            Vertices = [new(0, 0), new(3, 2), new(0, 3), new(2, 0)]
        }).Validate());
    }

    [Fact]
    public void InvalidLightingManifestsAndDuplicatesFail()
    {
        Assert.Throws<ArgumentException>(() => LightingCatalog.FromJson("{\"version\":2,\"scenes\":[]}"));
        Assert.Throws<ArgumentException>(() => LightingCatalog.FromJson(""));
        Assert.Throws<ArgumentException>(() => LightingCatalog.FromJson(
            "{\"version\":1,\"scenes\":[{\"id\":\"A\"},{\"id\":\"A\"}]}"));
        Assert.Throws<ArgumentException>(() => LightingCatalog.FromJson(
            "{\"version\":1,\"scenes\":[{\"id\":\"A\",\"lights\":[{\"id\":\"dup\"},{\"id\":\"dup\"}]}]}"));
    }

    [Fact]
    public void AuthoredTwoSceneManifestAndMaterialAssetsAreWellFormed()
    {
        // Validate ACTUAL packaged authoring data; no MonoGame graphics device needed.
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? manifest = null;
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "Content", "Lighting", "L123_LightingLab.json");
            if (File.Exists(candidate)) { manifest = candidate; break; }
            dir = dir.Parent;
        }
        Assert.NotNull(manifest);
        LightingCatalog catalog = LightingCatalog.FromFile(manifest);
        Assert.Equal(2, catalog.SceneIds.Count);
        Assert.True(catalog.Get("CAVERN").Lights.Length >= 2);
        Assert.Contains(catalog.Get("ATRIUM").Occluders, x => x.Vertices.Length > 4);
        string textureDir = Path.Combine(Path.GetDirectoryName(manifest)!, "Textures");
        foreach (LightingEnvironment scene in new[] { catalog.Get("CAVERN"), catalog.Get("ATRIUM") })
            foreach (LightingMaterialDefinition mat in scene.Materials)
                foreach (string? file in new[] { mat.Albedo, mat.Normal, mat.Mask, mat.Emission })
                {
                    if (file is null) continue;
                    string full = Path.Combine(textureDir, file);
                    Assert.True(File.Exists(full), "Missing material map: " + full);
                    byte[] header = File.ReadAllBytes(full);
                    Assert.True(header.Length > 24 && header[0] == 137 &&
                        header[1] == 80 && header[2] == 78 && header[3] == 71,
                        "Material is not a PNG: " + full);
                }
    }

    [Fact]
    public void LongRunningCosmeticLightUpdatesStayFiniteAndBounded()
    {
        var world = new LightWorld(capacity: 20);
        for (int i = 0; i < 20; i++)
            Assert.True(world.TryAdd(Light("l" + i, i - 10, i % 7)));
        var view = new LightingViewport(1280, 720, new LightPoint(0, 0), 50);
        for (int tick = 0; tick < 2000; tick++)
        {
            RadialLight candidate = world.Lights[tick % 20];
            double x = Math.Sin(tick / 60.0) * 10;
            world.Replace(candidate.Id, candidate with { Position = new LightPoint(x, tick % 7) });
            var visible = world.Visible(view, 12);
            Assert.InRange(visible.Count, 0, 12);
            foreach (var light in visible)
                Assert.True(double.IsFinite(RadialLighting.Attenuation(light, new LightPoint(0, 0))));
        }
        Assert.Equal(20, world.Count);
        world.Clear();
        Assert.Empty(world.Lights);
    }
}
