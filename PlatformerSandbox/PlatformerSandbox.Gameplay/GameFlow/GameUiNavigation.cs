using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace PlatformerSandbox.Gameplay.GameFlow;

public enum GameUiScreen { Title, Pause, Settings, Controls, LevelSelect, Graphics }
public enum GameUiCommand
{
    PlayMovementGym, PlaySharedWorld, PlayAuthoredWorld, PlayMotionLab,
    Resume, Restart, Settings, Controls, ToggleHints, Back, ReturnToTitle, Quit,
    PlayRouteLab, OpenLevelSelect, PlaySelectedLevel, Graphics
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
    // Only maintained gameplay/lighting destinations are shown. Older scenes,
    // fixtures, and LDtk files remain loadable by --dev-level and by world routes.
    public const string LightingIntegrationTest = GameLevelIds.LightingIntegrationTest;
    private static readonly IReadOnlyList<GameUiMenuItem> _items = Array.AsReadOnly(
        new GameUiMenuItem[]
        {
            new("PLAY / CONNECTED CAVERN", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.LdtkConnectedA)),
            new("TEST / MATERIAL SHOWCASE", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.MaterialShowcaseTest)),
            new("TEST / LIGHT TYPES SHOWCASE", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.LightTypesLab)),
            new("ENGINE PERFORMANCE LAB", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.LightingPerformanceLab)),
            new("TEST / LIGHTING QUALITY LAB", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.LightingQualityLab)),
            new("TEST / MOVEMENT GYM", GameUiCommand.PlaySelectedLevel,
                new SceneDestination(GameLevelIds.MovementGym))
        });

    public static IReadOnlyList<GameUiMenuItem> Items => _items;
    public static bool IsLegacy(GameUiMenuItem item) => item.Destination?.LevelId is
        GameLevelIds.LightingIntegrationTest or GameLevelIds.EmissiveLightingLab or GameLevelIds.Phase21LightingLab or GameLevelIds.LightingBasics;
    public static IReadOnlyList<GameUiMenuItem> Primary(IReadOnlyList<GameUiMenuItem> entries)
    {
        string[] order = { GameLevelIds.LdtkConnectedA, GameLevelIds.LightingPerformanceLab, GameLevelIds.LightingQualityLab,
            GameLevelIds.MovementGym, GameLevelIds.MaterialShowcaseTest, GameLevelIds.LightTypesLab };
        var items = entries.Where(x => !IsLegacy(x)).OrderBy(x => Array.IndexOf(order,x.Destination?.LevelId)).ToList();
        // Retired labs remain direct-launchable for regression coverage, never listed here.
        items.Add(new("BACK", GameUiCommand.Back));
        return items.AsReadOnly();
    }

    /// <summary>Only display currently maintained LDtk scenes that actually exist.
    /// Debug fixtures remain available via direct --dev-level or linked exits.</summary>
    public static IReadOnlyList<GameUiMenuItem> WithDiscoveredLevels(IEnumerable<string> levelIds)
    {
        ArgumentNullException.ThrowIfNull(levelIds);
        var available = levelIds.ToHashSet(StringComparer.Ordinal);
        var menu = _items.Where(x => x.Destination is SceneDestination destination &&
            (destination.LevelId == GameLevelIds.MovementGym ||
             available.Contains(destination.LevelId))).ToArray();
        return Array.AsReadOnly(menu);
    }
}


/// <summary>Shared mouse and draw geometry for visible menus. No MonoGame types.</summary>
public static class GameMenuLayout
{
    public static (int Left, int Top, int Width, int Height, int First, int Rows)
        Get(int viewportWidth, int viewportHeight, GameUiScreen screen,
            int entryCount, int selectionIndex)
    {
        int width = Math.Min(660, Math.Max(200, viewportWidth - 36));
        int left = Math.Max(8, (viewportWidth - width) / 2);
        int height = screen switch
        {
            GameUiScreen.Controls => 260,
            GameUiScreen.Title => 242,
            GameUiScreen.LevelSelect => 110 + entryCount * 30,
            _ => 242
        };
        height = Math.Min(height, Math.Max(140, viewportHeight - 32));
        int top = Math.Max(8, (viewportHeight - height) / 2);
        int rows = Math.Max(1, (height - 106) / 30);
        int first = Math.Clamp(selectionIndex - rows / 2, 0,
            Math.Max(0, entryCount - rows));
        return (left, top, width, height, first, rows);
    }

    /// <summary>Returns a hovered entry, never an entry hidden by scrolling.</summary>
    public static int Hit(int viewportWidth, int viewportHeight,
        GameUiScreen screen, int entryCount, int selectionIndex,
        int mouseX, int mouseY)
    {
        var b = Get(viewportWidth, viewportHeight, screen, entryCount, selectionIndex);
        if (mouseX < b.Left + 18 || mouseX >= b.Left + b.Width - 18 ||
            mouseY < b.Top + 58) return -1;
        int row = (mouseY - (b.Top + 58)) / 30;
        if (row < 0 || row >= b.Rows) return -1;
        int index = b.First + row;
        return index < entryCount && mouseY < b.Top + 58 + b.Rows * 30
            ? index : -1;
    }
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
            GameUiScreen.Controls or GameUiScreen.Graphics => current == GameUiScreen.Settings,
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
    private readonly IReadOnlyList<GameUiMenuItem> _levelMenuItems;
    private readonly GameUiNavigation _ui = new();
    private readonly SceneTransitionCurtain _curtain = new();
    private bool _disposed;
    private double _failureNoticeSeconds;
    public SceneFlowValidationMetrics Validation { get; } = new();

    public GameFlowController(IGameFlowSceneFactory factory,
        IReadOnlyList<GameUiMenuItem>? levelMenuItems = null)
    {
        _scenes = new GameSceneCoordinator(factory);
        var catalog = levelMenuItems ?? GameLevelMenuCatalog.Items;
        _levelMenuItems = GameLevelMenuCatalog.Primary(catalog);
        if (_levelMenuItems.Count == 0)
            throw new ArgumentException("Level menu cannot be empty.", nameof(levelMenuItems));
    }

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
        GameUiScreen.LevelSelect => _levelMenuItems,
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
            new("GRAPHICS SETTINGS", GameUiCommand.Graphics),
            new("BACK", GameUiCommand.Back)
        ],
        GameUiScreen.Controls or GameUiScreen.Graphics => [new("BACK", GameUiCommand.Back)],
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
            case GameUiCommand.Graphics:
                _ui.Push(GameUiScreen.Graphics, ActiveScene);
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
