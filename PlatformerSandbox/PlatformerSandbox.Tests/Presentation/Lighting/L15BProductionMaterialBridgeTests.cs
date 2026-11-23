using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>Stage 15B1 production opt-in policy and real LDtk field acceptance.
/// GPU output still requires a DesktopGL run.</summary>
public sealed class L15BProductionMaterialBridgeTests
{
    private const string SceneId = "LIGHTING_INTEGRATION_TEST";
    private static string Find(string relative)
    {
        string? folder = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && folder is not null; i++)
        {
            string file = Path.Combine(folder, relative);
            if (File.Exists(file)) return file;
            folder = Directory.GetParent(folder)?.FullName;
        }
        throw new FileNotFoundException(relative);
    }

    private static string Fixture() => File.ReadAllText(Find(Path.Combine(
        "Content", "Levels", "LightingIntegrationTest.ldtk")));

    [Fact]
    public void IntegrationSceneOptsInExactlyTwoShadowAwareArtworkSurfaces()
    {
        var scene = LdtkLightingImporter.FromJson(Fixture(), SceneId);
        var optIn = scene.Surfaces.Where(x =>
            x.Value.MaterialShading != ProductionSurfaceShading.Legacy).ToArray();
        Assert.Equal(2, optIn.Length);
        Assert.Contains(optIn, e => e.Value.MaterialShading == ProductionSurfaceShading.Normal);
        Assert.Contains(optIn, e => e.Value.MaterialShading == ProductionSurfaceShading.Hybrid);
        foreach (var entry in optIn)
        {
            LightingSurfaceOptions opts = entry.Value;
            Assert.True(opts.ReceivesShadows);
            Assert.Equal(opts.MaterialShading == ProductionSurfaceShading.Hybrid,
                opts.CastsShadows);
            Assert.True(opts.NeedsIndependentPass);
            Assert.Equal("PLAYER", opts.LayerId);
            ProductionSurfaceSubmission submission = ProductionSurfaceSubmission.From(entry.Key, opts);
            Assert.Equal(entry.Key, submission.InstanceId);
            Assert.False(string.IsNullOrEmpty(submission.NormalMap));
            Assert.Equal(opts.CastsShadows, submission.CasterId is not null);
            Assert.True(scene.SurfaceCenters.ContainsKey(entry.Key));
            Assert.True(File.Exists(Find(Path.Combine("Content", "Levels",
                submission.NormalMap!.Replace('/', Path.DirectorySeparatorChar)))));
            if (submission.EdgeMap is not null)
                Assert.True(File.Exists(Find(Path.Combine("Content", "Levels",
                    submission.EdgeMap.Replace('/', Path.DirectorySeparatorChar)))));
        }
    }

    [Fact]
    public void OriginalRegressionSceneDoesNotAccidentallyOptIn()
    {
        string original = File.ReadAllText(Find(Path.Combine(
            "Content", "Levels", "LightingFlatTest.ldtk")));
        var scene = LdtkLightingImporter.FromJson(original, "LIGHTING_FLAT_TEST");
        Assert.All(scene.Surfaces.Values,
            s => Assert.Equal(ProductionSurfaceShading.Legacy, s.MaterialShading));
    }

    [Fact]
    public void ProductionGpuMaterialCanReceiveAndCastActualLdtkShadows()
    {
        var scene = LdtkLightingImporter.FromJson(Fixture(), SceneId);
        var wall = scene.Surfaces.Single(x =>
            x.Value.MaterialShading == ProductionSurfaceShading.Hybrid);
        Assert.True(wall.Value.CastsShadows);
        Assert.True(wall.Value.ReceivesShadows);
        ProductionSurfaceSubmission submission = ProductionSurfaceSubmission.From(wall.Key, wall.Value);
        Assert.NotNull(ProductionWallLightRouting.OwnWall(submission, scene.Occluders));
    }

    [Fact]
    public void InvalidMapTraversalIsRejectedDuringImport()
    {
        var root = JsonNode.Parse(Fixture())!;
        var entry = root["levels"]![0]!["layerInstances"]!.AsArray()
            .SelectMany(layer => layer!["entityInstances"] is JsonArray entities
                ? entities : new JsonArray())
            .First(e => e!["iid"]!.GetValue<string>().StartsWith("da082cf3", StringComparison.Ordinal))!;
        entry["fieldInstances"]!.AsArray().First(f =>
            f!["__identifier"]!.GetValue<string>() == "NormalMap")!["__value"] = "../unsafe.png";
        Assert.Throws<ArgumentException>(() =>
            LdtkLightingImporter.FromJson(root.ToJsonString(), SceneId));
    }

    [Fact]
    public void TwoLightSelectionUsesAuthoritativeLayerRoutingAndStableRanking()
    {
        var layer = SceneLightingLayerSet.Standard11().Get("PLAYER");
        RadialLight Light(string id, double energy, double x, string[]? layers = null) =>
            new()
            {
                Id = id, Owner = "test", Position = new LightPoint(x, 0),
                Radius = 10, Intensity = energy, Color = LightRgb.White,
                Channels = LightChannel.All, AffectsLayers = layers
            };
        RadialLight[] lights =
        [
            Light("far", 4, 100), Light("a", 1, 0), Light("c", 2, 0),
            Light("b", 2, 0), Light("bg", 3, 0, ["BG1"])
        ];
        var chosen = ProductionSurfaceLightSelection.Select(lights, layer, new LightPoint(0, 0));
        Assert.Equal(new[] { "b", "c" }, chosen.Select(l => l.Id).ToArray());
        Assert.Equal(3, ProductionSurfaceLightSelection.CountEligible(
            lights, layer, new LightPoint(0, 0)));
        Assert.Empty(ProductionSurfaceLightSelection.Select(lights, layer,
            new LightPoint(0, 0), maximum: 0));
        // Art-preset radius scaling is applied *before* selecting GPU lights.
        RadialLight outskirts = Light("outskirts", 1, 12);
        Assert.Empty(ProductionSurfaceLightSelection.Select([outskirts], layer,
            new LightPoint(0, 0)));
        Assert.Single(ProductionSurfaceLightSelection.Select([outskirts], layer,
            new LightPoint(0, 0), radiusScale: 2));
    }

    [Fact]
    public void LegacyIsDefaultAndNormalMapIsOptional()
    {
        var legacy = new LightingSurfaceOptions();
        legacy.Validate();
        Assert.Equal(ProductionSurfaceShading.Legacy, legacy.MaterialShading);
        Assert.False(legacy.NeedsIndependentPass);
        var flat = new LightingSurfaceOptions
        {
            LayerId = "PLAYER", MaterialShading = ProductionSurfaceShading.Flat,
            ReceivesShadows = false
        };
        flat.Validate();
        Assert.True(flat.NeedsIndependentPass);
        Assert.Null(ProductionSurfaceSubmission.From("art1", flat).NormalMap);
    }
}
