using System;
using System.Collections.Generic;
using System.Linq;
using PlatformerSandbox.Gameplay.GameFlow;
using Xunit;

namespace PlatformerSandbox.Tests.Gameplay.GameFlow;

public sealed class LightingBasicsMenuEntryTests
{
    private sealed class Scene(MajorSceneKind kind, SceneDestination? destination) : IGameFlowScene
    {
        public MajorSceneKind Kind => kind;
        public SceneDestination? Destination => destination;
        public void Enter() { }
        public void Exit() { }
        public void Dispose() { }
    }

    private sealed class Factory : IGameFlowSceneFactory
    {
        public bool Supports(MajorSceneKind kind, SceneDestination? destination) =>
            kind == MajorSceneKind.Title && destination is null ||
            kind == MajorSceneKind.Gameplay && destination?.LevelId == GameLevelIds.LightingBasics &&
            destination.EntranceId == GameLevelIds.DefaultEntrance;

        public bool TryPrepare(MajorSceneKind kind, SceneDestination? destination,
            out IGameFlowScene? scene, out string? error)
        {
            error = Supports(kind, destination) ? null : "Unsupported";
            scene = error is null ? new Scene(kind, destination) : null;
            return scene is not null;
        }
    }

    [Fact]
    public void RetiredLightingBasicsRemainsDirectLaunchableAndCanReturnToTitle()
    {
        Assert.True(GameLevelIds.IsStandalonePlayableLevel(GameLevelIds.LightingBasics));
        Assert.DoesNotContain(GameLevelMenuCatalog.Items,item => item.Destination?.LevelId == GameLevelIds.LightingBasics);
        Assert.DoesNotContain(GameLevelMenuCatalog.WithDiscoveredLevels(Array.Empty<string>()),item => item.Destination?.LevelId == GameLevelIds.LightingBasics);
        using var flow = new GameFlowController(new Factory());
        Assert.Equal(SceneQueueResult.Queued,flow.RequestPlay(GameLevelIds.LightingBasics));
        Assert.Equal(GameLevelIds.LightingBasics, flow.Scenes.Pending?.Destination?.LevelId);
        Assert.True(flow.CommitPending());
        Assert.Equal(MajorSceneKind.Gameplay, flow.ActiveScene);
        flow.Back();
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        flow.Execute(GameUiCommand.ReturnToTitle);
        Assert.True(flow.CommitPending());
        Assert.Equal(GameUiScreen.Title, flow.CurrentMenu);
    }
}
