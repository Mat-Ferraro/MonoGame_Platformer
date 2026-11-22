using System;
using System.Collections.Generic;
using System.Linq;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Abilities;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.Presentation;
using PlatformerSandbox.Gameplay.World;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

/// <summary>Player-origin Pulse flashes must light the front face of nearby
/// occluding props without changing ordinary background light routing.</summary>
public sealed class L028PulseForegroundLightTests
{
    private static LdtkLightingScene Scene(SceneLightingLayerSet? layers = null) =>
        new("TEST", Array.Empty<RadialLight>(), Array.Empty<LightingOccluder>(),
            new Dictionary<string, LightingSurfaceOptions>(),
            new Dictionary<string, LightPoint>(), layers);

    private static WorldGameplayEvent Event(WorldGameplayEventKind kind,
        CharacterAbilityId? ability = CharacterAbilityId.Pulse, long tick = 1) =>
        new(78, tick, 1, kind, 1, new Vector2D(2, 2), AbilityId: ability);

    private static RadialLight Flash(GameplayLightingPresenter presenter) =>
        Assert.Single(presenter.BuildVisible(new LightingViewport(1280, 720,
            new LightPoint(2, 2), 32)).Where(x => x.Owner == "flash"));

    [Fact]
    public void PulseIlluminatesGameplayAndOnlyNearestForegroundWhileRetainingBackground()
    {
        var scene = Scene(SceneLightingLayerSet.Standard11());
        var presenter = new GameplayLightingPresenter();
        presenter.EnterWorld(78, scene);
        presenter.Present(Event(WorldGameplayEventKind.AbilityActivated));
        RadialLight light = Flash(presenter);
        Assert.True(light.RevealsSilhouettes);
        Assert.True(light.CastShadows);
        Assert.True(LightLayerRouting.Illuminates(light, scene.Layers.Get("PLAYER")));
        Assert.True(LightLayerRouting.Illuminates(light, scene.Layers.Get("FG1")));
        Assert.False(LightLayerRouting.Illuminates(light, scene.Layers.Get("FG2")));
        Assert.False(LightLayerRouting.Illuminates(light, scene.Layers.Get("FG5")));
        Assert.True(LightLayerRouting.Illuminates(light, scene.Layers.Get("BG1")));
        Assert.True(LightLayerRouting.Illuminates(light, scene.Layers.Get("BG5")));
        Assert.Equal(new[] { "PLAYER", "FG1" }, light.OccluderLayers!.ToArray());
        LightLayerRouting.ValidateRouting(light, scene.Layers);
    }

    [Fact]
    public void FrontFacingRoutingUsesCustomSceneLayerIdentifiers()
    {
        var layers = new SceneLightingLayerSet(new[]
        {
            new SceneLightingLayer { Id = "SCENERY", Order = -1, Group = RenderLayerGroup.Background },
            new SceneLightingLayer { Id = "HEROES", Order = 0, Group = RenderLayerGroup.Gameplay },
            new SceneLightingLayer { Id = "NEAR", Order = 1, Group = RenderLayerGroup.Foreground },
            new SceneLightingLayer { Id = "CLOSE", Order = 2, Group = RenderLayerGroup.Foreground }
        });
        var presenter = new GameplayLightingPresenter();
        presenter.EnterWorld(78, Scene(layers));
        presenter.Present(Event(WorldGameplayEventKind.AbilityActivated));
        RadialLight pulse = Flash(presenter);
        Assert.Equal(new[] { "SCENERY", "HEROES", "NEAR" }, pulse.AffectsLayers!.ToArray());
        Assert.Equal(new[] { "HEROES", "NEAR" }, pulse.OccluderLayers!.ToArray());
        LightLayerRouting.ValidateRouting(pulse, layers);
    }

    [Fact]
    public void PulseCanRevealSilhouetteCrateWithoutLightingItFromOrdinaryLamps()
    {
        var layer = SceneLightingLayerSet.Standard11().Gameplay;
        var surface = new LightingSurfaceOptions
        {
            LayerId = "PLAYER", LightingMode = LightingSurfaceMode.Silhouette,
            ReceivesLight = false, ReceivesShadows = false, CastsShadows = true
        };
        var art = new LightingArtPreset { Id = "test", Ambient = new LightRgb(.02, .03, .04) };
        var pulse = new RadialLight
        {
            Id = "pulse", Owner = "flash", Position = new LightPoint(0, 0),
            Radius = 4, Intensity = 1.25, Color = new LightRgb(.2, .7, 1),
            Channels = LightChannel.All, CastShadows = true,
            RevealsSilhouettes = true, AffectsLayers = new[] { "PLAYER", "FG1" },
            OccluderLayers = new[] { "PLAYER", "FG1" }
        };
        var backdrop = pulse with { Id = "ambientLamp", Owner = "level", RevealsSilhouettes = false };
        LightPoint near = new(.5, .5);
        LightRgb noReveal = LightingWorldSampler.SampleSilhouetteReveal(near,
            LightChannel.Terrain, false, "crate", art, new[] { backdrop },
            Array.Empty<LightingOccluder>(), layer);
        LightRgb yesReveal = LightingWorldSampler.SampleSilhouetteReveal(near,
            LightChannel.Terrain, false, "crate", art, new[] { pulse },
            Array.Empty<LightingOccluder>(), layer);
        LightRgb normal = LightingSurfaceShading.ResolveTint(surface, layer, LightRgb.White, noReveal);
        LightRgb flashed = LightingSurfaceShading.ResolveTint(surface, layer, LightRgb.White, yesReveal);
        Assert.InRange(normal.B, .1, .14);
        Assert.True(flashed.B > normal.B + .2);
        Assert.True(flashed.G > normal.G + .1);
        Assert.True(surface.CastsShadows); // Not changed by front-light reveal.
    }

    [Fact]
    public void FrontFacingLightFadesWithDistanceAndNeverLeaksIntoUnselectedForeground()
    {
        SceneLightingLayerSet layers = SceneLightingLayerSet.Standard11();
        var art = new LightingArtPreset { Id = "test", Ambient = new LightRgb(.02, .02, .02) };
        var pulse = new RadialLight
        {
            Id = "pulse", Owner = "flash", Position = new LightPoint(0, 0),
            Radius = 4, Intensity = 1, RevealsSilhouettes = true,
            AffectsLayers = new[] { "PLAYER", "FG1" }
        };
        LightRgb near = LightingWorldSampler.SampleSilhouetteReveal(new LightPoint(0, 0),
            LightChannel.Terrain, false, null, art, new[] { pulse },
            Array.Empty<LightingOccluder>(), layers.Get("PLAYER"));
        LightRgb far = LightingWorldSampler.SampleSilhouetteReveal(new LightPoint(3.5, 0),
            LightChannel.Terrain, false, null, art, new[] { pulse },
            Array.Empty<LightingOccluder>(), layers.Get("PLAYER"));
        LightRgb outOfRange = LightingWorldSampler.SampleSilhouetteReveal(new LightPoint(5, 0),
            LightChannel.Terrain, false, null, art, new[] { pulse },
            Array.Empty<LightingOccluder>(), layers.Get("PLAYER"));
        LightRgb wrongLayer = LightingWorldSampler.SampleSilhouetteReveal(new LightPoint(0, 0),
            LightChannel.Effects, false, null, art, new[] { pulse },
            Array.Empty<LightingOccluder>(), layers.Get("FG3"));
        Assert.True(near.R > far.R);
        Assert.Equal(LightRgb.Black, outOfRange);
        Assert.Equal(LightRgb.Black, wrongLayer);
    }

    [Fact]
    public void LitShadowCasterReceivesPulseOnItsFrontWithoutSelfOcclusion()
    {
        var layer = SceneLightingLayerSet.Standard11().Gameplay;
        var art = new LightingArtPreset { Id = "test", Ambient = new LightRgb(.03, .03, .03) };
        var pulse = new RadialLight
        {
            Id = "pulse", Owner = "flash", Position = new LightPoint(0, 2),
            Radius = 8, Intensity = 1, Color = new LightRgb(.2, .7, 1),
            CastShadows = true, RevealsSilhouettes = true,
            AffectsLayers = new[] { "PLAYER" }, OccluderLayers = new[] { "PLAYER" }
        };
        var crate = new LightingOccluder
        {
            Id = "crate", LayerId = "PLAYER",
            Vertices = new[]
            {
                new LightPoint(1, 1), new LightPoint(3, 1),
                new LightPoint(3, 3), new LightPoint(1, 3)
            }
        };
        LightPoint receiver = new(3.5, 2);
        LightRgb litOwnFace = LightingWorldSampler.Sample(receiver,
            LightChannel.Terrain, true, "crate", art, new[] { pulse }, new[] { crate }, layer);
        LightRgb backdropBlocked = LightingWorldSampler.Sample(receiver,
            LightChannel.Terrain, true, null, art, new[] { pulse }, new[] { crate }, layer);
        Assert.True(litOwnFace.B > backdropBlocked.B + .05);
    }

    [Fact]
    public void LandAndRespawnDoNotUnexpectedlyRevealSilhouettes()
    {
        foreach (var kind in new[] { WorldGameplayEventKind.Landed,
            WorldGameplayEventKind.PlayerRespawned })
        {
            var presenter = new GameplayLightingPresenter();
            presenter.EnterWorld(78, Scene());
            presenter.Present(Event(kind));
            RadialLight light = Flash(presenter);
            Assert.False(light.RevealsSilhouettes);
            Assert.Null(light.AffectsLayers);
            Assert.False(light.CastShadows);
        }
    }

    [Fact]
    public void DuplicateAcceptedDashDoesNotStackAndWorldChangeClearsFlash()
    {
        var presenter = new GameplayLightingPresenter();
        presenter.EnterWorld(78, Scene());
        var ability = Event(WorldGameplayEventKind.AbilityActivated);
        presenter.Present(ability);
        presenter.Present(ability);
        Assert.Equal(1, presenter.ActiveFlashes);
        presenter.Update(.1, worldPaused: true);
        Assert.Equal(1, presenter.ActiveFlashes);
        presenter.EnterWorld(79, Scene());
        Assert.Empty(presenter.BuildVisible(new LightingViewport(1280, 720,
            new LightPoint(2, 2), 32)).Where(x => x.Owner == "flash"));
    }
}
