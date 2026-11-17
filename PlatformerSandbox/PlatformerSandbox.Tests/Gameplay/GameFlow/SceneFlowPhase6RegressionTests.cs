using System;
using System.Collections.Generic;
using PlatformerSandbox.Gameplay.GameFlow;

namespace PlatformerSandbox.Tests.Gameplay.GameFlow;

/// <summary>
/// Phase 6 lifecycle/load/focus/stress regressions. All tests run headlessly;
/// the separate F9 Route Lab smoke uses the same production coordinator.
/// </summary>
public sealed class SceneFlowPhase6RegressionTests
{
    private sealed class TestScene(MajorSceneKind kind, SceneDestination? destination,
        Registry registry) : IGameFlowScene
    {
        private bool _disposed;
        public MajorSceneKind Kind { get; } = kind;
        public SceneDestination? Destination { get; } = destination;
        public void Enter()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TestScene));
            registry.Entered++;
        }
        public void Exit() => registry.Exited++;
        public void Dispose()
        {
            if (_disposed) { registry.DoubleDisposals++; return; }
            _disposed = true;
            registry.Disposed++;
        }
    }

    private sealed class Registry : IGameFlowSceneFactory
    {
        public int Created { get; private set; }
        public int Entered;
        public int Exited;
        public int Disposed;
        public int DoubleDisposals;
        public bool FailNext;
        public bool WrongNext;

        public bool Supports(MajorSceneKind kind, SceneDestination? destination) =>
            kind == MajorSceneKind.Title && destination is null ||
            kind == MajorSceneKind.Gameplay && destination is not null &&
            destination.LevelId is (GameLevelIds.RouteA or GameLevelIds.RouteB or GameLevelIds.MotionLab) &&
            destination.EntranceId is ("START" or "WEST" or "EAST");

        public bool TryPrepare(MajorSceneKind kind, SceneDestination? destination,
            out IGameFlowScene? scene, out string? error)
        {
            if (FailNext)
            {
                FailNext = false;
                scene = null;
                error = "SIMULATED LOAD FAILURE";
                return false;
            }
            if (!Supports(kind, destination))
            {
                scene = null;
                error = "INVALID";
                return false;
            }
            Created++;
            SceneDestination? prepared = WrongNext ? new SceneDestination(GameLevelIds.MotionLab) : destination;
            WrongNext = false;
            scene = new TestScene(kind, prepared, this);
            error = null;
            return true;
        }
    }

    private static bool Complete(GameFlowController flow)
    {
        Assert.True(flow.IsTransitionActive);
        Assert.False(flow.AdvanceTransition(0.12));
        Assert.Equal(SceneCurtainPhase.Covered, flow.Curtain.Phase);
        bool changed = flow.AdvanceTransition(0);
        Assert.False(flow.AdvanceTransition(0.12));
        Assert.False(flow.IsTransitionActive);
        return changed;
    }

    private static void Play(GameFlowController flow, string level = GameLevelIds.RouteA,
        string entrance = "START")
    {
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(level, entrance));
        Assert.True(Complete(flow));
    }

    [Fact]
    public void InvalidPreparedTitleIsDisposedBeforeStartupFails()
    {
        var registry = new Registry { WrongNext = true };
        Assert.Throws<InvalidOperationException>(() => new GameFlowController(registry));
        Assert.Equal(1, registry.Created);
        Assert.Equal(1, registry.Disposed);
        Assert.Equal(0, registry.DoubleDisposals);
    }

    [Fact]
    public void SceneTrackerStartsWithOneAndEndsWithZero()
    {
        var registry = new Registry();
        var flow = new GameFlowController(registry);
        Assert.Equal(1, flow.Scenes.LiveSceneInstances);
        Assert.Equal(1, flow.Scenes.PreparedSceneInstances);
        flow.Dispose();
        Assert.Equal(0, flow.Scenes.LiveSceneInstances);
        Assert.Equal(1, registry.Disposed);
    }

    [Fact]
    public void FortySceneReplacementsLeaveExactlyOneLiveOwner()
    {
        var registry = new Registry();
        using (var flow = new GameFlowController(registry))
        {
            Play(flow);
            for (int i = 0; i < 40; i++)
            {
                string target = i % 2 == 0 ? GameLevelIds.RouteB : GameLevelIds.RouteA;
                Assert.Equal(SceneQueueResult.Queued, flow.RequestChangeLevel(target, "WEST"));
                Assert.True(Complete(flow));
                Assert.Equal(target, flow.Scenes.ActiveDestination?.LevelId);
                Assert.Equal(1, flow.Scenes.LiveSceneInstances);
                Assert.Equal(2, flow.Scenes.PeakLiveSceneInstances);
            }
            Assert.Equal(41, flow.Validation.SuccessfulCommits);
            Assert.Equal(41, flow.Validation.PreparationAttempts);
            Assert.Equal(41, flow.Scenes.CommitCount);
            Assert.Equal(registry.Created - 1, registry.Disposed);
        }
        Assert.Equal(registry.Created, registry.Disposed);
        Assert.Equal(0, registry.DoubleDisposals);
    }

    [Fact]
    public void RepeatedPlayReturnToTitleNeverDuplicatesSceneOwner()
    {
        var registry = new Registry();
        using var flow = new GameFlowController(registry);
        for (int i = 0; i < 18; i++)
        {
            Play(flow);
            Assert.Equal(SceneQueueResult.Queued, flow.RequestTitle());
            Assert.True(Complete(flow));
            Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
            Assert.Equal(1, flow.Scenes.LiveSceneInstances);
        }
        Assert.Equal(36, flow.Validation.SuccessfulCommits);
    }

    [Fact]
    public void FailedLoadRetainsSceneAndDoesNotCreateNewOwner()
    {
        var registry = new Registry();
        using var flow = new GameFlowController(registry);
        Play(flow);
        long initialCommit = flow.Scenes.CommitCount;
        int initialDisposal = registry.Disposed;
        registry.FailNext = true;
        Assert.Equal(SceneQueueResult.Queued,
            flow.RequestChangeLevel(GameLevelIds.RouteB, "EAST"));
        Assert.False(Complete(flow));
        Assert.Equal(GameLevelIds.RouteA, flow.Scenes.ActiveDestination?.LevelId);
        Assert.Equal(initialCommit, flow.Scenes.CommitCount);
        Assert.Equal(initialDisposal, registry.Disposed);
        Assert.Equal(1, flow.Scenes.LiveSceneInstances);
        Assert.NotNull(flow.LastFailure);
        Assert.Equal(1, flow.Validation.FailedPreparations);
        Assert.False(flow.IsGameplaySuspended);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestRestart());
        Assert.True(Complete(flow));
    }

    [Fact]
    public void MismatchedPreparedSceneIsDisposedWithoutKillingOldScene()
    {
        var registry = new Registry();
        using var flow = new GameFlowController(registry);
        Play(flow);
        registry.WrongNext = true;
        int disposedBefore = registry.Disposed;
        Assert.Equal(SceneQueueResult.Queued,
            flow.RequestChangeLevel(GameLevelIds.RouteB, "WEST"));
        Assert.False(Complete(flow));
        Assert.Equal(disposedBefore + 1, registry.Disposed);
        Assert.Equal(GameLevelIds.RouteA, flow.Scenes.ActiveDestination?.LevelId);
        Assert.Equal(1, flow.Scenes.LiveSceneInstances);
    }

    [Fact]
    public void InvalidDestinationNeverBeginsCurtainOrPreparesScene()
    {
        var registry = new Registry();
        using var flow = new GameFlowController(registry);
        Play(flow);
        int created = registry.Created;
        Assert.Equal(SceneQueueResult.Invalid, flow.RequestChangeLevel("MISSING", "WEST"));
        Assert.False(flow.IsTransitionActive);
        Assert.Equal(created, registry.Created);
        Assert.Equal(1, flow.Validation.Invalid);
        Assert.Equal(0, flow.Validation.FailedPreparations);
    }

    [Fact]
    public void DuplicateAndBusyRequestCountsAreVisibleButDoNotPrepare()
    {
        using var flow = new GameFlowController(new Registry());
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(GameLevelIds.RouteA));
        Assert.Equal(SceneQueueResult.Duplicate, flow.RequestPlay(GameLevelIds.RouteA));
        Assert.Equal(SceneQueueResult.Busy, flow.RequestPlay(GameLevelIds.RouteB));
        Assert.Equal(1, flow.Validation.Queued);
        Assert.Equal(1, flow.Validation.Duplicates);
        Assert.Equal(1, flow.Validation.Busy);
        Assert.True(Complete(flow));
        Assert.Equal(1, flow.Validation.PreparationAttempts);
    }

    [Fact]
    public void RequestsDuringFadeInRemainGated()
    {
        using var flow = new GameFlowController(new Registry());
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(GameLevelIds.RouteA));
        Assert.False(flow.AdvanceTransition(0.12));
        Assert.True(flow.AdvanceTransition(0));
        Assert.Equal(SceneCurtainPhase.FadingIn, flow.Curtain.Phase);
        Assert.Equal(SceneQueueResult.Busy, flow.RequestChangeLevel(GameLevelIds.RouteB, "WEST"));
        Assert.False(flow.AdvanceTransition(0.12));
        Assert.Equal(SceneQueueResult.Queued, flow.RequestRestart());
        Assert.True(Complete(flow));
    }

    [Fact]
    public void ManyRapidUiRequestsNeverTriggerConcurrentPreparation()
    {
        using var flow = new GameFlowController(new Registry());
        for (int i = 0; i < 35; i++)
        {
            Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(GameLevelIds.RouteA));
            for (int j = 0; j < 10; j++)
                Assert.Equal(SceneQueueResult.Duplicate, flow.RequestPlay(GameLevelIds.RouteA));
            Assert.True(Complete(flow));
            Assert.Equal(SceneQueueResult.Queued, flow.RequestTitle());
            Assert.True(Complete(flow));
        }
        Assert.Equal(70, flow.Validation.SuccessfulCommits);
        Assert.Equal(350, flow.Validation.Duplicates);
        Assert.Equal(70, flow.Validation.PreparationAttempts);
    }

    [Fact]
    public void PauseAndNestedOverlaysFreezeGameplayAndPreserveFocus()
    {
        using var flow = new GameFlowController(new Registry());
        Play(flow);
        flow.PauseOrResume();
        Assert.True(flow.IsGameplaySuspended);
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        flow.Navigate(1);
        int focusBefore = flow.SelectionIndex;
        flow.Execute(GameUiCommand.Settings);
        Assert.Equal(GameUiScreen.Settings, flow.CurrentMenu);
        flow.Execute(GameUiCommand.Controls);
        Assert.Equal(GameUiScreen.Controls, flow.CurrentMenu);
        flow.Back();
        Assert.Equal(GameUiScreen.Settings, flow.CurrentMenu);
        flow.Back();
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        Assert.Equal(focusBefore, flow.SelectionIndex);
        flow.PauseOrResume();
        Assert.False(flow.IsGameplaySuspended);
    }

    [Fact]
    public void RequestWhilePausedKeepsWorldGatedUntilNewSceneActive()
    {
        using var flow = new GameFlowController(new Registry());
        Play(flow);
        flow.PauseOrResume();
        Assert.Equal(SceneQueueResult.Queued, flow.RequestRestart());
        Assert.True(flow.IsGameplaySuspended);
        Assert.True(Complete(flow));
        Assert.False(flow.IsGameplaySuspended);
        Assert.False(flow.Ui.HasOverlay);
    }

    [Fact]
    public void FlowMetricsUseBoundedHistoryAndRealElapsedTimes()
    {
        using var flow = new GameFlowController(new Registry());
        for (int i = 0; i < 30; i++)
            Assert.Equal(SceneQueueResult.Invalid, flow.RequestTitle());
        Assert.Equal(16, flow.Validation.RecentEvents.Count);
        Play(flow);
        Assert.True(flow.Validation.LastPreparationMilliseconds >= 0);
        Assert.True(flow.Validation.MaximumPreparationMilliseconds >=
            flow.Validation.LastPreparationMilliseconds);
        Assert.True(flow.Validation.MeanPreparationMilliseconds >= 0);
        Assert.Contains("COMMITTED", flow.Validation.LatestEvent);
    }

    [Fact]
    public void NoPreparationMeasurementsBeforeFirstActualTransition()
    {
        using var flow = new GameFlowController(new Registry());
        Assert.Equal(0, flow.Validation.PreparationAttempts);
        Assert.Equal(0, flow.Validation.MeanPreparationMilliseconds);
        Assert.Equal(0, flow.Validation.MaximumPreparationMilliseconds);
    }

    [Fact]
    public void SmokeRunnerCompletesEightRealTransitionsIncludingFailedLoad()
    {
        var registry = new Registry();
        using var flow = new GameFlowController(registry);
        Play(flow);
        var snapshot = new SceneFlowProbeSnapshot(GameLevelIds.RouteA, "START", 11, 1, 21, 70);
        var smoke = new SceneFlowSmokeRunner();
        smoke.Start(snapshot);
        int worldId = 21;
        for (int iteration = 0; iteration < 15 && smoke.IsRunning; iteration++)
        {
            SceneFlowSmokeCommand? action = smoke.Advance(snapshot, flow.IsTransitionActive,
                flow.Curtain.LastSucceeded);
            if (action is not SceneFlowSmokeCommand command) continue;
            if (command.Operation == SceneFlowSmokeOperation.ExpectedFailure)
                registry.FailNext = true;
            SceneQueueResult accepted = command.Operation == SceneFlowSmokeOperation.Restart
                ? flow.RequestRestart()
                : flow.RequestChangeLevel(command.LevelId, command.EntranceId);
            Assert.Equal(SceneQueueResult.Queued, accepted);
            bool changed = Complete(flow);
            if (changed)
            {
                worldId++;
                snapshot = snapshot with
                {
                    LevelId = flow.Scenes.ActiveDestination!.LevelId,
                    EntranceId = flow.Scenes.ActiveDestination!.EntranceId,
                    LevelEntries = snapshot.LevelEntries + 1,
                    WorldId = worldId,
                    WorldTick = 0
                };
            }
            else
                Assert.Equal(SceneFlowSmokeOperation.ExpectedFailure, command.Operation);
        }
        // The last transition is checked by one final observation.
        smoke.Advance(snapshot, flow.IsTransitionActive, flow.Curtain.LastSucceeded);
        Assert.False(smoke.IsRunning);
        Assert.True(smoke.Passed, smoke.Status);
        Assert.Equal(8, smoke.PassedChecks);
        Assert.Equal(1, flow.Validation.FailedPreparations);
        Assert.Equal(1, flow.Scenes.LiveSceneInstances);
    }

    [Fact]
    public void SmokeRunnerDoesNotGenerateMoreCommandsDuringCurtain()
    {
        var smoke = new SceneFlowSmokeRunner();
        var snapshot = new SceneFlowProbeSnapshot(GameLevelIds.RouteA, "START", 3, 1, 5, 12);
        smoke.Start(snapshot);
        Assert.Null(smoke.Advance(snapshot, true, null));
        Assert.Equal(0, smoke.PassedChecks);
        Assert.NotNull(smoke.Advance(snapshot, false, null));
        Assert.Null(smoke.Advance(snapshot, true, null));
    }

    [Fact]
    public void SmokeRunnerRejectsChangedSession()
    {
        var smoke = new SceneFlowSmokeRunner();
        var original = new SceneFlowProbeSnapshot(GameLevelIds.RouteA, "START", 3, 1, 5, 0);
        smoke.Start(original);
        smoke.Advance(original, false, null);
        var changed = new SceneFlowProbeSnapshot(GameLevelIds.RouteB, "WEST", 4, 2, 6, 0);
        smoke.Advance(changed, false, true);
        Assert.False(smoke.Passed);
        Assert.Contains("Gameplay session", smoke.Status);
    }

    [Fact]
    public void SmokeRunnerRejectsReusedWorldInstance()
    {
        var smoke = new SceneFlowSmokeRunner();
        var original = new SceneFlowProbeSnapshot(GameLevelIds.RouteA, "START", 3, 1, 5, 0);
        smoke.Start(original);
        smoke.Advance(original, false, null);
        smoke.Advance(original with { LevelId = GameLevelIds.RouteB, EntranceId = "WEST",
            LevelEntries = 2 }, false, true);
        Assert.False(smoke.Passed);
        Assert.Contains("invariant", smoke.Status);
    }

    [Fact]
    public void SmokeRunnerRejectsMutatedWorldAfterFailedPreparation()
    {
        var smoke = new SceneFlowSmokeRunner();
        var start = new SceneFlowProbeSnapshot(GameLevelIds.RouteA, "START", 5, 1, 1, 80);
        smoke.Start(start);
        // Complete first change and one restart to reach expected failure.
        smoke.Advance(start, false, null);
        var afterChange = new SceneFlowProbeSnapshot(GameLevelIds.RouteB, "WEST", 5, 2, 2, 0);
        smoke.Advance(afterChange, false, true);
        var afterRestart = afterChange with { LevelEntries = 3, WorldId = 3 };
        smoke.Advance(afterRestart, false, true);
        smoke.Advance(afterRestart with { WorldTick = 1 }, false, false);
        Assert.False(smoke.Passed);
        Assert.Contains("Failed load changed", smoke.Status);
    }

    [Fact]
    public void SmokeRunnerCanBeExplicitlyAbortedAndRestarted()
    {
        var smoke = new SceneFlowSmokeRunner();
        var snapshot = new SceneFlowProbeSnapshot(GameLevelIds.RouteA, "START", 5, 1, 1, 0);
        smoke.Start(snapshot);
        smoke.Abort("User cancelled");
        Assert.False(smoke.IsRunning);
        Assert.Contains("ABORTED", smoke.Status);
        smoke.Start(snapshot);
        Assert.True(smoke.IsRunning);
        Assert.Equal(0, smoke.PassedChecks);
    }

    [Fact]
    public void SmokeRunnerRequiresRealRouteWorldIdentities()
    {
        var smoke = new SceneFlowSmokeRunner();
        Assert.Throws<ArgumentException>(() => smoke.Start(
            new SceneFlowProbeSnapshot("BAD", "START", 1, 1, 1, 0)));
        Assert.Throws<ArgumentException>(() => smoke.Start(
            new SceneFlowProbeSnapshot(GameLevelIds.RouteA, "START", 0, 1, 1, 0)));
    }
}
