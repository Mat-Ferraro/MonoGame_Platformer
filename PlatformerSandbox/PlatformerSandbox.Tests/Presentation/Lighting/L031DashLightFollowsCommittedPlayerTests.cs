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

public sealed class L031DashLightFollowsCommittedPlayerTests
{
    private static readonly LightingViewport View = new(1280, 720, new LightPoint(2, 1), 32);

    private static GameplayLightingPresenter Presenter()
    {
        var scene = new LdtkLightingScene("TEST", Array.Empty<RadialLight>(),
            Array.Empty<LightingOccluder>(),
            new Dictionary<string, LightingSurfaceOptions>(),
            new Dictionary<string, LightPoint>());
        var presenter = new GameplayLightingPresenter();
        presenter.EnterWorld(78, scene);
        return presenter;
    }

    private static WorldGameplayEvent Pulse(long tick = 1, int player = 1) =>
        new(78, tick, 1, WorldGameplayEventKind.AbilityActivated, player,
            new Vector2D(0, 1), AbilityId: CharacterAbilityId.Pulse);

    private static WorldPlayerSnapshot Player(int id, double x, double y) =>
        new(id, Aabb.FromCenterAndHalfExtents(new Vector2D(x, y),
            new Vector2D(.2, .4)), Vector2D.Zero,
            false, false, null, 0, false);

    private static WorldStepResult Step(long tick, params WorldPlayerSnapshot[] players) =>
        new(tick, tick / 120.0, players);

    private static RadialLight Flash(GameplayLightingPresenter presenter) =>
        Assert.Single(presenter.BuildVisible(View).Where(l => l.Owner == "flash"));

    [Fact]
    public void PulseTracksCommittedPlayerAcrossMultipleTicks()
    {
        var presenter = Presenter();
        presenter.Present(Pulse());
        Assert.Equal(new LightPoint(0, 1), Flash(presenter).Position);
        presenter.FollowCommittedPlayers(Step(1, Player(1, 1.2, 1)));
        Assert.Equal(new LightPoint(1.2, 1), Flash(presenter).Position);
        presenter.FollowCommittedPlayers(Step(2, Player(1, 2.6, 1.4)));
        Assert.Equal(new LightPoint(2.6, 1.4), Flash(presenter).Position);
        Assert.True(Flash(presenter).RevealsSilhouettes);
        Assert.True(Flash(presenter).CastShadows);
    }

    [Fact]
    public void LandingFlashDoesNotFollowPlayer()
    {
        var presenter = Presenter();
        presenter.Present(new WorldGameplayEvent(78, 1, 1,
            WorldGameplayEventKind.Landed, 1, new Vector2D(0, 1)));
        presenter.FollowCommittedPlayers(Step(2, Player(1, 5, 1)));
        Assert.Equal(new LightPoint(0, 1), Flash(presenter).Position);
    }

    [Fact]
    public void MissingOrRemovedPlayerDoesNotLeaveGhostFlash()
    {
        var presenter = Presenter();
        presenter.Present(Pulse());
        presenter.FollowCommittedPlayers(Step(2, Player(2, 8, 1)));
        Assert.Empty(presenter.BuildVisible(View).Where(l => l.Owner == "flash"));
        presenter.Present(Pulse(3));
        presenter.Present(new WorldGameplayEvent(78, 4, 1,
            WorldGameplayEventKind.PlayerRemoved, 1, new Vector2D(3, 1)));
        Assert.Empty(presenter.BuildVisible(View).Where(l => l.Owner == "flash"));
    }

    [Fact]
    public void RespawnClearsOldPulseAndSpawnsStationaryGreenFlash()
    {
        var presenter = Presenter();
        presenter.Present(Pulse());
        presenter.Present(new WorldGameplayEvent(78, 2, 1,
            WorldGameplayEventKind.PlayerRespawned, 1, new Vector2D(9, 1)));
        RadialLight light = Flash(presenter);
        Assert.Equal(new LightPoint(9, 1), light.Position);
        Assert.False(light.RevealsSilhouettes);
        presenter.FollowCommittedPlayers(Step(3, Player(1, 10, 1)));
        Assert.Equal(new LightPoint(9, 1), Flash(presenter).Position);
    }

    [Fact]
    public void CrateFrontFaceGetsPulseWhileBackgroundBehindCrateStaysOccluded()
    {
        var presenter = Presenter();
        presenter.Present(Pulse());
        presenter.FollowCommittedPlayers(Step(1, Player(1, 1.5, 1)));
        RadialLight pulse = Flash(presenter);
        var blocker = new LightingOccluder
        {
            Id = "crate", LayerId = "PLAYER",
            Vertices =
            [
                new LightPoint(1, 0), new LightPoint(2, 0),
                new LightPoint(2, 2), new LightPoint(1, 2)
            ]
        };
        var art = new LightingArtPreset
        {
            Id = "TEST", Ambient = new LightRgb(.02, .02, .02)
        };
        SceneLightingLayer layer = SceneLightingLayerSet.Standard11().Gameplay;
        LightRgb front = LightingWorldSampler.SampleSilhouetteReveal(
            new LightPoint(1.5, 1), LightChannel.Terrain,
            true, "crate", art, [pulse], [blocker], layer);
        LightRgb behind = LightingWorldSampler.Sample(
            new LightPoint(2.8, 1), LightChannel.Terrain,
            true, null, art, [pulse], [blocker], layer);
        Assert.True(front.B > .3);
        Assert.Equal(.02, behind.B, 8);
    }

    [Fact]
    public void DuplicateActivationAndSceneChangeClearFollowingFlash()
    {
        var presenter = Presenter();
        var pulse = Pulse();
        presenter.Present(pulse);
        presenter.Present(pulse);
        presenter.FollowCommittedPlayers(Step(1, Player(1, 1, 1)));
        Assert.Equal(1, presenter.ActiveFlashes);
        presenter.Clear();
        Assert.Empty(presenter.BuildVisible(View));
    }
}
