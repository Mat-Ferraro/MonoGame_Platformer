using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation;
using PlatformerSandbox.Foundation.Presentation.Particles;
using PlatformerSandbox.Gameplay.Abilities;
using PlatformerSandbox.Gameplay.Presentation;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Presentation.Particles;

public sealed class P04GameplayParticlePresenterTests
{
    private static readonly Vector2D Here = new(4, 2);

    private static ParticleCompositeCatalog Catalog()
    {
        ParticleEmitterDefinition burst = new()
        {
            Id = "burst", BurstCount = 4, LifetimeSeconds = new(1, 1),
            Speed = new(0, 0), StartSize = new(.1, .1), EndSize = new(0, 0)
        };
        ParticleEmitterDefinition continuous = new()
        {
            Id = "flow", RatePerSecond = 20, LifetimeSeconds = new(2, 2),
            Speed = new(0, 0), StartSize = new(.1, .1), EndSize = new(0, 0)
        };
        static ParticleCompositeDefinition Effect(string id, string emitter, bool ui = false,
            bool follow = false) => new()
        {
            Id = id,
            Parts = [new ParticleEffectPartDefinition
            {
                EmitterId = emitter, Capacity = 36,
                Space = ui ? ParticleCoordinateSpace.Screen : ParticleCoordinateSpace.World,
                Layer = ui ? ParticleVisualLayer.Ui : ParticleVisualLayer.World,
                Anchor = follow ? ParticleEmitterAnchor.Follow : ParticleEmitterAnchor.Fixed
            }]
        };
        return new ParticleCompositeCatalog([burst, continuous],
        [
            Effect("fx.jump", "burst"), Effect("fx.landing", "burst"),
            Effect("fx.wall", "burst"), Effect("fx.dash", "flow", follow: true),
            Effect("fx.respawn", "burst"), Effect("fx.sceneMotes", "flow", follow: true),
            Effect("fx.uiSelect", "burst", ui: true),
            Effect("fx.uiNavigate", "burst", ui: true)
        ]);
    }

    private static ParticleGameplayBindings Bindings() => new()
    {
        Version = 1, JumpStarted = "fx.jump", Landed = "fx.landing",
        WallHit = "fx.wall", PulseActivated = "fx.dash",
        PlayerRespawned = "fx.respawn", SceneAmbient = "fx.sceneMotes",
        UiConfirm = "fx.uiSelect", UiBack = "fx.uiSelect",
        UiNavigate = "fx.uiNavigate"
    };

    private static ParticleEffectPresenter Presenter(bool ambient = false)
    {
        ParticleEffectPresenter presenter = new(Catalog(), Bindings());
        presenter.EnterWorld(77, ambient ? Here : null);
        return presenter;
    }

    private static WorldGameplayEvent Event(WorldGameplayEventKind kind, int sequence,
        int playerId = 1, long worldId = 77,
        CharacterAbilityId? ability = null) =>
        new(worldId, 30, sequence, kind, playerId, Here,
            AbilityId: ability);

    private static WorldPlayerSnapshot Player(int id, double x) => new(
        id, new Aabb(new Vector2D(x - .5, 1), new Vector2D(x + .5, 2)),
        Vector2D.Zero, true, false, null, 0, false);

    private static WorldStepResult Step(int tick,
        IReadOnlyList<WorldGameplayEvent> events, params WorldPlayerSnapshot[] players) =>
        new(tick, tick / 60.0, players) { Events = events };

    [Fact]
    public void JumpIsPostCommitOnlyAndRejectedCommandsHaveNoEffect()
    {
        ParticleEffectPresenter presenter = Presenter();
        Assert.Empty(presenter.WorldInstances);
        presenter.Present(Event(WorldGameplayEventKind.AbilityRejected, 1));
        presenter.Present(Event(WorldGameplayEventKind.CharacterStateRequestDenied, 2));
        Assert.Equal(0, presenter.ActiveWorldEffects);
        presenter.PresentCommitted(Step(30,
            [Event(WorldGameplayEventKind.JumpStarted, 3)], Player(1, Here.X)));
        Assert.Equal(1, presenter.ActiveWorldEffects);
        Assert.Equal(4, Assert.Single(presenter.WorldInstances).Parts[0].Particles.ActiveCount);
    }

    [Fact]
    public void IdempotencyStopsRepeatedOrStaleCommittedEvents()
    {
        ParticleEffectPresenter presenter = Presenter();
        WorldGameplayEvent jump = Event(WorldGameplayEventKind.JumpStarted, 1);
        presenter.Present(jump);
        presenter.Present(jump);
        presenter.Present(Event(WorldGameplayEventKind.Landed, 2, worldId: 76));
        Assert.Equal(1, presenter.ActiveWorldEffects);
        presenter.Present(Event(WorldGameplayEventKind.Landed, 3));
        Assert.Equal(2, presenter.ActiveWorldEffects);
    }

    [Fact]
    public void JumpLandingAndWallEventsUseIndependentBoundedEffects()
    {
        ParticleEffectPresenter presenter = Presenter();
        presenter.Present(Event(WorldGameplayEventKind.JumpStarted, 1));
        presenter.Present(Event(WorldGameplayEventKind.Landed, 2));
        presenter.Present(Event(WorldGameplayEventKind.WallHit, 3));
        Assert.Equal(3, presenter.ActiveWorldEffects);
        Assert.Equal(new[] { "fx.jump", "fx.landing", "fx.wall" },
            presenter.WorldInstances.Select(x => x.Definition.Id).ToArray());
    }

    [Fact]
    public void AcceptedPulseTrailFollowsCommittedPositionAndStopsOnCompletion()
    {
        ParticleEffectPresenter presenter = Presenter();
        presenter.PresentCommitted(Step(30,
            [Event(WorldGameplayEventKind.AbilityActivated, 1, ability: CharacterAbilityId.Pulse)],
            Player(1, 4)));
        ParticleCompositeInstance trail = Assert.Single(presenter.WorldInstances);
        presenter.Update(.1, false);
        Assert.Equal(2, trail.Parts[0].Particles.ActiveCount);
        Vector2D oldParticlePosition = trail.Parts[0].Particles.GetAt(0).Position;
        presenter.PresentCommitted(Step(31, [], Player(1, 12)));
        presenter.Update(.1, false);
        Assert.Equal(4, trail.Parts[0].Particles.ActiveCount);
        Assert.Equal(oldParticlePosition, trail.Parts[0].Particles.GetAt(0).Position);
        Assert.Equal(12, trail.Parts[0].Particles.GetAt(2).Position.X, 10);
        presenter.Present(Event(WorldGameplayEventKind.AbilityCompleted, 2,
            ability: CharacterAbilityId.Pulse));
        Assert.False(trail.Parts[0].IsEmitting);
        presenter.Update(.1, false);
        Assert.Equal(4, trail.Parts[0].Particles.ActiveCount);
    }

    [Fact]
    public void RejectedPulseAndOtherAbilitiesDoNotSpawnDashTrail()
    {
        ParticleEffectPresenter presenter = Presenter();
        presenter.Present(Event(WorldGameplayEventKind.AbilityRejected, 1,
            ability: CharacterAbilityId.Pulse));
        presenter.Present(Event(WorldGameplayEventKind.AbilityActivated, 2,
            ability: CharacterAbilityId.Focus));
        Assert.Equal(0, presenter.ActiveWorldEffects);
    }

    [Fact]
    public void RespawnClearsOnlyTheAffectedPlayer()
    {
        ParticleEffectPresenter presenter = Presenter();
        presenter.Present(Event(WorldGameplayEventKind.JumpStarted, 1));
        presenter.Present(Event(WorldGameplayEventKind.Landed, 2, playerId: 2));
        ParticleCompositeInstance two = Assert.Single(presenter.WorldInstances, x => x.Definition.Id == "fx.landing");
        presenter.Present(Event(WorldGameplayEventKind.PlayerRespawned, 3));
        Assert.Equal(2, presenter.ActiveWorldEffects);
        Assert.Contains(two, presenter.WorldInstances);
        Assert.Contains(presenter.WorldInstances, x => x.Definition.Id == "fx.respawn");
        presenter.Present(Event(WorldGameplayEventKind.PlayerRemoved, 4, playerId: 2));
        Assert.DoesNotContain(two, presenter.WorldInstances);
    }

    [Fact]
    public void WorldPauseFreezesGameplayParticlesButUiKeepsAging()
    {
        ParticleEffectPresenter presenter = Presenter();
        presenter.Present(Event(WorldGameplayEventKind.JumpStarted, 1));
        presenter.PresentUi(ParticleUiFeedback.Confirm, new Vector2D(400, 140));
        ParticleCompositeInstance world = Assert.Single(presenter.WorldInstances);
        ParticleCompositeInstance ui = Assert.Single(presenter.UiInstances);
        presenter.Update(.1, true);
        Assert.Equal(0, world.Parts[0].Particles.GetAt(0).AgeSeconds);
        Assert.Equal(.1, ui.Parts[0].Particles.GetAt(0).AgeSeconds, 10);
        presenter.Update(.1, false);
        Assert.Equal(.1, world.Parts[0].Particles.GetAt(0).AgeSeconds, 10);
    }

    [Fact]
    public void ReturningToTitleClearsOnlyWorldScopedEffects()
    {
        ParticleEffectPresenter presenter = Presenter(ambient: true);
        presenter.Present(Event(WorldGameplayEventKind.JumpStarted, 1));
        presenter.PresentUi(ParticleUiFeedback.Back, new Vector2D(300, 200));
        Assert.Equal(2, presenter.ActiveWorldEffects);
        presenter.LeaveWorld();
        Assert.Equal(0, presenter.ActiveWorldEffects);
        Assert.Equal(0, presenter.ActiveWorldInstanceId);
        Assert.Equal(1, presenter.ActiveUiEffects);
        presenter.Update(.1, true);
        Assert.Equal(.1, presenter.UiInstances[0].Parts[0].Particles.GetAt(0).AgeSeconds, 10);
    }

    [Fact]
    public void RestartReplacesWorldAndAllowsReusedTickSequenceInNewWorld()
    {
        ParticleEffectPresenter presenter = Presenter();
        presenter.Present(Event(WorldGameplayEventKind.JumpStarted, 1));
        presenter.EnterWorld(78);
        Assert.Equal(0, presenter.ActiveWorldEffects);
        presenter.Present(Event(WorldGameplayEventKind.JumpStarted, 1, worldId: 77));
        Assert.Equal(0, presenter.ActiveWorldEffects);
        presenter.Present(Event(WorldGameplayEventKind.JumpStarted, 1, worldId: 78));
        Assert.Equal(1, presenter.ActiveWorldEffects);
    }

    [Fact]
    public void SceneAtmosphereFollowsCommittedPlayerWithoutTeleportingOldParticles()
    {
        ParticleEffectPresenter presenter = Presenter(ambient: true);
        ParticleCompositeInstance ambient = Assert.Single(presenter.SceneInstances);
        presenter.Update(.1, false);
        var first = ambient.Parts[0].Particles.GetAt(0).Position;
        presenter.PresentCommitted(Step(31, [], Player(1, 18)));
        presenter.Update(.1, false);
        Assert.Equal(first, ambient.Parts[0].Particles.GetAt(0).Position);
        Assert.Equal(18, ambient.Parts[0].Particles.GetAt(2).Position.X, 10);
    }

    [Fact]
    public void DisablingCosmeticsDoesNotAffectGameEventsOrLeaveEffectsRunning()
    {
        ParticleEffectPresenter presenter = Presenter();
        WorldGameplayEvent jump = Event(WorldGameplayEventKind.JumpStarted, 1);
        presenter.Present(jump);
        presenter.SetEnabled(false);
        Assert.Equal(0, presenter.ActiveWorldEffects);
        presenter.Present(Event(WorldGameplayEventKind.JumpStarted, 2));
        presenter.PresentUi(ParticleUiFeedback.Confirm, Here);
        Assert.Equal(0, presenter.ActiveUiEffects);
        presenter.SetEnabled(true);
        presenter.Present(Event(WorldGameplayEventKind.JumpStarted, 3));
        Assert.Equal(1, presenter.ActiveWorldEffects);
    }

    [Fact]
    public void CapacityIsBoundedAndDoesNotChangeCommittedStepData()
    {
        ParticleEffectPresenter presenter = Presenter();
        WorldStepResult step = Step(30,
            Enumerable.Range(0, 40).Select(i => Event(WorldGameplayEventKind.JumpStarted, i)).ToArray(),
            Player(1, 4));
        WorldPlayerSnapshot unchanged = step.Players[0];
        presenter.PresentCommitted(step);
        Assert.Equal(12, presenter.ActiveWorldEffects);
        Assert.True(presenter.DroppedWorldEffects > 0);
        Assert.Equal(unchanged, step.Players[0]);
        Assert.Equal(40, step.Events.Count);
    }

    [Fact]
    public void InvalidBindingsFailBeforeGameStartsWithSilentFallback()
    {
        ParticleCompositeCatalog catalog = Catalog();
        Assert.Throws<ArgumentException>(() =>
            (Bindings() with { JumpStarted = "not.an.effect" }).Validate(catalog));
        Assert.Throws<ArgumentException>(() =>
            (Bindings() with { UiConfirm = "fx.jump" }).Validate(catalog));
        Assert.Throws<ArgumentException>(() =>
            (Bindings() with { Landed = "fx.uiSelect" }).Validate(catalog));
        Assert.Throws<NotSupportedException>(() =>
            (Bindings() with { Version = 2 }).Validate(catalog));
    }

    [Fact]
    public void LdtkParticleProjectionMatchesClampedWorldCamera()
    {
        var view = Camera2DView.FromFocus(new Vector2D(0, 5), 26, 20, 1280, 720, 32);
        ParticleViewport p = ParticleViewportAdapters.FromLdtkCamera(view);
        Vector2D world = new(5, 6);
        Assert.Equal(view.WorldRect(new Aabb(new Vector2D(5, 5), new Vector2D(6, 6))).X,
            p.ToScreen(new Vector2D(5, 6), ParticleCoordinateSpace.World).X, 0);
        Assert.Equal(view.ScreenToWorld(420, 300), p.ToWorld(420, 300));
        var gym = ParticleViewportAdapters.FromMovementGym(1280, 720, 8);
        // The gym baseline is 78% of viewport HEIGHT, not width.
        Assert.Equal(720 * .78, gym.ToScreen(new Vector2D(8, 0),
            ParticleCoordinateSpace.World).Y, 10);
        // Camera centered at world X=8 projects to half of viewport width.
        Assert.Equal(1280 * .5, gym.ToScreen(new Vector2D(8, 0),
            ParticleCoordinateSpace.World).X, 10);
    }

    [Fact]
    public void RealP04ManifestsParseAndBindingsResolve()
    {
        string root = AppContext.BaseDirectory;
        string? catalogPath = null;
        for (int depth = 0; depth < 12 && root is not null; depth++)
        {
            string path = Path.Combine(root, "Content", "Particles", "P04_GameplayEffects.json");
            if (File.Exists(path)) { catalogPath = path; break; }
            root = Directory.GetParent(root)?.FullName!;
        }
        Assert.NotNull(catalogPath);
        string folder = Path.GetDirectoryName(catalogPath)!;
        var catalog = ParticleCompositeCatalog.FromJson(File.ReadAllText(catalogPath));
        var bindings = ParticleGameplayBindings.FromJson(File.ReadAllText(
            Path.Combine(folder, "P04_EventBindings.json")), catalog);
        Assert.Equal("fx.dash", bindings.PulseActivated);
        Assert.True(catalog.Count >= 10);
        Assert.Equal(ParticleCoordinateSpace.Screen,
            catalog.Get(bindings.UiConfirm).Parts![0].Space);
    }

    [Fact]
    public void RealGameplayTrajectoryMatchesWithOrWithoutVfxConsumer()
    {
        WorldSession withVfx = SharedWorldClockGym.Create(worldInstanceId: 77);
        WorldSession withoutVfx = SharedWorldClockGym.Create(worldInstanceId: 77);
        ParticleEffectPresenter presenter = Presenter();
        for (int tick = 0; tick < 95; tick++)
        {
            WorldPlayerCommand[] commands =
            [
                new(1, HorizontalDirection.Right, JumpPressed: tick == 10),
                new(2, HorizontalDirection.Left, JumpPressed: tick == 24)
            ];
            WorldStepResult a = withVfx.Step(commands);
            WorldStepResult b = withoutVfx.Step(commands);
            presenter.PresentCommitted(a);
            presenter.Update(1.0 / 60.0, false);
            Assert.Equal(b.WorldTick, a.WorldTick);
            for (int i = 0; i < a.Players.Count; i++)
                Assert.Equal(b.Players[i], a.Players[i]);
            Assert.True(a.Events.SequenceEqual(b.Events));
        }
    }
}
