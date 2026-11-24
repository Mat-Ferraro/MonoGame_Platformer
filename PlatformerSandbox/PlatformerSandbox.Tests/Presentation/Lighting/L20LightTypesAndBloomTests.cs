using System;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L20LightTypesAndBloomTests
{
    private static string Find(string relative)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 14 && dir is not null; i++)
        {
            string candidate = Path.Combine(dir, relative);
            if (File.Exists(candidate)) return candidate;
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new FileNotFoundException("Missing Phase 20 fixture: " + relative);
    }

    [Fact]
    public void PointRemainsTheStableDefaultAndDirectionalIsGlobal()
    {
        RadialLight point = new() { Id = "p", Position = new LightPoint(1, 1), Radius = 4, Intensity = 2 };
        Assert.Equal(LightShape.Point, point.Shape);
        Assert.Equal(2, RadialLighting.Attenuation(point, point.Position), 6);
        Assert.Equal(0, RadialLighting.Attenuation(point, new LightPoint(20, 20)));
        RadialLight sun = point with { Shape = LightShape.Directional };
        Assert.Equal(2, RadialLighting.Attenuation(sun, new LightPoint(10000, -20000)), 6);
        Assert.True(new LightingViewport(1280, 720, new LightPoint(10000, 20000), 32)
            .InfluencesViewport(sun));
    }

    [Fact]
    public void SpotHasAnAngularConeWithSoftEdges()
    {
        RadialLight spot = new() { Id = "s", Shape = LightShape.Spot,
            Position = new LightPoint(0, 0), Direction = new LightPoint(1, 0),
            Radius = 12, Intensity = 3, ConeAngleDegrees = 60, ConeSoftness = .2 };
        double ahead = RadialLighting.Attenuation(spot, new LightPoint(4, 0));
        double reverse = RadialLighting.Attenuation(spot, new LightPoint(-4, 0));
        double side = RadialLighting.Attenuation(spot, new LightPoint(0, 4));
        Assert.True(ahead > 0.9);
        Assert.Equal(0, reverse);
        Assert.Equal(0, side);
    }

    [Fact]
    public void TubeUsesDistanceToSegmentAndBoundedAuthoredValues()
    {
        RadialLight tube = new() { Id = "t", Shape = LightShape.Tube,
            Position = new LightPoint(10, 10), Direction = new LightPoint(0, 1),
            Radius = 2, TubeLength = 8, Intensity = 2 };
        double along = RadialLighting.Attenuation(tube, new LightPoint(10, 13));
        double off = RadialLighting.Attenuation(tube, new LightPoint(13, 13));
        Assert.Equal(2, along, 6);
        Assert.Equal(0, off);
        Assert.Throws<ArgumentOutOfRangeException>(() => (tube with { TubeLength = 41 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (tube with { ConeAngleDegrees = 180 }).Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => (tube with { Direction = new LightPoint(0,0) }).Validate());
    }

    [Fact]
    public void LightTypesLabImportsAllFiveRoleTaggedSources()
    {
        string json = File.ReadAllText(Find(Path.Combine("Content", "Levels", "LightTypesLab.ldtk")));
        LdtkLightingScene scene = LdtkLightingImporter.FromJson(json, GameLevelIds.LightTypesLab);
        Assert.Equal(5, scene.Lights.Count);
        Assert.Single(scene.Lights.Where(l => l.Tag == "SUN" && l.Shape == LightShape.Directional));
        Assert.Single(scene.Lights.Where(l => l.Tag == "MOON" && l.Shape == LightShape.Directional));
        Assert.Single(scene.Lights.Where(l => l.Tag == "POINT" && l.Shape == LightShape.Point));
        Assert.Single(scene.Lights.Where(l => l.Tag == "SPOT" && l.Shape == LightShape.Spot));
        Assert.Single(scene.Lights.Where(l => l.Tag == "TUBE" && l.Shape == LightShape.Tube));
        Assert.True(scene.Occluders.Count >= 2);
        Assert.All(scene.Lights, light => light.Validate());
        Assert.Contains(GameLevelMenuCatalog.Items,
            item => item.Destination?.LevelId == GameLevelIds.LightTypesLab);
    }

    [Fact]
    public void MultiResolutionBloomCostsTenPassesWhenActive()
    {
        Assert.Equal(10, L19PostProcessPolicy.MultiResolutionBloomGpuPassCount(true, true));
        Assert.Equal(0, L19PostProcessPolicy.MultiResolutionBloomGpuPassCount(false, true));
        Assert.Equal(0, L19PostProcessPolicy.MultiResolutionBloomGpuPassCount(true, false));
        Assert.Equal(4, L19PostProcessPolicy.BloomGpuPassCount(true, true));
    }

    [Fact]
    public void OriginalShowcaseRemainsIndependentAndValid()
    {
        string json = File.ReadAllText(Find(Path.Combine("Content", "Levels", "MaterialShowcaseTest.ldtk")));
        LdtkLightingScene scene = LdtkLightingImporter.FromJson(json, GameLevelIds.MaterialShowcaseTest);
        Assert.Equal(3, scene.Lights.Count);
        Assert.All(scene.Lights, light => Assert.Equal(LightShape.Point, light.Shape));
    }

    [Fact]
    public void LightTypesFixtureRetainsAllAuthoredArtworkFiles()
    {
        string file = Find(Path.Combine("Content", "Levels", "LightTypesLab.ldtk"));
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(file));
        foreach (var layer in document.RootElement.GetProperty("levels")[0]
            .GetProperty("layerInstances").EnumerateArray())
        foreach (var entity in layer.GetProperty("entityInstances").EnumerateArray())
        foreach (var field in entity.GetProperty("fieldInstances").EnumerateArray())
        {
            string? id = field.GetProperty("__identifier").GetString();
            if (id is not ("Image" or "EmissionMap" or "NormalMap" or "EdgeMap")) continue;
            if (field.GetProperty("__value").ValueKind != System.Text.Json.JsonValueKind.String) continue;
            string? map = field.GetProperty("__value").GetString();
            if (string.IsNullOrWhiteSpace(map)) continue;
            Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(file)!,
                map.Replace('/', Path.DirectorySeparatorChar))), map);
        }
    }
}
