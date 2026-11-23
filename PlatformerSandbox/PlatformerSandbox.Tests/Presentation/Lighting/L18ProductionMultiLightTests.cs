using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>Phase 18 production multi-light acceptance. GPU appearance is
/// additionally checked with the in-game ALL / COMPARE 2 developer toggle.</summary>
public sealed class L18ProductionMultiLightTests
{
    private static RadialLight Light(string id, double x = 0, double y = 0,
        double z = 0, string[]? layers = null) => new()
    {
        Id = id, Owner = "test", Position = new(x, y), Radius = 10,
        Intensity = 0.6, Color = LightRgb.White, VirtualZ = z,
        CastShadows = true, AffectsLayers = layers
    };

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(5, 3)]
    [InlineData(11, 6)]
    [InlineData(12, 6)]
    public void BatchPlanCoversEachLightOnceAndAmbientRemainsSeparate(int count, int passes)
    {
        var batches = ProductionMaterialLightPlan.Batches(count);
        Assert.Equal(passes, batches.Count);
        Assert.Equal(count, batches.Sum(b => b.Count));
        var visited = new List<int>();
        foreach (var batch in batches)
        {
            batch.Validate();
            for (int i = 0; i < batch.Count; i++) visited.Add(batch.Start + i);
        }
        Assert.Equal(Enumerable.Range(0, count), visited);
        // One ambient pass is required whether there are zero or twelve lights.
        Assert.Equal(1 + passes, 1 + batches.Count);
    }

    [Fact]
    public void ComparisonModeAndGlobalHighQualityBoundAreExplicit()
    {
        Assert.Equal(12, ProductionMaterialLightPlan.Limit(ProductionMaterialLightMode.AllEligible));
        Assert.Equal(2, ProductionMaterialLightPlan.Limit(ProductionMaterialLightMode.CompareTwo));
        Assert.Equal(LightingFrameBudget.For(LightingQuality.High).MaxLights,
            ProductionMaterialLightPlan.MaximumLights);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProductionMaterialLightPlan.Batches(13));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ProductionMaterialLightPlan.Limit((ProductionMaterialLightMode)123));
    }

    [Fact]
    public void FiveRelevantLightsAreAllSelectedInDeterministicOrder()
    {
        var layer = SceneLightingLayerSet.Standard11().Get("PLAYER");
        var lights = new[]
        {
            Light("c"), Light("e"), Light("a"), Light("d"), Light("b"),
            Light("remote", 40), Light("wrong-layer", layers: ["BG1"])
        };
        Assert.Equal(new[] { "a", "b", "c", "d", "e" },
            ProductionSurfaceLightSelection.Select(lights, layer, new(0, 0), maximum: 12)
                .Select(l => l.Id));
        Assert.Equal(new[] { "a", "b" },
            ProductionSurfaceLightSelection.Select(lights, layer, new(0, 0))
                .Select(l => l.Id)); // Legacy lab selection contract unchanged.
        Assert.Equal(5, ProductionSurfaceLightSelection.CountEligible(lights, layer, new(0, 0)));
    }

    [Fact]
    public void EachBatchMaintainsIndependentShadowAndDepthRouting()
    {
        var layer = SceneLightingLayerSet.Standard11().Get("PLAYER");
        var wall = new LightingOccluder
        {
            Id = "own", LayerId = "PLAYER",
            Vertices = [new(2, -1), new(4, -1), new(4, 1), new(2, 1)]
        };
        var blocker = new LightingOccluder
        {
            Id = "blocking", LayerId = "PLAYER",
            Vertices = [new(0.5, -1), new(1.5, -1), new(1.5, 1), new(0.5, 1)]
        };
        var surface = new ProductionSurfaceSubmission
        {
            InstanceId = "own", CasterId = "own", CastsShadows = true,
            ReceivesShadows = true, LayerId = "PLAYER", MaterialDepthZ = 0,
            Shading = ProductionSurfaceShading.Hybrid
        };
        var lights = new[] { Light("a", -2, 0), Light("b", -1, 0),
            Light("c", 0, 0, -1), Light("d", 1, 2), Light("e", 0, 0, 1) };
        IReadOnlyList<RadialLight> chosen = ProductionSurfaceLightSelection.Select(
            lights, layer, new(3, 0), maximum: 12);
        Assert.Equal(5, chosen.Count);
        var flattened = ProductionMaterialLightPlan.Batches(chosen.Count)
            .SelectMany(batch => chosen.Skip(batch.Start).Take(batch.Count)).ToArray();
        Assert.Equal(chosen.Select(l => l.Id), flattened.Select(l => l.Id));
        Assert.All(flattened, light => Assert.DoesNotContain("own",
            ProductionWallLightRouting.OtherBlockers(surface, light, [wall, blocker])
                .Select(b => b.Id)));
        Assert.True(ProductionWallLightRouting.Behind(surface, lights[2], wall));
        Assert.False(ProductionWallLightRouting.Behind(surface, lights[4], wall));
        Assert.Contains(ProductionWallLightRouting.OtherBlockers(surface, lights[2],
            [wall, blocker]), b => b.Id == "blocking");
    }

    [Fact]
    public void AccumulationReferenceDoesNotMultiplyAmbientOrLoseOddFinalLight()
    {
        // This pure numerical reference guards the batch design, not GPU blend state.
        double ambient = .13;
        double[] contributions = [.04, .07, .06, .03, .05];
        double reference = Math.Min(1, ambient + contributions.Sum());
        double actual = ambient;
        foreach (var pair in ProductionMaterialLightPlan.Batches(contributions.Length))
            for (int i = 0; i < pair.Count; i++)
                actual += contributions[pair.Start + i];
        Assert.Equal(reference, Math.Min(1, actual), 12);
        Assert.NotEqual(reference, Math.Min(1, ambient * 3 + contributions.Sum()));
    }

    private static string Fixture()
    {
        string? folder = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && folder is not null; i++)
        {
            string candidate = Path.Combine(folder, "Content", "Levels", "LightingIntegrationTest.ldtk");
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            folder = Directory.GetParent(folder)?.FullName;
        }
        throw new FileNotFoundException("Phase 18 LDtk acceptance fixture not found.");
    }

    [Fact]
    public void ActualLdTkSceneExercisesFiveAndFourLightMaterialSurfaces()
    {
        var scene = LdtkLightingImporter.FromJson(Fixture(), "LIGHTING_INTEGRATION_TEST");
        Assert.Equal(11, scene.Lights.Count);
        Assert.Equal(3, scene.OccluderMotions.Count); // Phase 16 dynamic walls preserved.
        var opted = scene.Surfaces.Where(s =>
            s.Value.MaterialShading != ProductionSurfaceShading.Legacy).ToArray();
        Assert.Equal(2, opted.Length);
        var layer = scene.Layers.Get("PLAYER");
        foreach (var surface in opted)
        {
            Assert.True(scene.SurfaceCenters.TryGetValue(surface.Key, out LightPoint center));
            var full = ProductionSurfaceLightSelection.Select(scene.Lights, layer,
                center, maximum: ProductionMaterialLightPlan.MaximumLights);
            Assert.True(full.Count >= 4, "Both authored GPU material samples need >=4 contributing lights.");
            Assert.Equal(2, ProductionSurfaceLightSelection.Select(scene.Lights, layer,
                center).Count);
        }
        var pillar = opted.Single(s => s.Value.CastsShadows);
        var wall = ProductionWallLightRouting.OwnWall(
            ProductionSurfaceSubmission.From(pillar.Key, pillar.Value), scene.Occluders);
        Assert.NotNull(wall);
        Assert.True(scene.Lights.Count(l => ProductionWallLightRouting.Behind(
            ProductionSurfaceSubmission.From(pillar.Key, pillar.Value), l, wall)) >= 3);
        Assert.Contains(scene.Lights, l => l.VirtualZ > pillar.Value.MaterialDepthZ &&
            l.AffectsLayers is not null && l.AffectsLayers.Contains("PLAYER"));
    }
}
