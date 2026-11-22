using System;
using System.IO;
using System.Linq;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Foundation.Presentation.Lighting;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>Regression checks for the dedicated flat lighting diagnostics level.
/// Existing connected caverns must remain completely separate.</summary>
public sealed class LFlatLightingDiagnosticLevelTests
{
    private const string LevelId = "LIGHTING_FLAT_TEST";
    private static string Find(string relative)
    {
        string? folder = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && folder is not null; i++)
        {
            string path = Path.Combine(folder, relative);
            if (File.Exists(path)) return path;
            folder = Directory.GetParent(folder)?.FullName;
        }
        throw new FileNotFoundException("Missing flat lighting test asset: " + relative);
    }

    private static string FilePath() => Find(Path.Combine("Content", "Levels", "LightingFlatTest.ldtk"));
    private static string Json() => File.ReadAllText(FilePath());

    [Fact]
    public void FlatLevelIsDiscoveredAsAnIndependentPlayableDestination()
    {
        var catalog = LdtkContentCatalog.Discover(Path.GetDirectoryName(FilePath())!);
        Assert.True(catalog.Levels.ContainsKey(LevelId));
        Assert.True(catalog.Supports(new SceneDestination(LevelId, GameLevelIds.DefaultEntrance)));
        Assert.Equal(LevelId, catalog.Prepare(new SceneDestination(
            LevelId, GameLevelIds.DefaultEntrance)).Imported.World.LevelId);
        Assert.True(catalog.Levels.ContainsKey("CAVERN_A"));
        Assert.True(catalog.Levels.ContainsKey("CAVERN_B"));
        Assert.True(catalog.Levels.ContainsKey("CAVERN_C"));
    }

    [Fact]
    public void OneContinuousFloorHasNoObstaclesOrGameplayTriggers()
    {
        LdtkConnectedLevel level = LdtkConnectedWorldCatalog.FromJson(Json()).
            Require(new SceneDestination(LevelId, GameLevelIds.DefaultEntrance));
        var floor = Assert.Single(level.Imported.World.StaticColliders);
        Assert.Equal(0, floor.Bounds.Left, 6);
        Assert.Equal(80, floor.Bounds.Right, 6);
        Assert.Equal(0, floor.Bounds.Bottom, 6);
        Assert.Equal(14, floor.Bounds.Top, 6);
        Assert.Single(level.Imported.World.SpawnPoints);
        Assert.True(level.Imported.World.SpawnPoints[0].BoundsAtStart.Bottom > floor.Bounds.Top);
        Assert.Empty(level.Exits);
        Assert.Empty(level.WindRegions);
        Assert.Empty(level.Checkpoints);
        Assert.Empty(level.Imported.World.MovingPlatformTrajectories);
    }

    [Fact]
    public void AuthoredLightsIncludeBothShadowedAndUnshadowedSources()
    {
        LdtkLightingScene scene = LdtkLightingImporter.FromJson(Json(), LevelId);
        Assert.Equal(4, scene.Lights.Count);
        Assert.Equal(2, scene.Lights.Count(x => x.CastShadows));
        Assert.Equal(2, scene.Lights.Count(x => !x.CastShadows));
        Assert.All(scene.Lights, light => light.Validate());
        Assert.Equal(3, scene.Occluders.Count);
        Assert.All(scene.Occluders, blocker => blocker.Validate());
    }

    [Fact]
    public void IdenticalCrateArtDemonstratesIndependentSurfaceLightingModes()
    {
        string json = Json();
        var artwork = LdtkTerrainArtworkImporter.FromJson(json, LevelId).Artworks;
        var crates = artwork.Where(x => x.ImagePath.EndsWith("/Crate.png", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, crates.Length);
        Assert.Equal(crates[0].WidthPx, crates[1].WidthPx);
        Assert.Equal(crates[0].HeightPx, crates[1].HeightPx);
        LdtkLightingScene lights = LdtkLightingImporter.FromJson(json, LevelId);
        Assert.All(crates, x => Assert.True(lights.GetSurface(x.InstanceId).CastsShadows));
        var silhouette = Assert.Single(crates.Where(x => lights.GetSurface(x.InstanceId).LightingMode == LightingSurfaceMode.Silhouette));
        var lit = Assert.Single(crates.Where(x => lights.GetSurface(x.InstanceId).LightingMode == LightingSurfaceMode.Lit));
        Assert.False(lights.GetSurface(silhouette.InstanceId).ReceivesLight);
        Assert.True(lights.GetSurface(lit.InstanceId).ReceivesLight);
        Assert.NotEqual(silhouette.InstanceId, lit.InstanceId);
        Assert.All(crates, x => Assert.Contains(lights.Occluders, b => b.Id == x.InstanceId));
    }

    [Fact]
    public void LitBackWallFloorAndSilhouetteForegroundContrastWithUnlitReference()
    {
        string json = Json();
        var artwork = LdtkTerrainArtworkImporter.FromJson(json, LevelId).Artworks;
        LdtkLightingScene lighting = LdtkLightingImporter.FromJson(json, LevelId);
        Assert.Equal(13, artwork.Count);
        var backdrop = Assert.Single(artwork.Where(x => x.ImagePath.EndsWith("/Backdrop.png", StringComparison.Ordinal)));
        var ground = Assert.Single(artwork.Where(x => x.ImagePath.EndsWith("/Ground.png", StringComparison.Ordinal)));
        Assert.True(lighting.GetSurface(backdrop.InstanceId).ReceivesLight);
        Assert.True(lighting.GetSurface(backdrop.InstanceId).ReceivesShadows);
        Assert.True(lighting.GetSurface(ground.InstanceId).ReceivesLight);
        var unlit = Assert.Single(artwork.Where(x => x.ImagePath.EndsWith("/Unlit.png", StringComparison.Ordinal)));
        Assert.False(lighting.GetSurface(unlit.InstanceId).ReceivesLight);
        Assert.False(lighting.GetSurface(unlit.InstanceId).ReceivesShadows);
        // L02 intentionally makes foreground vines light-receiving silhouettes. They
        // stay dark at baseline and brighten only when a light targets their FG3 layer.
        var vines = Assert.Single(artwork.Where(x => x.ImagePath.EndsWith("/Vines.png", StringComparison.Ordinal)));
        var vinesSurface = lighting.GetSurface(vines.InstanceId);
        Assert.True(vinesSurface.ReceivesLight);
        Assert.False(vinesSurface.ReceivesShadows);
        Assert.Equal("FG3", vinesSurface.LayerId);
        var vinesLayer = lighting.Layers.Get(vinesSurface.LayerId);
        Assert.Equal(RenderLayerLightingMode.Silhouette, vinesLayer.Mode);
        Assert.Single(lighting.Lights.Where(light => LightLayerRouting.Illuminates(light, vinesLayer)));
    }

    [Fact]
    public void EveryAuthoredPngExistsAtTheExactRelativePath()
    {
        string folder = Path.GetDirectoryName(FilePath())!;
        var artwork = LdtkTerrainArtworkImporter.FromJson(Json(), LevelId).Artworks;
        Assert.All(artwork, x => Assert.True(
            File.Exists(Path.Combine(folder, x.ImagePath.Replace('/', Path.DirectorySeparatorChar))),
            "Missing authored PNG: " + x.ImagePath));
    }
}
