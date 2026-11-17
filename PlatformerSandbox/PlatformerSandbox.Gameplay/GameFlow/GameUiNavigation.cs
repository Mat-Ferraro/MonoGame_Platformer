using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace PlatformerSandbox.Gameplay.GameFlow;

public enum GameUiScreen { Title, Pause, Settings, Controls, LevelSelect }
public enum GameUiCommand
{
    PlayMovementGym, PlaySharedWorld, PlayAuthoredWorld, PlayMotionLab,
    Resume, Restart, Settings, Controls, ToggleHints, Back, ReturnToTitle, Quit,
    PlayRouteLab, OpenLevelSelect, PlaySelectedLevel
}
public readonly record struct GameUiMenuItem(string Label, GameUiCommand Command,
    SceneDestination? Destination = null);

/// <summary>
/// The one visible level catalog for this development build. Keep the IDs and
/// entrance names here rather than having the presentation invent destinations.
/// No MonoGame dependencies: it can be validated in headless tests.
/// </summary>
public static class GameLevelMenuCatalog
{
    private static readonly IReadOnlyList<GameUiMenuItem> _items = Array.AsReadOnly(
        new GameUiMenuItem[]
        {
            new("LDTK  /  FIRST ATTEMPT", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.LdtkTest)),
            new("LDTK  /  PARALLAX DEMO", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.LdtkParallaxDemo)),
            new("LDTK  /  SCENERY DEMO", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.LdtkSceneryDemo)),
            new("LDTK  /  TERRAIN DEMO", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.LdtkTerrainDemo)),
            new("LDTK  /  CONNECTED CAVERN A", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.LdtkConnectedA)),
            new("LDTK  /  CONNECTED CAVERN B", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.LdtkConnectedB)),
            new("ROUTES  /  LEVEL A", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.RouteA)),
            new("ROUTES  /  LEVEL B EAST", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.RouteB, "EAST")),
            new("GYM  /  MOVEMENT", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.MovementGym)),
            new("GYM  /  SHARED WORLD", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.SharedWorld)),
            new("GYM  /  AUTHORED WORLD", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.AuthoredWorld)),
            new("GYM  /  MOTION LAB", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.MotionLab)),
        });

    public static IReadOnlyList<GameUiMenuItem> Items => _items;
}


/// <summary>
/// Small focused overlay stack. Title is the root view of the Title major
/// scene, not an overlay. Each pushed screen remembers its own selection.
/// </summary>
public sealed class GameUiNavigation
{
    private sealed class Frame(GameUiScreen screen)
    {
        public GameUiScreen Screen { get; } = screen;
        public int Selection { get; set; }
    }

    private readonly List<Frame> _overlays = [];
    private readonly Frame _title = new(GameUiScreen.Title);

    public int OverlayDepth => _overlays.Count;
    public bool HasOverlay => _overlays.Count != 0;
    public bool IsGameplayPaused => _overlays.Count != 0 &&
        _overlays[0].Screen == GameUiScreen.Pause;

    public GameUiScreen? CurrentScreen(MajorSceneKind scene)
    {
        if (_overlays.Count > 0) return _overlays[^1].Screen;
        return scene == MajorSceneKind.Title ? GameUiScreen.Title : null;
    }

    public int SelectionIndex(MajorSceneKind scene) =>
        _overlays.Count > 0 ? _overlays[^1].Selection :
        scene == MajorSceneKind.Title ? _title.Selection : -1;

    public void Push(GameUiScreen screen, MajorSceneKind scene)
    {
        if (screen == GameUiScreen.Title)
            throw new ArgumentException("Title is a major-scene view, not an overlay.");
        GameUiScreen? current = CurrentScreen(scene);
        bool valid = screen switch
        {
            GameUiScreen.Pause => scene == MajorSceneKind.Gameplay && current is null,
            GameUiScreen.LevelSelect =>
                (scene == MajorSceneKind.Title && current == GameUiScreen.Title) ||
                (scene == MajorSceneKind.Gameplay && current == GameUiScreen.Pause),
            GameUiScreen.Settings => current is GameUiScreen.Pause or GameUiScreen.Title,
            GameUiScreen.Controls => current == GameUiScreen.Settings,
            _ => false
        };
        if (!valid) throw new InvalidOperationException("Invalid UI navigation transition.");
        _overlays.Add(new Frame(screen));
    }

    public bool Back()
    {
        if (_overlays.Count == 0) return false;
        _overlays.RemoveAt(_overlays.Count - 1);
        return true;
    }

    public void Clear() => _overlays.Clear();

    public void MoveSelection(int delta, int itemCount, MajorSceneKind scene)
    {
        if (itemCount < 1) throw new ArgumentOutOfRangeException(nameof(itemCount));
        Frame frame = _overlays.Count > 0 ? _overlays[^1] :
            scene == MajorSceneKind.Title ? _title :
            throw new InvalidOperationException("Gameplay has no focused menu.");
        frame.Selection = ((frame.Selection + delta) % itemCount + itemCount) % itemCount;
    }
}

/// <summary>
/// Semantic menu policy with no MonoGame device types. Input sources send
/// navigation actions here; gameplay is gated whenever a UI overlay has focus.
/// </summary>
public sealed class GameFlowController : IDisposable
{
    private readonly GameSceneCoordinator _scenes;
    private readonly GameUiNavigation _ui = new();
    private readonly SceneTransitionCurtain _curtain = new();
    private bool _disposed;
    private double _failureNoticeSeconds;
    public SceneFlowValidationMetrics Validation { get; } = new();

    public GameFlowController(IGameFlowSceneFactory factory) =>
        _scenes = new GameSceneCoordinator(factory);

    public GameSceneCoordinator Scenes => _scenes;
    public GameUiNavigation Ui => _ui;
    public SceneTransitionCurtain Curtain => _curtain;
    public bool IsTransitionActive => _curtain.IsActive;
    public double? LastPreparationMilliseconds { get; private set; }
    public long PreparationAttempts { get; private set; }
    public double FailureNoticeSecondsRemaining => _failureNoticeSeconds;
    public MajorSceneKind ActiveScene => _scenes.ActiveKind;
    public bool IsGameplaySuspended => ActiveScene != MajorSceneKind.Gameplay || _ui.HasOverlay || IsTransitionActive;
    public bool IsMenuOpen => _ui.CurrentScreen(ActiveScene) is not null;
    public bool ShowFlowHints { get; private set; } = true;
    public bool ExitRequested { get; private set; }
    public string? LastFailure => _scenes.LastFailure;
    public GameUiScreen? CurrentMenu => _ui.CurrentScreen(ActiveScene);

    public IReadOnlyList<GameUiMenuItem> MenuItems => CurrentMenu switch
    {
        GameUiScreen.Title =>
        [
            new("SELECT LEVEL", GameUiCommand.OpenLevelSelect),
            new("SETTINGS", GameUiCommand.Settings),
            new("QUIT", GameUiCommand.Quit)
        ],
        GameUiScreen.LevelSelect => GameLevelMenuCatalog.Items,
        GameUiScreen.Pause =>
        [
            new("RESUME", GameUiCommand.Resume),
            new("RESTART LEVEL", GameUiCommand.Restart),
            new("SETTINGS", GameUiCommand.Settings),
            new("RETURN TO TITLE", GameUiCommand.ReturnToTitle),
            new("SELECT LEVEL", GameUiCommand.OpenLevelSelect)
        ],
        GameUiScreen.Settings =>
        [
            new(ShowFlowHints ? "FLOW HINTS: ON" : "FLOW HINTS: OFF", GameUiCommand.ToggleHints),
            new("CONTROLS", GameUiCommand.Controls),
            new("BACK", GameUiCommand.Back)
        ],
        GameUiScreen.Controls => [new("BACK", GameUiCommand.Back)],
        _ => Array.Empty<GameUiMenuItem>()
    };

    public int SelectionIndex => _ui.SelectionIndex(ActiveScene);

    private SceneQueueResult Queue(SceneTransitionRequest request)
    {
        // Preserve duplicate/busy semantics while a request is queued.
        // No new request can replace a committed scene during fade-in.
        if (_curtain.IsActive && _scenes.Pending is null)
        {
            Validation.RecordRequest(request.Kind, SceneQueueResult.Busy);
            return SceneQueueResult.Busy;
        }
        SceneQueueResult result = _scenes.Request(request);
        Validation.RecordRequest(request.Kind, result);
        if (result == SceneQueueResult.Queued)
        {
            _curtain.Begin();
            _failureNoticeSeconds = 0;
        }
        return result;
    }

    public SceneQueueResult RequestPlay(string levelId,
        string entranceId = GameLevelIds.DefaultEntrance) =>
        Queue(SceneTransitionRequest.Play(levelId, entranceId));

    public SceneQueueResult RequestChangeLevel(string levelId, string entranceId) =>
        Queue(SceneTransitionRequest.ChangeLevel(levelId, entranceId));

    public SceneQueueResult RequestRestart() => Queue(SceneTransitionRequest.Restart());

    public SceneQueueResult RequestTitle() => Queue(SceneTransitionRequest.ToTitle());

    /// <summary>
    /// Advance only non-authoritative presentation time. When the curtain is
    /// covered, the existing coordinator prepares/commits synchronously at
    /// this safe update boundary. A recoverable preparation failure keeps
    /// the old scene, clears the gate after fade-in, and reports an error.
    /// </summary>
    public bool AdvanceTransition(double presentationSeconds)
    {
        if (!double.IsFinite(presentationSeconds) || presentationSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(presentationSeconds));
        _failureNoticeSeconds = Math.Max(0, _failureNoticeSeconds - presentationSeconds);
        if (!_curtain.Advance(presentationSeconds)) return false;

        long start = Stopwatch.GetTimestamp();
        bool changed;
        try
        {
            changed = _scenes.CommitPending();
        }
        finally
        {
            LastPreparationMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            PreparationAttempts++;
        }
        Validation.RecordPreparation(changed, LastPreparationMilliseconds!.Value,
            _scenes.ActiveKind, _scenes.ActiveDestination, _scenes.LastFailure);
        if (changed)
            _ui.Clear();
        else
            _failureNoticeSeconds = 6.0;
        _curtain.Complete(changed);
        return changed;
    }

    /// <summary>
    /// Preserve the Phase 1-4 explicit commit API for tests and direct dev
    /// startup. Normal game-loop transitions use AdvanceTransition instead.
    /// </summary>
    public bool CommitPending()
    {
        long start = Stopwatch.GetTimestamp();
        bool changed = _scenes.CommitPending();
        double duration = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        Validation.RecordPreparation(changed, duration, _scenes.ActiveKind,
            _scenes.ActiveDestination, _scenes.LastFailure);
        if (changed) _ui.Clear();
        _curtain.Reset();
        return changed;
    }

    public void PauseOrResume()
    {
        if (IsTransitionActive || ActiveScene != MajorSceneKind.Gameplay) return;
        if (_ui.HasOverlay) _ui.Clear();
        else _ui.Push(GameUiScreen.Pause, ActiveScene);
    }

    public void Back()
    {
        if (IsTransitionActive) return;
        if (!_ui.Back() && ActiveScene == MajorSceneKind.Gameplay)
            _ui.Push(GameUiScreen.Pause, ActiveScene);
    }

    public void Navigate(int direction)
    {
        if (IsTransitionActive || !IsMenuOpen) return;
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        _ui.MoveSelection(direction, MenuItems.Count, ActiveScene);
    }

    public void ConfirmSelection()
    {
        if (IsTransitionActive || !IsMenuOpen) return;
        IReadOnlyList<GameUiMenuItem> items = MenuItems;
        if (items.Count == 0) return;
        GameUiMenuItem selected = items[SelectionIndex];
        if (selected.Command == GameUiCommand.PlaySelectedLevel)
        {
            if (CurrentMenu != GameUiScreen.LevelSelect ||
                selected.Destination is not SceneDestination destination)
                throw new InvalidOperationException("A level selection requires a destination.");
            RequestPlay(destination.LevelId, destination.EntranceId);
            return;
        }
        Execute(selected.Command);
    }

    public void Execute(GameUiCommand command)
    {
        if (IsTransitionActive) return;
        switch (command)
        {
            case GameUiCommand.OpenLevelSelect:
                if (CurrentMenu is GameUiScreen.Title or GameUiScreen.Pause)
                    _ui.Push(GameUiScreen.LevelSelect, ActiveScene);
                break;
            case GameUiCommand.PlayMovementGym:
                RequestPlay(GameLevelIds.MovementGym); break;
            case GameUiCommand.PlaySharedWorld:
                RequestPlay(GameLevelIds.SharedWorld); break;
            case GameUiCommand.PlayAuthoredWorld:
                RequestPlay(GameLevelIds.AuthoredWorld); break;
            case GameUiCommand.PlayMotionLab:
                RequestPlay(GameLevelIds.MotionLab); break;
            case GameUiCommand.PlayRouteLab:
                RequestPlay(GameLevelIds.RouteA); break;
            case GameUiCommand.Resume:
                if (CurrentMenu == GameUiScreen.Pause) _ui.Clear();
                break;
            case GameUiCommand.Restart:
                if (CurrentMenu == GameUiScreen.Pause) RequestRestart();
                break;
            case GameUiCommand.Settings:
                _ui.Push(GameUiScreen.Settings, ActiveScene);
                break;
            case GameUiCommand.Controls:
                _ui.Push(GameUiScreen.Controls, ActiveScene);
                break;
            case GameUiCommand.ToggleHints:
                if (CurrentMenu == GameUiScreen.Settings)
                    ShowFlowHints = !ShowFlowHints;
                break;
            case GameUiCommand.Back:
                Back(); break;
            case GameUiCommand.ReturnToTitle:
                if (ActiveScene == MajorSceneKind.Gameplay) RequestTitle();
                break;
            case GameUiCommand.Quit:
                if (ActiveScene == MajorSceneKind.Title) ExitRequested = true;
                break;
            default: throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _scenes.Dispose();
    }
}
