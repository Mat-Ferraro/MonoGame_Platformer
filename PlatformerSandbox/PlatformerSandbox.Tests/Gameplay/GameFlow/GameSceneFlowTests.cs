using System;
using System.Collections.Generic;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;

namespace PlatformerSandbox.Tests.Gameplay.GameFlow;

public sealed class GameSceneFlowTests
{
    private sealed class TestScene(MajorSceneKind kind,
        SceneDestination? destination, List<string> events) : IGameFlowScene
    {
        public MajorSceneKind Kind => kind;
        public SceneDestination? Destination => destination;
        public void Enter() => events.Add($"ENTER:{Kind}:{Destination?.LevelId}");
        public void Exit() => events.Add($"EXIT:{Kind}:{Destination?.LevelId}");
        public void Dispose() => events.Add($"DISPOSE:{Kind}:{Destination?.LevelId}");
    }

    private sealed class TestFactory : IGameFlowSceneFactory
    {
        public List<string> Events { get; } = [];
        public bool FailNext { get; set; }
        public int Prepares { get; private set; }

        public bool Supports(MajorSceneKind kind, SceneDestination? destination) =>
            kind == MajorSceneKind.Title && destination is null ||
            kind == MajorSceneKind.Gameplay && destination is not null &&
            destination.EntranceId == GameLevelIds.DefaultEntrance &&
            destination.LevelId is (GameLevelIds.MovementGym or GameLevelIds.AuthoredWorld
                or GameLevelIds.SharedWorld or GameLevelIds.MotionLab);

        public bool TryPrepare(MajorSceneKind kind, SceneDestination? destination,
            out IGameFlowScene? scene, out string? error)
        {
            Prepares++;
            scene = null;
            if (FailNext)
            {
                FailNext = false;
                error = "Simulated content preparation failure";
                return false;
            }
            if (!Supports(kind, destination))
            {
                error = "Unknown level";
                return false;
            }
            error = null;
            Events.Add($"PREPARE:{kind}:{destination?.LevelId}");
            scene = new TestScene(kind, destination, Events);
            return true;
        }
    }

    private static void Start(GameFlowController flow, string levelId = GameLevelIds.MovementGym)
    {
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(levelId));
        Assert.True(flow.CommitPending());
        Assert.Equal(MajorSceneKind.Gameplay, flow.ActiveScene);
    }

    [Fact]
    public void StartsInTitleAndDoesNotSimulateGameplay()
    {
        using var flow = new GameFlowController(new TestFactory());
        Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
        Assert.True(flow.IsGameplaySuspended);
        Assert.Equal(GameUiScreen.Title, flow.CurrentMenu);
        Assert.Null(flow.Scenes.Pending);
    }

    [Fact]
    public void PlayRequestIsDeferredUntilExplicitCommitBoundary()
    {
        var factory = new TestFactory();
        using var flow = new GameFlowController(factory);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(GameLevelIds.SharedWorld));
        Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
        Assert.Equal(1, factory.Prepares); // Only Title has been prepared.
        Assert.True(flow.CommitPending());
        Assert.Equal(MajorSceneKind.Gameplay, flow.ActiveScene);
        Assert.Equal(GameLevelIds.SharedWorld, flow.Scenes.ActiveDestination?.LevelId);
        Assert.Equal(2, factory.Prepares);
        Assert.Equal(1, flow.Scenes.CommitCount);
    }

    [Fact]
    public void FactoryPreparesBeforeExitingOldScene()
    {
        var factory = new TestFactory();
        using var flow = new GameFlowController(factory);
        Start(flow, GameLevelIds.AuthoredWorld);
        int prepareIndex = factory.Events.FindIndex(s => s == "PREPARE:Gameplay:AUTHORED_WORLD");
        int titleExitIndex = factory.Events.FindIndex(s => s == "EXIT:Title:");
        int gameplayEnterIndex = factory.Events.FindIndex(s => s == "ENTER:Gameplay:AUTHORED_WORLD");
        Assert.True(prepareIndex >= 0);
        Assert.True(prepareIndex < titleExitIndex);
        Assert.True(titleExitIndex < gameplayEnterIndex);
    }

    [Fact]
    public void DuplicateRequestCoalescesAndDifferentRequestIsBusy()
    {
        var factory = new TestFactory();
        using var flow = new GameFlowController(factory);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(GameLevelIds.MovementGym));
        Assert.Equal(SceneQueueResult.Duplicate, flow.RequestPlay(GameLevelIds.MovementGym));
        Assert.Equal(SceneQueueResult.Busy, flow.RequestPlay(GameLevelIds.MotionLab));
        Assert.True(flow.CommitPending());
        Assert.Equal(2, factory.Prepares);
        Assert.Equal(1, flow.Scenes.CommitCount);
    }

    [Fact]
    public void MissingLevelAndInvalidEntranceAreRejectedWithoutChangingScene()
    {
        using var flow = new GameFlowController(new TestFactory());
        Assert.Equal(SceneQueueResult.Invalid, flow.RequestPlay("MISSING_LEVEL"));
        Assert.Equal(SceneQueueResult.Invalid, flow.RequestPlay(
            GameLevelIds.SharedWorld, "UNKNOWN_ENTRANCE"));
        Assert.Null(flow.Scenes.Pending);
        Assert.False(flow.CommitPending());
        Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
        Assert.NotNull(flow.LastFailure);
    }

    [Fact]
    public void AFailedPreparationPreservesPreviousSceneAndClearsPendingGate()
    {
        var factory = new TestFactory();
        using var flow = new GameFlowController(factory);
        Start(flow);
        factory.FailNext = true;
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(GameLevelIds.MotionLab));
        Assert.False(flow.CommitPending());
        Assert.Equal(GameLevelIds.MovementGym, flow.Scenes.ActiveDestination?.LevelId);
        Assert.Equal(MajorSceneKind.Gameplay, flow.ActiveScene);
        Assert.Contains("preparation", flow.LastFailure!.ToLowerInvariant());
        Assert.Null(flow.Scenes.Pending);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(GameLevelIds.MotionLab));
        Assert.True(flow.CommitPending());
    }

    [Fact]
    public void RestartIsDisallowedAtTitleAndRecreatesSameLevelInGameplay()
    {
        var factory = new TestFactory();
        using var flow = new GameFlowController(factory);
        Assert.Equal(SceneQueueResult.Invalid, flow.RequestRestart());
        Start(flow, GameLevelIds.MotionLab);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestRestart());
        Assert.Equal(GameLevelIds.MotionLab, flow.Scenes.ActiveDestination?.LevelId);
        Assert.True(flow.CommitPending());
        Assert.Equal(GameLevelIds.MotionLab, flow.Scenes.ActiveDestination?.LevelId);
        Assert.Equal(2, flow.Scenes.CommitCount);
        Assert.Equal(3, factory.Prepares);
    }

    [Fact]
    public void ReturnToTitleDisposesGameplayAndReentersCleanly()
    {
        var factory = new TestFactory();
        using var flow = new GameFlowController(factory);
        for (int i = 0; i < 3; i++)
        {
            Start(flow);
            Assert.Equal(SceneQueueResult.Queued, flow.RequestTitle());
            Assert.True(flow.CommitPending());
            Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
            Assert.Equal(GameUiScreen.Title, flow.CurrentMenu);
        }
        Assert.Equal(6, flow.Scenes.CommitCount);
        Assert.Equal(3, factory.Events.FindAll(s => s == "DISPOSE:Gameplay:MOVEMENT_GYM").Count);
    }

    [Fact]
    public void PauseFreezesWorldTickWhileUiCanStillNavigate()
    {
        using var flow = new GameFlowController(new TestFactory());
        Start(flow, GameLevelIds.SharedWorld);
        WorldSession world = SharedWorldClockGym.Create();
        for (int i = 0; i < 10; i++)
            if (!flow.IsGameplaySuspended) world.Step([]);
        long before = world.WorldTick;
        flow.PauseOrResume();
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        for (int i = 0; i < 150; i++)
            if (!flow.IsGameplaySuspended) world.Step([]);
        Assert.Equal(before, world.WorldTick);
        flow.Navigate(1);
        flow.Navigate(1);
        flow.ConfirmSelection(); // Settings, not a gameplay tick.
        Assert.Equal(GameUiScreen.Settings, flow.CurrentMenu);
        Assert.Equal(before, world.WorldTick);
        flow.PauseOrResume();
        Assert.False(flow.IsGameplaySuspended);
        world.Step([]);
        Assert.Equal(before + 1, world.WorldTick);
    }

    [Fact]
    public void SettingsReturnToOriginalCallerFromTitleAndPause()
    {
        using var flow = new GameFlowController(new TestFactory());
        flow.Execute(GameUiCommand.Settings);
        Assert.Equal(GameUiScreen.Settings, flow.CurrentMenu);
        flow.Back();
        Assert.Equal(GameUiScreen.Title, flow.CurrentMenu);
        Start(flow);
        flow.PauseOrResume();
        flow.Execute(GameUiCommand.Settings);
        Assert.Equal(GameUiScreen.Settings, flow.CurrentMenu);
        flow.Back();
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        Assert.True(flow.IsGameplaySuspended);
    }

    [Fact]
    public void ControlsBackRestoresSettingsFocusThenPauseSelection()
    {
        using var flow = new GameFlowController(new TestFactory());
        Start(flow);
        flow.PauseOrResume();
        flow.Navigate(1); // restart
        flow.Navigate(1); // settings
        int selection = flow.SelectionIndex;
        flow.ConfirmSelection();
        Assert.Equal(GameUiScreen.Settings, flow.CurrentMenu);
        flow.Navigate(1); // controls
        flow.ConfirmSelection();
        Assert.Equal(GameUiScreen.Controls, flow.CurrentMenu);
        flow.Back();
        Assert.Equal(GameUiScreen.Settings, flow.CurrentMenu);
        Assert.Equal(1, flow.SelectionIndex);
        flow.Back();
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        Assert.Equal(selection, flow.SelectionIndex);
    }

    [Fact]
    public void PauseFromTitleDoesNotCreateGameplayOverlay()
    {
        using var flow = new GameFlowController(new TestFactory());
        flow.PauseOrResume();
        Assert.Equal(GameUiScreen.Title, flow.CurrentMenu);
        Assert.Equal(0, flow.Ui.OverlayDepth);
    }

    [Fact]
    public void ReturningToTitleClearsNestedSettingsAndPauseStack()
    {
        using var flow = new GameFlowController(new TestFactory());
        Start(flow);
        flow.PauseOrResume();
        flow.Execute(GameUiCommand.Settings);
        Assert.Equal(2, flow.Ui.OverlayDepth);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestTitle());
        Assert.True(flow.CommitPending());
        Assert.Equal(GameUiScreen.Title, flow.CurrentMenu);
        Assert.Equal(0, flow.Ui.OverlayDepth);
    }

    [Fact]
    public void BackAtGameplayOpensPauseAndBackAgainResumes()
    {
        using var flow = new GameFlowController(new TestFactory());
        Start(flow);
        flow.Back();
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        flow.Back();
        Assert.Null(flow.CurrentMenu);
    }

    [Fact]
    public void FlowHintSettingSurvivesSceneReplacement()
    {
        using var flow = new GameFlowController(new TestFactory());
        flow.Execute(GameUiCommand.Settings);
        flow.Execute(GameUiCommand.ToggleHints);
        Assert.False(flow.ShowFlowHints);
        flow.Back();
        Start(flow);
        flow.PauseOrResume();
        Assert.False(flow.ShowFlowHints);
        Assert.True(flow.CommitPending() == false);
    }

    [Fact]
    public void TitleMenuOffersMaintainedScenesWithoutLegacyGymClutter()
    {
        using var flow = new GameFlowController(new TestFactory());
        Assert.Contains(flow.MenuItems, x => x.Command == GameUiCommand.OpenLevelSelect);
        flow.Execute(GameUiCommand.OpenLevelSelect);
        Assert.Equal(GameUiScreen.LevelSelect, flow.CurrentMenu);
        Assert.Contains(flow.MenuItems, x => x.Destination?.LevelId == GameLevelIds.MovementGym);
        Assert.DoesNotContain(flow.MenuItems, x => GameLevelMenuCatalog.IsLegacy(x));
        Assert.DoesNotContain(flow.MenuItems, x => x.Destination?.LevelId == GameLevelIds.SharedWorld);
        Assert.DoesNotContain(flow.MenuItems, x => x.Destination?.LevelId == GameLevelIds.MotionLab);
    }

    [Fact]
    public void QuitIsOnlyAcceptedFromTitle()
    {
        using var flow = new GameFlowController(new TestFactory());
        Start(flow);
        flow.Execute(GameUiCommand.Quit);
        Assert.False(flow.ExitRequested);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestTitle());
        Assert.True(flow.CommitPending());
        flow.Execute(GameUiCommand.Quit);
        Assert.True(flow.ExitRequested);
    }

    [Fact]
    public void NewSceneDestinationRejectsEmptyIDs()
    {
        Assert.Throws<ArgumentException>(() => new SceneDestination(""));
        Assert.Throws<ArgumentException>(() => new SceneDestination("L", " "));
    }
}
