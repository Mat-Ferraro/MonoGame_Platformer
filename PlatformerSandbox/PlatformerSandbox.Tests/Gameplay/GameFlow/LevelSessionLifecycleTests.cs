using System;
using System.Collections.Generic;
using System.Linq;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Tests.Gameplay.GameFlow;

public sealed class LevelSessionLifecycleTests
{
    private static readonly GameSessionParticipant[] Members =
    [
        new(1, 1001, 1), new(2, 1002, 22)
    ];

    private static WorldSession MakeWorld(string level, string entrance, long runId = 1,
        IReadOnlyList<GameSessionParticipant>? players = null) =>
        RouteLevelGym.Create(new SceneDestination(level, entrance),
            players ?? Members, MovementGymSession.PlayableAssistancePreset,
            SimulationFaultSensitivity.Strict, runId);

    [Fact]
    public void BothLevelsExposeValidatedNamedEntrances()
    {
        foreach (string level in new[] { GameLevelIds.RouteA, GameLevelIds.RouteB })
        {
            foreach (string entrance in new[] {
                GameLevelIds.DefaultEntrance, RouteLevelGym.WestEntrance,
                RouteLevelGym.EastEntrance })
                Assert.True(RouteLevelGym.Catalog.Supports(new SceneDestination(level, entrance)));
            Assert.False(RouteLevelGym.Catalog.Supports(new SceneDestination(level, "MISSING")));
        }
        Assert.False(RouteLevelGym.Catalog.Supports(new SceneDestination("MISSING", "START")));
    }

    [Fact]
    public void DistinctEntrancesHaveDifferentAndCorrectPlayerSpawns()
    {
        WorldSession west = MakeWorld(GameLevelIds.RouteA, RouteLevelGym.WestEntrance, 101);
        WorldSession east = MakeWorld(GameLevelIds.RouteA, RouteLevelGym.EastEntrance, 102);
        Assert.Equal(RouteLevelGym.WestOne, west.GetPlayerSlot(1).SpawnId);
        Assert.Equal(RouteLevelGym.WestTwo, west.GetPlayerSlot(2).SpawnId);
        Assert.Equal(RouteLevelGym.EastOne, east.GetPlayerSlot(1).SpawnId);
        Assert.Equal(RouteLevelGym.EastTwo, east.GetPlayerSlot(2).SpawnId);
        Assert.True(west.GetPlayer(1).Bounds.Left < east.GetPlayer(1).Bounds.Left);
        Assert.True(west.GetPlayer(2).Bounds.Left < east.GetPlayer(2).Bounds.Left);
    }

    [Fact]
    public void ExitLinksPreserveExplicitEntranceDirection()
    {
        var from = new SceneDestination(GameLevelIds.RouteA);
        Assert.True(RouteLevelGym.Catalog.TryResolveExit(from,
            RouteLevelGym.LeftGateId, out SceneDestination? left));
        Assert.Equal(new SceneDestination(GameLevelIds.RouteB, RouteLevelGym.EastEntrance), left);
        Assert.True(RouteLevelGym.Catalog.TryResolveExit(from,
            RouteLevelGym.RightGateId, out SceneDestination? right));
        Assert.Equal(new SceneDestination(GameLevelIds.RouteB, RouteLevelGym.WestEntrance), right);
        Assert.False(RouteLevelGym.Catalog.TryResolveExit(from, 999, out _));
        Assert.True(RouteLevelGym.Catalog.TryResolveExit(right!,
            RouteLevelGym.RightGateId, out SceneDestination? back));
        Assert.Equal(new SceneDestination(GameLevelIds.RouteA, RouteLevelGym.WestEntrance), back);
    }

    [Fact]
    public void MissingExitTargetFailsAtCatalogConstruction()
    {
        var definition = AuthoredWorldGym.CreateDefinition();
        var entrance = new LevelEntranceDefinition(GameLevelIds.DefaultEntrance,
            AuthoredWorldGym.GroundSpawn,
            new Dictionary<int, string> { [1] = AuthoredWorldGym.LeftSpawn });
        var exit = new LevelExitDefinition(301, new SceneDestination("NO_SUCH_LEVEL"));
        var level = new LevelRouteDefinition(definition, [entrance], [exit]);
        Assert.Throws<ArgumentException>(() => new LevelRouteCatalog([level]));
    }

    [Fact]
    public void BadEntranceSpawnsAndUnknownExitTriggersAreRejected()
    {
        var definition = AuthoredWorldGym.CreateDefinition();
        Assert.Throws<ArgumentException>(() => new LevelRouteDefinition(definition,
            [new LevelEntranceDefinition("START", "NONEXISTENT",
                new Dictionary<int, string>())], []));
        Assert.Throws<ArgumentException>(() => new LevelRouteDefinition(definition,
            [new LevelEntranceDefinition("START", AuthoredWorldGym.GroundSpawn,
                new Dictionary<int, string>())],
            [new LevelExitDefinition(999, new SceneDestination(GameLevelIds.RouteB))]));
    }

    [Fact]
    public void RealTriggerProducesCommittedExitEventNotRepeatedEachTick()
    {
        WorldSession world = MakeWorld(GameLevelIds.RouteA, "START", 71);
        int entries = 0;
        for (int i = 0; i < 600; i++)
        {
            WorldStepResult step = world.Step([new WorldPlayerCommand(1, HorizontalDirection.Right)]);
            entries += step.Events.Count(e => e.Kind == WorldGameplayEventKind.TriggerEntered &&
                e.PlayerId == 1 && e.TriggerId == RouteLevelGym.RightGateId);
            if (entries > 0)
            {
                Assert.Equal(1, entries);
                // Stop inside the gate and ensure normal overlap doesn't repeat entry.
                for (int k = 0; k < 2; k++)
                    entries += world.Step([]).Events.Count(e =>
                        e.Kind == WorldGameplayEventKind.TriggerEntered &&
                        e.PlayerId == 1 && e.TriggerId == RouteLevelGym.RightGateId);
                break;
            }
        }
        Assert.Equal(1, entries);
        Assert.Empty(world.Snapshot().Events);
    }

    [Fact]
    public void TransitionCreatesFreshWorldWithoutRestartingSession()
    {
        var session = new GameplaySession(8, Members);
        var firstDestination = new SceneDestination(GameLevelIds.RouteA);
        WorldSession first = MakeWorld(GameLevelIds.RouteA, "START", 701);
        session.EnterLevel(firstDestination, first.PlayerSlots);
        for (int i = 0; i < 90; i++) first.Step([]);
        Assert.Equal(90, first.WorldTick);
        Assert.True(RouteLevelGym.Catalog.TryResolveExit(firstDestination,
            RouteLevelGym.RightGateId, out SceneDestination? target));
        WorldSession second = MakeWorld(target!.LevelId, target.EntranceId, 702,
            session.PreviewRoster(first.PlayerSlots, 1));
        Assert.Equal(0, second.WorldTick);
        Assert.Equal(90, first.WorldTick);
        Assert.Equal(701, first.WorldInstanceId);
        Assert.Equal(702, second.WorldInstanceId);
        session.EnterLevel(target, second.PlayerSlots);
        Assert.Equal(8, session.SessionId);
        Assert.Equal(2, session.LevelEntryCount);
        Assert.Equal(target, session.CurrentLevel);
        Assert.Equal(1001, second.GetPlayerSlot(1).CharacterId);
        Assert.Equal(1002, second.GetPlayerSlot(2).CharacterId);
    }

    [Fact]
    public void PlayerOnlyRespawnDoesNotRewindWorldOrOtherPlayer()
    {
        WorldSession world = MakeWorld(GameLevelIds.RouteB, RouteLevelGym.WestEntrance);
        for (int i = 0; i < 40; i++) world.Step([]);
        long tick = world.WorldTick;
        var otherPlayer = world.GetPlayer(2);
        world.RespawnPlayer(1);
        Assert.Equal(tick, world.WorldTick);
        Assert.Equal(otherPlayer.Bounds, world.GetPlayer(2).Bounds);
        Assert.Equal(otherPlayer.Velocity, world.GetPlayer(2).Velocity);
        world.Step([]);
        Assert.Equal(tick + 1, world.WorldTick);
    }

    [Fact]
    public void RestartResetsWorldClockButNotSessionOrEntrance()
    {
        var entrance = new SceneDestination(GameLevelIds.RouteB, RouteLevelGym.EastEntrance);
        var session = new GameplaySession(14, Members);
        WorldSession before = MakeWorld(entrance.LevelId, entrance.EntranceId, 801);
        session.EnterLevel(entrance, before.PlayerSlots);
        for (int i = 0; i < 120; i++) before.Step([]);
        WorldSession after = MakeWorld(entrance.LevelId, entrance.EntranceId, 802,
            session.PreviewRoster(before.PlayerSlots, 1));
        session.EnterLevel(entrance, after.PlayerSlots);
        Assert.Equal(14, session.SessionId);
        Assert.Equal(2, session.LevelEntryCount);
        Assert.Equal(0, after.WorldTick);
        Assert.Equal(entrance, session.CurrentLevel);
        Assert.Equal(RouteLevelGym.EastOne, after.GetPlayerSlot(1).SpawnId);
    }

    [Fact]
    public void SessionRosterKeepsPlayerIdentityAcrossInputReassignment()
    {
        var session = new GameplaySession(1, Members);
        WorldSession first = MakeWorld(GameLevelIds.RouteA, "START");
        first.AssignInputSource(1, 77);
        session.EnterLevel(new SceneDestination(GameLevelIds.RouteA), first.PlayerSlots);
        var roster = session.PreviewRoster(first.PlayerSlots, 77);
        Assert.Equal(77, roster.Single(x => x.PlayerId == 1).InputSourceId);
        Assert.Equal(1001, roster.Single(x => x.PlayerId == 1).CharacterId);
        Assert.Equal(22, roster.Single(x => x.PlayerId == 2).InputSourceId);
    }

    [Fact]
    public void SessionRejectsInvalidAndDuplicateOwnership()
    {
        Assert.Throws<ArgumentException>(() => new GameplaySession(3, []));
        Assert.Throws<ArgumentException>(() => new GameplaySession(3,
            [new GameSessionParticipant(1, 1001, 1),
             new GameSessionParticipant(2, 1002, 1)]));
        Assert.Throws<ArgumentException>(() => new GameplaySession(3,
            [new GameSessionParticipant(1, 1001, 1),
             new GameSessionParticipant(2, 1001, 2)]));
    }

    [Fact]
    public void FailedSessionParticipantUpdateLeavesOldRosterUntouched()
    {
        var session = new GameplaySession(19, Members);
        Assert.Throws<ArgumentException>(() => session.UpdateParticipants(
            [new GameSessionParticipant(2, 1001, 2),
             new GameSessionParticipant(1, 1001, 1)]));
        Assert.Equal(2, session.Participants.Count);
        Assert.Equal(1002, session.Participants.Single(x => x.PlayerId == 2).CharacterId);
    }

    private sealed class DisposableProbe : IDisposable
    {
        public int Disposes { get; private set; }
        public void Dispose() => Disposes++;
    }

    [Fact]
    public void LevelResourceScopeDisposesOnlyOwnedResourcesExactlyOnce()
    {
        var scope = new SceneResourceScope();
        var a = scope.Own(new DisposableProbe());
        var b = scope.Own(new DisposableProbe());
        scope.Dispose();
        scope.Dispose();
        Assert.True(scope.IsDisposed);
        Assert.Equal(1, a.Disposes);
        Assert.Equal(1, b.Disposes);
        Assert.Throws<ObjectDisposedException>(() => scope.Own(new DisposableProbe()));
    }

    private sealed class FlowScene(MajorSceneKind kind, SceneDestination? dest) : IGameFlowScene
    {
        public MajorSceneKind Kind => kind;
        public SceneDestination? Destination => dest;
        public void Enter() { }
        public void Exit() { }
        public void Dispose() { }
    }

    private sealed class FlowFactory : IGameFlowSceneFactory
    {
        public bool FailNext { get; set; }
        public bool Supports(MajorSceneKind kind, SceneDestination? destination) =>
            kind == MajorSceneKind.Title && destination is null ||
            kind == MajorSceneKind.Gameplay && destination is not null &&
            RouteLevelGym.Catalog.Supports(destination);
        public bool TryPrepare(MajorSceneKind kind, SceneDestination? destination,
            out IGameFlowScene? scene, out string? error)
        {
            scene = null;
            if (FailNext)
            {
                FailNext = false;
                error = "preparation intentionally rejected";
                return false;
            }
            if (!Supports(kind, destination))
            {
                error = "unknown entrance";
                return false;
            }
            scene = new FlowScene(kind, destination);
            error = null;
            return true;
        }
    }

    [Fact]
    public void ChangeLevelIsForbiddenFromTitleAndDoesNotCommitInsideRequest()
    {
        using var flow = new GameFlowController(new FlowFactory());
        Assert.Equal(SceneQueueResult.Invalid, flow.RequestChangeLevel(
            GameLevelIds.RouteB, RouteLevelGym.WestEntrance));
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(GameLevelIds.RouteA));
        Assert.True(flow.CommitPending());
        var old = flow.Scenes.ActiveDestination;
        Assert.Equal(SceneQueueResult.Queued, flow.RequestChangeLevel(
            GameLevelIds.RouteB, RouteLevelGym.EastEntrance));
        Assert.Equal(old, flow.Scenes.ActiveDestination);
        Assert.Equal(SceneQueueResult.Duplicate, flow.RequestChangeLevel(
            GameLevelIds.RouteB, RouteLevelGym.EastEntrance));
        Assert.Equal(SceneQueueResult.Busy, flow.RequestChangeLevel(
            GameLevelIds.RouteB, RouteLevelGym.WestEntrance));
        Assert.True(flow.CommitPending());
        Assert.Equal(new SceneDestination(GameLevelIds.RouteB, RouteLevelGym.EastEntrance),
            flow.Scenes.ActiveDestination);
    }

    [Fact]
    public void InvalidOrFailedLevelChangePreservesCurrentSceneAndClearsBusyGate()
    {
        var factory = new FlowFactory();
        using var flow = new GameFlowController(factory);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(GameLevelIds.RouteA));
        Assert.True(flow.CommitPending());
        Assert.Equal(SceneQueueResult.Invalid, flow.RequestChangeLevel(
            GameLevelIds.RouteB, "BAD"));
        Assert.Null(flow.Scenes.Pending);
        factory.FailNext = true;
        Assert.Equal(SceneQueueResult.Queued, flow.RequestChangeLevel(
            GameLevelIds.RouteB, RouteLevelGym.WestEntrance));
        Assert.False(flow.CommitPending());
        Assert.Equal(new SceneDestination(GameLevelIds.RouteA),
            flow.Scenes.ActiveDestination);
        Assert.Null(flow.Scenes.Pending);
        Assert.NotNull(flow.LastFailure);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestChangeLevel(
            GameLevelIds.RouteB, RouteLevelGym.WestEntrance));
        Assert.True(flow.CommitPending());
    }

    [Fact]
    public void PauseAndReturnToTitleKeepTheirExistingSemantics()
    {
        using var flow = new GameFlowController(new FlowFactory());
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(GameLevelIds.RouteA));
        Assert.True(flow.CommitPending());
        flow.PauseOrResume();
        Assert.True(flow.IsGameplaySuspended);
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestTitle());
        Assert.True(flow.CommitPending());
        Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
        Assert.Equal(0, flow.Ui.OverlayDepth);
    }
}
