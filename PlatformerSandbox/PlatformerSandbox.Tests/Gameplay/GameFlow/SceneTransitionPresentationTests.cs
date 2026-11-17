using System;
using System.Collections.Generic;
using PlatformerSandbox.Gameplay.GameFlow;

namespace PlatformerSandbox.Tests.Gameplay.GameFlow;

/// <summary>
/// SM Phase 5: deterministic presentation gate/lifecycle checks. The tests
/// use a fake, headless scene factory; only elapsed presentation seconds
/// control the curtain. No graphics device or gameplay physics is needed.
/// </summary>
public sealed class SceneTransitionPresentationTests
{
    private sealed class Scene(MajorSceneKind kind, SceneDestination? destination,
        List<string> events) : IGameFlowScene
    {
        public MajorSceneKind Kind { get; } = kind;
        public SceneDestination? Destination { get; } = destination;
        public void Enter() => events.Add($"enter:{Destination?.LevelId ?? "TITLE"}");
        public void Exit() => events.Add($"exit:{Destination?.LevelId ?? "TITLE"}");
        public void Dispose() => events.Add($"dispose:{Destination?.LevelId ?? "TITLE"}");
    }

    private sealed class Factory : IGameFlowSceneFactory
    {
        public readonly List<string> Events = [];
        public bool FailNext { get; set; }
        public int Preparations { get; private set; }
        public bool Supports(MajorSceneKind kind, SceneDestination? destination) =>
            kind == MajorSceneKind.Title && destination is null ||
            kind == MajorSceneKind.Gameplay && destination is not null &&
            destination.EntranceId is ("START" or "WEST") &&
            destination.LevelId is ("ROUTE_A" or "ROUTE_B" or "MOTION_LAB");
        public bool TryPrepare(MajorSceneKind kind, SceneDestination? destination,
            out IGameFlowScene? scene, out string? error)
        {
            Preparations++;
            if (FailNext)
            {
                FailNext = false;
                scene = null;
                error = "Simulated content failure";
                Events.Add("fail:prepare");
                return false;
            }
            scene = new Scene(kind, destination, Events);
            error = null;
            Events.Add($"prepare:{destination?.LevelId ?? "TITLE"}");
            return true;
        }
    }

    private static void FinishFadeOut(GameFlowController flow)
    {
        Assert.False(flow.AdvanceTransition(0.06));
        Assert.False(flow.AdvanceTransition(0.06));
        Assert.Equal(SceneCurtainPhase.Covered, flow.Curtain.Phase);
        Assert.Equal(1f, flow.Curtain.Opacity);
    }

    private static void Start(GameFlowController flow, string level = "ROUTE_A")
    {
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay(level));
        FinishFadeOut(flow);
        Assert.True(flow.AdvanceTransition(0.016));
        Assert.False(flow.AdvanceTransition(0.12));
        Assert.Equal(SceneCurtainPhase.Idle, flow.Curtain.Phase);
        Assert.False(flow.IsGameplaySuspended);
    }

    [Fact]
    public void CurtainMovesThroughFourPhasesOnPresentationTime()
    {
        var curtain = new SceneTransitionCurtain();
        Assert.Equal(SceneCurtainPhase.Idle, curtain.Phase);
        Assert.False(curtain.IsActive);
        Assert.Equal(0f, curtain.Opacity);
        curtain.Begin();
        Assert.Equal(SceneCurtainPhase.FadingOut, curtain.Phase);
        Assert.False(curtain.Advance(0.03));
        Assert.InRange(curtain.Opacity, 0.24f, 0.26f);
        Assert.False(curtain.Advance(0.09));
        Assert.Equal(SceneCurtainPhase.Covered, curtain.Phase);
        Assert.True(curtain.Advance(0));
        curtain.Complete(true);
        Assert.Equal(SceneCurtainPhase.FadingIn, curtain.Phase);
        Assert.Equal(1f, curtain.Opacity);
        Assert.False(curtain.Advance(0.06));
        Assert.InRange(curtain.Opacity, 0.49f, 0.51f);
        Assert.False(curtain.Advance(0.06));
        Assert.Equal(SceneCurtainPhase.Idle, curtain.Phase);
        Assert.False(curtain.IsActive);
        Assert.Equal(0f, curtain.Opacity);
        Assert.Equal(true, curtain.LastSucceeded);
    }

    [Fact]
    public void CurtainRejectsInvalidTimeAndOverlappingBegins()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SceneTransitionCurtain(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SceneTransitionCurtain(double.NaN));
        var curtain = new SceneTransitionCurtain();
        curtain.Begin();
        Assert.Throws<InvalidOperationException>(() => curtain.Begin());
        Assert.Throws<ArgumentOutOfRangeException>(() => curtain.Advance(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => curtain.Advance(double.PositiveInfinity));
        Assert.Throws<InvalidOperationException>(() => curtain.Complete(true));
    }

    [Fact]
    public void RequestRemainsDeferredUntilCoveredUpdate()
    {
        var factory = new Factory();
        using var flow = new GameFlowController(factory);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay("ROUTE_A"));
        Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
        Assert.Equal(1, factory.Preparations);
        Assert.True(flow.IsTransitionActive);
        Assert.False(flow.AdvanceTransition(0.11));
        Assert.Equal(1, factory.Preparations);
        Assert.False(flow.AdvanceTransition(0.01));
        Assert.Equal(SceneCurtainPhase.Covered, flow.Curtain.Phase);
        Assert.Equal(1, factory.Preparations);
        Assert.True(flow.AdvanceTransition(0));
        Assert.Equal(2, factory.Preparations);
        Assert.Equal("ROUTE_A", flow.Scenes.ActiveDestination?.LevelId);
        Assert.Equal(SceneCurtainPhase.FadingIn, flow.Curtain.Phase);
        Assert.True(flow.IsGameplaySuspended);
        Assert.NotNull(flow.LastPreparationMilliseconds);
        Assert.True(flow.LastPreparationMilliseconds >= 0);
        Assert.Equal(1, flow.PreparationAttempts);
    }

    [Fact]
    public void DuplicateRequestsCoalesceAndConflictingRequestsStayBusy()
    {
        var factory = new Factory();
        using var flow = new GameFlowController(factory);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay("ROUTE_A"));
        Assert.Equal(SceneQueueResult.Duplicate, flow.RequestPlay("ROUTE_A"));
        Assert.Equal(SceneQueueResult.Busy, flow.RequestPlay("ROUTE_B"));
        FinishFadeOut(flow);
        Assert.True(flow.AdvanceTransition(0.01));
        Assert.Equal(SceneQueueResult.Busy, flow.RequestTitle());
        Assert.False(flow.AdvanceTransition(0.12));
        Assert.Equal(SceneQueueResult.Queued, flow.RequestChangeLevel("ROUTE_B", "WEST"));
    }

    [Fact]
    public void InvalidDestinationNeverStartsCoverOrBlocksTitle()
    {
        using var flow = new GameFlowController(new Factory());
        Assert.Equal(SceneQueueResult.Invalid, flow.RequestPlay("NOT_THERE"));
        Assert.False(flow.IsTransitionActive);
        Assert.Equal(SceneCurtainPhase.Idle, flow.Curtain.Phase);
        Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
        Assert.NotNull(flow.LastFailure);
    }

    [Fact]
    public void FailedPreparationUncoversSameUsableWorldAndCanRetry()
    {
        var factory = new Factory();
        using var flow = new GameFlowController(factory);
        Start(flow);
        factory.FailNext = true;
        Assert.Equal(SceneQueueResult.Queued, flow.RequestChangeLevel("ROUTE_B", "WEST"));
        FinishFadeOut(flow);
        Assert.False(flow.AdvanceTransition(0.016));
        Assert.Equal(false, flow.Curtain.LastSucceeded);
        Assert.Equal("ROUTE_A", flow.Scenes.ActiveDestination?.LevelId);
        Assert.Equal(SceneCurtainPhase.FadingIn, flow.Curtain.Phase);
        Assert.NotNull(flow.LastFailure);
        Assert.True(flow.FailureNoticeSecondsRemaining > 0);
        Assert.False(flow.AdvanceTransition(0.12));
        Assert.False(flow.IsGameplaySuspended);
        Assert.Null(flow.Scenes.Pending);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestChangeLevel("ROUTE_B", "WEST"));
        FinishFadeOut(flow);
        Assert.True(flow.AdvanceTransition(0.016));
        Assert.Equal("ROUTE_B", flow.Scenes.ActiveDestination?.LevelId);
        Assert.Null(flow.LastFailure);
        Assert.Equal(3, flow.PreparationAttempts);
    }

    [Fact]
    public void LoadingFailureDoesNotDisposeActiveScene()
    {
        var factory = new Factory();
        using var flow = new GameFlowController(factory);
        Start(flow);
        factory.FailNext = true;
        Assert.Equal(SceneQueueResult.Queued, flow.RequestChangeLevel("ROUTE_B", "WEST"));
        FinishFadeOut(flow);
        Assert.False(flow.AdvanceTransition(0));
        Assert.DoesNotContain("dispose:ROUTE_A", factory.Events);
        Assert.DoesNotContain("exit:ROUTE_A", factory.Events);
        Assert.Equal("ROUTE_A", flow.Scenes.ActiveDestination?.LevelId);
    }

    [Fact]
    public void ReturnToTitleClearsOverlayOnlyWhenCommitSucceeds()
    {
        using var flow = new GameFlowController(new Factory());
        Start(flow);
        flow.PauseOrResume();
        flow.Execute(GameUiCommand.Settings);
        Assert.Equal(GameUiScreen.Settings, flow.CurrentMenu);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestTitle());
        Assert.Equal(GameUiScreen.Settings, flow.CurrentMenu);
        FinishFadeOut(flow);
        Assert.True(flow.AdvanceTransition(0));
        Assert.Equal(GameUiScreen.Title, flow.CurrentMenu);
        Assert.Equal(0, flow.Ui.OverlayDepth);
        Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
    }

    [Fact]
    public void FailedRestartKeepsPauseMenuAndCanResume()
    {
        var factory = new Factory();
        using var flow = new GameFlowController(factory);
        Start(flow);
        flow.PauseOrResume();
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        factory.FailNext = true;
        Assert.Equal(SceneQueueResult.Queued, flow.RequestRestart());
        FinishFadeOut(flow);
        Assert.False(flow.AdvanceTransition(0));
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        Assert.False(flow.AdvanceTransition(0.12));
        flow.PauseOrResume();
        Assert.Null(flow.CurrentMenu);
        Assert.False(flow.IsGameplaySuspended);
    }

    [Fact]
    public void MenusCannotNavigateOrActivateWhileCovered()
    {
        using var flow = new GameFlowController(new Factory());
        Start(flow);
        flow.PauseOrResume();
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestRestart());
        int selected = flow.SelectionIndex;
        flow.Navigate(1);
        flow.Back();
        flow.ConfirmSelection();
        flow.PauseOrResume();
        Assert.Equal(selected, flow.SelectionIndex);
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        Assert.True(flow.IsTransitionActive);
    }

    [Fact]
    public void FullGameClockIsNotNeededToAdvanceCurtain()
    {
        using var flow = new GameFlowController(new Factory());
        Start(flow);
        Assert.Equal(SceneQueueResult.Queued, flow.RequestChangeLevel("ROUTE_B", "WEST"));
        long before = flow.Scenes.CommitCount;
        for (int i = 0; i < 4; i++)
            Assert.False(flow.AdvanceTransition(0.03));
        Assert.Equal(SceneCurtainPhase.Covered, flow.Curtain.Phase);
        Assert.Equal(before, flow.Scenes.CommitCount);
        Assert.True(flow.IsGameplaySuspended);
        Assert.True(flow.AdvanceTransition(0));
        Assert.Equal(before + 1, flow.Scenes.CommitCount);
    }

    [Fact]
    public void ExplicitDirectDeveloperCommitBypassesCurtain()
    {
        using var flow = new GameFlowController(new Factory());
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay("MOTION_LAB"));
        Assert.True(flow.CommitPending());
        Assert.Equal(SceneCurtainPhase.Idle, flow.Curtain.Phase);
        Assert.False(flow.IsGameplaySuspended);
    }

    [Fact]
    public void FailureNoticeExpiresOnPresentationTime()
    {
        var factory = new Factory();
        using var flow = new GameFlowController(factory);
        Start(flow);
        factory.FailNext = true;
        Assert.Equal(SceneQueueResult.Queued, flow.RequestRestart());
        FinishFadeOut(flow);
        Assert.False(flow.AdvanceTransition(0));
        Assert.False(flow.AdvanceTransition(0.12));
        Assert.True(flow.FailureNoticeSecondsRemaining > 0);
        Assert.False(flow.AdvanceTransition(6.0));
        Assert.Equal(0.0, flow.FailureNoticeSecondsRemaining);
    }

    [Fact]
    public void PauseCannotBeResumedWhileReturnToTitleIsFading()
    {
        using var flow = new GameFlowController(new Factory());
        Start(flow);
        flow.PauseOrResume();
        Assert.Equal(SceneQueueResult.Queued, flow.RequestTitle());
        flow.PauseOrResume();
        Assert.Equal(GameUiScreen.Pause, flow.CurrentMenu);
        FinishFadeOut(flow);
        Assert.True(flow.AdvanceTransition(0));
        Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
        Assert.True(flow.IsTransitionActive);
        flow.Execute(GameUiCommand.Quit);
        Assert.False(flow.ExitRequested);
        Assert.False(flow.AdvanceTransition(0.12));
        Assert.Equal(GameUiScreen.Title, flow.CurrentMenu);
    }

    [Fact]
    public void TitlePreparationFailureKeepsTitleMenuAndAllowsPlayRetry()
    {
        var factory = new Factory();
        using var flow = new GameFlowController(factory);
        factory.FailNext = true;
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay("ROUTE_A"));
        FinishFadeOut(flow);
        Assert.False(flow.AdvanceTransition(0));
        Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
        Assert.Equal(GameUiScreen.Title, flow.CurrentMenu);
        Assert.False(flow.AdvanceTransition(0.12));
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay("ROUTE_A"));
        FinishFadeOut(flow);
        Assert.True(flow.AdvanceTransition(0));
        Assert.Equal(MajorSceneKind.Gameplay, flow.ActiveScene);
    }

    [Fact]
    public void SuccessfulReplacementDisposesOldSceneOnceAfterPreparation()
    {
        var factory = new Factory();
        using var flow = new GameFlowController(factory);
        Start(flow);
        int before = factory.Events.Count;
        Assert.Equal(SceneQueueResult.Queued, flow.RequestChangeLevel("ROUTE_B", "WEST"));
        FinishFadeOut(flow);
        Assert.True(flow.AdvanceTransition(0));
        List<string> operation = factory.Events.GetRange(before, factory.Events.Count - before);
        Assert.Equal(1, operation.FindAll(e => e == "dispose:ROUTE_A").Count);
        Assert.True(operation.IndexOf("prepare:ROUTE_B") < operation.IndexOf("exit:ROUTE_A"));
        Assert.True(operation.IndexOf("exit:ROUTE_A") < operation.IndexOf("enter:ROUTE_B"));
        Assert.True(operation.IndexOf("enter:ROUTE_B") < operation.IndexOf("dispose:ROUTE_A"));
    }

    [Fact]
    public void ZeroPresentationTimeCannotCompleteFadeOut()
    {
        using var flow = new GameFlowController(new Factory());
        Assert.Equal(SceneQueueResult.Queued, flow.RequestPlay("ROUTE_A"));
        for (int i = 0; i < 120; i++)
            Assert.False(flow.AdvanceTransition(0));
        Assert.Equal(SceneCurtainPhase.FadingOut, flow.Curtain.Phase);
        Assert.Equal(MajorSceneKind.Title, flow.ActiveScene);
    }

    [Fact]
    public void RepeatedLevelChangesNeverLeaveSceneGated()
    {
        var factory = new Factory();
        using var flow = new GameFlowController(factory);
        Start(flow);
        for (int i = 0; i < 12; i++)
        {
            string level = i % 2 == 0 ? "ROUTE_B" : "ROUTE_A";
            Assert.Equal(SceneQueueResult.Queued,
                flow.RequestChangeLevel(level, "WEST"));
            FinishFadeOut(flow);
            Assert.True(flow.AdvanceTransition(0));
            Assert.False(flow.AdvanceTransition(0.12));
            Assert.False(flow.IsTransitionActive);
            Assert.False(flow.IsGameplaySuspended);
        }
        Assert.Equal(13, flow.PreparationAttempts);
        Assert.Equal(13, flow.Scenes.CommitCount);
    }
}
