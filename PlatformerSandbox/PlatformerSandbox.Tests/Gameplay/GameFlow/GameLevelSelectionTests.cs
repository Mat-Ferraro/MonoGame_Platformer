using System;
using System.Collections.Generic;
using System.Linq;
using PlatformerSandbox.Gameplay.GameFlow;
using Xunit;

namespace PlatformerSandbox.Tests.Gameplay.GameFlow;

public sealed class GameLevelSelectionTests
{
    private sealed class TestScene(MajorSceneKind kind, SceneDestination? destination) : IGameFlowScene
    {
        public MajorSceneKind Kind => kind;
        public SceneDestination? Destination => destination;
        public void Enter() { }
        public void Exit() { }
        public void Dispose() { }
    }

    private sealed class TestFactory : IGameFlowSceneFactory
    {
        public bool FailNextPreparation { get; set; }

        public bool Supports(MajorSceneKind kind, SceneDestination? destination) =>
            kind == MajorSceneKind.Title && destination is null ||
            kind == MajorSceneKind.Gameplay && destination is not null &&
            GameLevelMenuCatalog.Items.Any(item => item.Destination == destination);

        public bool TryPrepare(MajorSceneKind kind, SceneDestination? destination,
            out IGameFlowScene? scene, out string? error)
        {
            if (!Supports(kind, destination))
            {
                scene = null;
                error = "Unsupported destination";
                return false;
            }
            if (FailNextPreparation)
            {
                FailNextPreparation = false;
                scene = null;
                error = "Simulated load failure";
                return false;
            }
            scene = new TestScene(kind, destination);
            error = null;
            return true;
        }
    }

    [Fact]
    public void TitleOpensLevelSelectionAndBackReturnsToTitle()
    {
        using var flow = new GameFlowController(new TestFactory());
        Assert.Equal(GameUiScreen.Title, flow.CurrentMenu);
        Assert.Equal(GameUiCommand.OpenLevelSelect, flow.MenuItems[0].Command);
        flow.ConfirmSelection();
        Assert.Equal(GameUiScreen.LevelSelect, flow.CurrentMenu);
        Assert.True(flow.Ui.HasOverlay);
        flow.Back();
        Assert.Equal(GameUiScreen.Title, flow.CurrentMenu);
        Assert.False(flow.Ui.HasOverlay);
        Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
    }

    [Fact]
    public void LevelMenuContainsAllFourLdtkScenesAndAllKnownGyms()
    {
        using var flow = new GameFlowController(new TestFactory());
        flow.Execute(GameUiCommand.OpenLevelSelect);
        string[] required =
        [
            GameLevelIds.LdtkTest, GameLevelIds.LdtkParallaxDemo,
            GameLevelIds.LdtkSceneryDemo, GameLevelIds.LdtkTerrainDemo,
            GameLevelIds.LdtkConnectedA, GameLevelIds.LdtkConnectedB,
            GameLevelIds.MovementGym, GameLevelIds.SharedWorld,
            GameLevelIds.AuthoredWorld, GameLevelIds.MotionLab,
            GameLevelIds.RouteA, GameLevelIds.RouteB, GameLevelIds.LightingBasics
        ];
        Assert.Equal(required.Length, flow.MenuItems.Count);
        foreach (string level in required)
            Assert.Single(flow.MenuItems, i => i.Destination?.LevelId == level);
        Assert.All(flow.MenuItems, i =>
            Assert.Equal(GameUiCommand.PlaySelectedLevel, i.Command));
    }

    [Fact]
    public void EveryStandaloneMenuDestinationIsAcceptedBySharedSupportRule()
    {
        foreach (GameUiMenuItem entry in GameLevelMenuCatalog.Items)
        {
            SceneDestination destination = Assert.IsType<SceneDestination>(entry.Destination);
            if (destination.LevelId is GameLevelIds.RouteA or GameLevelIds.RouteB)
                continue; // Routes have a separate entrance-aware catalog.
            Assert.Equal(GameLevelIds.DefaultEntrance, destination.EntranceId);
            Assert.True(GameLevelIds.IsStandalonePlayableLevel(destination.LevelId));
        }
        Assert.True(GameLevelIds.IsStandalonePlayableLevel(GameLevelIds.LdtkTerrainDemo));
        Assert.False(GameLevelIds.IsStandalonePlayableLevel("NON_EXISTENT"));
    }

    [Fact]
    public void SelectingTerrainDemoQueuesItAndEntersGameplayOnlyAtCommit()
    {
        using var flow = new GameFlowController(new TestFactory());
        flow.Execute(GameUiCommand.OpenLevelSelect);
        int index = GameLevelMenuCatalog.Items.ToList().FindIndex(i =>
            i.Destination?.LevelId == GameLevelIds.LdtkTerrainDemo);
        Assert.True(index >= 0);
        for (int n = 0; n < index; n++) flow.Navigate(1);
        Assert.Equal(index, flow.SelectionIndex);
        flow.ConfirmSelection();
        Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
        Assert.Equal(GameLevelIds.LdtkTerrainDemo, flow.Scenes.Pending?.Destination?.LevelId);
        Assert.True(flow.CommitPending());
        Assert.Equal(MajorSceneKind.Gameplay, flow.ActiveScene);
        Assert.Equal(GameLevelIds.LdtkTerrainDemo, flow.Scenes.ActiveDestination?.LevelId);
        Assert.False(flow.Ui.HasOverlay);
    }

    [Fact]
    public void SelectingRouteBRetainsEastEntrance()
    {
        using var flow = new GameFlowController(new TestFactory());
        flow.Execute(GameUiCommand.OpenLevelSelect);
        int index = GameLevelMenuCatalog.Items.ToList().FindIndex(i =>
            i.Destination?.LevelId == GameLevelIds.RouteB);
        Assert.True(index >= 0);
        for (int n = 0; n < index; n++) flow.Navigate(1);
        flow.ConfirmSelection();
        Assert.Equal("EAST", flow.Scenes.Pending?.Destination?.EntranceId);
        Assert.True(flow.CommitPending());
        Assert.Equal(GameLevelIds.RouteB, flow.Scenes.ActiveDestination?.LevelId);
    }

    [Fact]
    public void FailedLevelPreparationLeavesTitleAndSelectionUsableForRetry()
    {
        var factory = new TestFactory();
        using var flow = new GameFlowController(factory);
        flow.Execute(GameUiCommand.OpenLevelSelect);
        factory.FailNextPreparation = true;
        flow.ConfirmSelection();
        Assert.False(flow.CommitPending());
        Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
        Assert.Equal(GameUiScreen.LevelSelect, flow.CurrentMenu);
        Assert.Contains("Simulated", flow.LastFailure);
        flow.ConfirmSelection();
        Assert.True(flow.CommitPending());
        Assert.Equal(MajorSceneKind.Gameplay, flow.ActiveScene);
    }

    [Fact]
    public void PauseCanOpenLevelSelectionAndBackRestoresPausedMenu()
    {
        using var flow = new GameFlowController(new TestFactory());
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(GameLevelIds.LdtkTest));
        Assert.True(flow.CommitPending());
        flow.PauseOrResume();
        Assert.True(flow.IsGameplaySuspended);
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        flow.Execute(GameUiCommand.OpenLevelSelect);
        Assert.Equal(GameUiScreen.LevelSelect, flow.CurrentMenu);
        flow.Back();
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        Assert.True(flow.IsGameplaySuspended);
    }

    [Fact]
    public void SelectingFromPauseChangesWorldOnlyAtCommitAndClosesAllOverlays()
    {
        using var flow = new GameFlowController(new TestFactory());
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(GameLevelIds.LdtkTest));
        Assert.True(flow.CommitPending());
        flow.PauseOrResume();
        flow.Execute(GameUiCommand.OpenLevelSelect);
        flow.Navigate(1); // LDTK_PARALLAX_DEMO
        flow.ConfirmSelection();
        Assert.Equal(GameLevelIds.LdtkTest, flow.Scenes.ActiveDestination?.LevelId);
        Assert.True(flow.IsGameplaySuspended);
        Assert.True(flow.CommitPending());
        Assert.Equal(GameLevelIds.LdtkParallaxDemo, flow.Scenes.ActiveDestination?.LevelId);
        Assert.Null(flow.CurrentMenu);
        Assert.False(flow.IsGameplaySuspended);
    }

    [Fact]
    public void LevelSelectionWrapsThroughCompleteCatalog()
    {
        using var flow = new GameFlowController(new TestFactory());
        flow.Execute(GameUiCommand.OpenLevelSelect);
        flow.Navigate(-1);
        Assert.Equal(GameLevelMenuCatalog.Items.Count - 1, flow.SelectionIndex);
        flow.Navigate(1);
        Assert.Equal(0, flow.SelectionIndex);
    }
}
