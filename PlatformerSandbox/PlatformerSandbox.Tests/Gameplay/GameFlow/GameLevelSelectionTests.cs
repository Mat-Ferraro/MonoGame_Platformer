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
            destination.EntranceId == GameLevelIds.DefaultEntrance &&
            GameLevelIds.IsStandalonePlayableLevel(destination.LevelId);

        // The UI catalog is intentionally only a shortlist. Valid standalone
        // fixtures must remain loadable without being displayed in that menu.

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
    public void LevelMenuShowsMaintainedDestinationsOnly()
    {
        using var flow = new GameFlowController(new TestFactory());
        flow.Execute(GameUiCommand.OpenLevelSelect);
        string[] required =
        [
            GameLevelIds.LdtkConnectedA,
            GameLevelMenuCatalog.LightingIntegrationTest,
            GameLevelIds.MaterialShowcaseTest,
            GameLevelIds.LightTypesLab,
            GameLevelIds.LightingBasics,
            GameLevelIds.MovementGym
        ];
        Assert.Equal(required, flow.MenuItems.Select(x => x.Destination!.LevelId));
        Assert.DoesNotContain(flow.MenuItems,
            x => x.Destination?.LevelId == GameLevelIds.LdtkParallaxDemo);
        Assert.All(flow.MenuItems, x =>
            Assert.Equal(GameUiCommand.PlaySelectedLevel, x.Command));
    }

    [Fact]
    public void DiscoveredLevelsAreWhitelistedAndMissingProjectsAreHidden()
    {
        var menu = GameLevelMenuCatalog.WithDiscoveredLevels(
        [
            GameLevelMenuCatalog.LightingIntegrationTest,
            "EXPERIMENT_THAT_MUST_STAY_HIDDEN"
        ]);
        Assert.DoesNotContain(menu, x => x.Destination?.LevelId == "EXPERIMENT_THAT_MUST_STAY_HIDDEN");
        Assert.DoesNotContain(menu, x => x.Destination?.LevelId == GameLevelIds.LdtkConnectedA);
        Assert.Contains(menu, x => x.Destination?.LevelId == GameLevelMenuCatalog.LightingIntegrationTest);
        Assert.Contains(menu, x => x.Destination?.LevelId == GameLevelIds.LightingBasics);
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
    public void HiddenRegressionLevelCanLaunchWithoutAppearingInTheLevelMenu()
    {
        Assert.DoesNotContain(GameLevelMenuCatalog.Items,
            item => item.Destination?.LevelId == GameLevelIds.LdtkTest);

        using var flow = new GameFlowController(new TestFactory());
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(GameLevelIds.LdtkTest));
        Assert.True(flow.CommitPending());
        Assert.Equal(MajorSceneKind.Gameplay, flow.ActiveScene);
        Assert.Equal(GameLevelIds.LdtkTest, flow.Scenes.ActiveDestination?.LevelId);
    }

    [Fact]
    public void HiddenRegressionLevelDoesNotAllowUnknownEntrances()
    {
        using var flow = new GameFlowController(new TestFactory());
        Assert.Equal(SceneQueueResult.Invalid,
            flow.RequestPlay(GameLevelIds.LdtkTest, "UNKNOWN_ENTRANCE"));
        Assert.Null(flow.Scenes.Pending);
    }

    [Fact]
    public void SelectingLightingIntegrationQueuesItAndEntersGameplayOnlyAtCommit()
    {
        using var flow = new GameFlowController(new TestFactory());
        flow.Execute(GameUiCommand.OpenLevelSelect);
        int index = GameLevelMenuCatalog.Items.ToList().FindIndex(x =>
            x.Destination?.LevelId == GameLevelMenuCatalog.LightingIntegrationTest);
        Assert.True(index >= 0);
        for (int n = 0; n < index; n++) flow.Navigate(1);
        flow.ConfirmSelection();
        Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
        Assert.Equal(GameLevelMenuCatalog.LightingIntegrationTest,
            flow.Scenes.Pending?.Destination?.LevelId);
        Assert.True(flow.CommitPending());
        Assert.Equal(GameLevelMenuCatalog.LightingIntegrationTest,
            flow.Scenes.ActiveDestination?.LevelId);
    }

    [Fact]
    public void LevelMenuHitTestingTracksVisibleRows()
    {
        const int width = 1280, height = 720;
        var b = GameMenuLayout.Get(width, height, GameUiScreen.LevelSelect,
            4, 0);
        Assert.Equal(0, GameMenuLayout.Hit(width, height,
            GameUiScreen.LevelSelect, 4, 0, b.Left + 25, b.Top + 68));
        Assert.Equal(1, GameMenuLayout.Hit(width, height,
            GameUiScreen.LevelSelect, 4, 0, b.Left + 25, b.Top + 98));
        Assert.Equal(-1, GameMenuLayout.Hit(width, height,
            GameUiScreen.LevelSelect, 4, 0, 0, 0));
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
        flow.Navigate(1); // LIGHTING_INTEGRATION_TEST
        flow.ConfirmSelection();
        Assert.Equal(GameLevelIds.LdtkTest, flow.Scenes.ActiveDestination?.LevelId);
        Assert.True(flow.IsGameplaySuspended);
        Assert.True(flow.CommitPending());
        Assert.Equal(GameLevelMenuCatalog.LightingIntegrationTest, flow.Scenes.ActiveDestination?.LevelId);
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
