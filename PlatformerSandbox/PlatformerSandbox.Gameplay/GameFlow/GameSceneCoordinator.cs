using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Gameplay.GameFlow;

/// <summary>Stable authoring IDs used by this first application-flow vertical slice.</summary>
public static class GameLevelIds
{
    public const string MovementGym = "MOVEMENT_GYM";
    public const string SharedWorld = "SHARED_WORLD";
    public const string AuthoredWorld = "AUTHORED_WORLD";
    public const string MotionLab = "MOTION_LAB";
    public const string RouteA = "ROUTE_A";
    public const string RouteB = "ROUTE_B";
    public const string LdtkTest = "LDTK_TEST";
    public const string LdtkParallaxDemo = "LDTK_PARALLAX_DEMO";
    public const string LdtkSceneryDemo = "LDTK_SCENERY_DEMO";
    public const string LdtkTerrainDemo = "LDTK_TERRAIN_DEMO";
    public const string LdtkConnectedA = "CAVERN_A";
    public const string LdtkConnectedB = "CAVERN_B";
    public const string DefaultEntrance = "START";

    // Single source of truth for the standalone playable fixtures. Routes have
    // their own validated entrance catalog and are intentionally excluded.
    public static bool IsStandalonePlayableLevel(string levelId) => levelId is
        (MovementGym or SharedWorld or AuthoredWorld or MotionLab or LdtkTest or
         LdtkParallaxDemo or LdtkSceneryDemo or LdtkTerrainDemo or
         LdtkConnectedA or LdtkConnectedB);
}

public sealed record SceneDestination
{
    public string LevelId { get; }
    public string EntranceId { get; }

    public SceneDestination(string levelId, string entranceId = GameLevelIds.DefaultEntrance)
    {
        if (string.IsNullOrWhiteSpace(levelId))
            throw new ArgumentException("A level ID is required.", nameof(levelId));
        if (string.IsNullOrWhiteSpace(entranceId))
            throw new ArgumentException("An entrance ID is required.", nameof(entranceId));
        LevelId = levelId;
        EntranceId = entranceId;
    }
}

public enum MajorSceneKind { Title, Gameplay }
public enum SceneTransitionKind { Play, Restart, ReturnToTitle, ChangeLevel }
public enum SceneQueueResult { Queued, Duplicate, Busy, Invalid }

public readonly record struct SceneTransitionRequest(
    SceneTransitionKind Kind, SceneDestination? Destination = null)
{
    public static SceneTransitionRequest Play(string levelId,
        string entranceId = GameLevelIds.DefaultEntrance) =>
        new(SceneTransitionKind.Play, new SceneDestination(levelId, entranceId));
    public static SceneTransitionRequest ChangeLevel(string levelId,
        string entranceId) =>
        new(SceneTransitionKind.ChangeLevel, new SceneDestination(levelId, entranceId));
    public static SceneTransitionRequest Restart() => new(SceneTransitionKind.Restart);
    public static SceneTransitionRequest ToTitle() => new(SceneTransitionKind.ReturnToTitle);
}

/// <summary>A prepared scene. No scene creates or replaces another scene.</summary>
public interface IGameFlowScene : IDisposable
{
    MajorSceneKind Kind { get; }
    SceneDestination? Destination { get; }
    void Enter();
    void Exit();
}

/// <summary>
/// Prepare happens before the previous scene is retired, allowing invalid
/// content to be rejected without destroying the currently usable scene.
/// </summary>
public interface IGameFlowSceneFactory
{
    bool Supports(MajorSceneKind kind, SceneDestination? destination);
    bool TryPrepare(MajorSceneKind kind, SceneDestination? destination,
        out IGameFlowScene? scene, out string? error);
}

/// <summary>
/// Owns exactly one active major scene and its lifecycle. Requesting a change
/// never performs it; the MonoGame host commits only at a safe update boundary.
/// </summary>
public sealed class GameSceneCoordinator : IDisposable
{
    private readonly IGameFlowSceneFactory _factory;
    private IGameFlowScene _active;
    private SceneTransitionRequest? _pending;
    private bool _disposed;
    public long PreparedSceneInstances { get; private set; }
    public long DisposedSceneInstances { get; private set; }
    public long LiveSceneInstances => PreparedSceneInstances - DisposedSceneInstances;
    public long PeakLiveSceneInstances { get; private set; }

    private void OnPrepared()
    {
        PreparedSceneInstances++;
        PeakLiveSceneInstances = Math.Max(PeakLiveSceneInstances, LiveSceneInstances);
    }

    private void OnDisposed() => DisposedSceneInstances++;

    public GameSceneCoordinator(IGameFlowSceneFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        if (!_factory.TryPrepare(MajorSceneKind.Title, null,
            out IGameFlowScene? title, out string? error) || title is null)
            throw new InvalidOperationException("Cannot initialize Title: " + error);
        if (title.Kind != MajorSceneKind.Title || title.Destination is not null)
        {
            // A bad startup factory result is still an owned prepared scene.
            // Dispose it before failing fast on the invalid contract.
            OnPrepared();
            title.Dispose();
            OnDisposed();
            throw new InvalidOperationException("Scene factory returned an invalid Title scene.");
        }
        _active = title;
        OnPrepared();
        _active.Enter();
    }

    public MajorSceneKind ActiveKind => _active.Kind;
    public SceneDestination? ActiveDestination => _active.Destination;
    public SceneTransitionRequest? Pending => _pending;
    public string? LastFailure { get; private set; }
    public long CommitCount { get; private set; }

    public SceneQueueResult Request(SceneTransitionRequest request)
    {
        EnsureAlive();
        if (!Enum.IsDefined(request.Kind))
            return Reject("Unknown scene transition type.");
        if (_pending is SceneTransitionRequest existing)
        {
            if (existing == request)
                return SceneQueueResult.Duplicate;
            return SceneQueueResult.Busy;
        }

        MajorSceneKind target;
        SceneDestination? destination;
        switch (request.Kind)
        {
            case SceneTransitionKind.Play:
                target = MajorSceneKind.Gameplay;
                destination = request.Destination;
                if (destination is null)
                    return Reject("Play requires a level destination.");
                break;
            case SceneTransitionKind.ChangeLevel:
                if (_active.Kind != MajorSceneKind.Gameplay || request.Destination is null)
                    return Reject("Level changes require active gameplay and a destination.");
                target = MajorSceneKind.Gameplay;
                destination = request.Destination;
                break;
            case SceneTransitionKind.Restart:
                if (request.Destination is not null)
                    return Reject("Restart uses the active level destination.");
                if (_active.Kind != MajorSceneKind.Gameplay)
                    return Reject("Cannot restart outside gameplay.");
                target = MajorSceneKind.Gameplay;
                destination = _active.Destination;
                break;
            case SceneTransitionKind.ReturnToTitle:
                if (request.Destination is not null || _active.Kind == MajorSceneKind.Title)
                    return Reject("Return to Title is only valid from gameplay.");
                target = MajorSceneKind.Title;
                destination = null;
                break;
            default:
                return Reject("Invalid transition.");
        }

        if (!_factory.Supports(target, destination))
            return Reject("Unknown or unsupported destination: " + destination?.LevelId +
                " / " + destination?.EntranceId);
        _pending = request;
        LastFailure = null;
        return SceneQueueResult.Queued;
    }

    /// <summary>Returns true only if the active scene was successfully replaced.</summary>
    public bool CommitPending()
    {
        EnsureAlive();
        if (_pending is not SceneTransitionRequest request)
            return false;
        // Clear before preparing so a rejected request does not strand a busy gate.
        _pending = null;
        MajorSceneKind target = request.Kind == SceneTransitionKind.ReturnToTitle
            ? MajorSceneKind.Title : MajorSceneKind.Gameplay;
        SceneDestination? destination = request.Kind switch
        {
            SceneTransitionKind.Play => request.Destination,
            SceneTransitionKind.ChangeLevel => request.Destination,
            SceneTransitionKind.Restart => _active.Destination,
            _ => null
        };

        if (!_factory.TryPrepare(target, destination,
            out IGameFlowScene? next, out string? error) || next is null)
        {
            LastFailure = error ?? "Scene preparation failed.";
            if (next is not null)
            {
                OnPrepared();
                next.Dispose();
                OnDisposed();
            }
            return false;
        }
        OnPrepared();
        if (next.Kind != target || next.Destination != destination)
        {
            next.Dispose();
            OnDisposed();
            LastFailure = "Scene factory produced the wrong destination.";
            return false;
        }

        // No simulation is running during CommitPending. Prepare precedes exit;
        // do not suppress programmer faults in Enter/Exit with broad catches.
        IGameFlowScene previous = _active;
        try
        {
            previous.Exit();
        }
        catch
        {
            // A programming fault in Exit is not treated as a recoverable
            // content failure. Release the unused candidate, then rethrow.
            next.Dispose();
            OnDisposed();
            throw;
        }
        try
        {
            next.Enter();
        }
        catch
        {
            next.Dispose();
            OnDisposed();
            previous.Enter();
            throw;
        }
        _active = next;
        previous.Dispose();
        OnDisposed();
        CommitCount++;
        LastFailure = null;
        return true;
    }

    private SceneQueueResult Reject(string message)
    {
        LastFailure = message;
        return SceneQueueResult.Invalid;
    }

    private void EnsureAlive()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GameSceneCoordinator));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pending = null;
        _active.Exit();
        _active.Dispose();
        OnDisposed();
    }
}
