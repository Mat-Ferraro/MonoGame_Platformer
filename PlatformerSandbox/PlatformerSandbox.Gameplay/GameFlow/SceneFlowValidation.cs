using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Gameplay.GameFlow;

/// <summary>
/// Bounded, non-authoritative metrics for scene-flow inspection. Only scene
/// requests and completed scene preparations are recorded, not physics ticks.
/// No graphics or device dependency.
/// </summary>
public sealed class SceneFlowValidationMetrics
{
    private readonly string[] _history = new string[16];
    private int _historyNext;
    private int _historyCount;
    private double _preparationTotalMilliseconds;

    public long Queued { get; private set; }
    public long Duplicates { get; private set; }
    public long Busy { get; private set; }
    public long Invalid { get; private set; }
    public long PreparationAttempts { get; private set; }
    public long SuccessfulCommits { get; private set; }
    public long FailedPreparations { get; private set; }
    public double LastPreparationMilliseconds { get; private set; }
    public double MaximumPreparationMilliseconds { get; private set; }
    public double MeanPreparationMilliseconds => PreparationAttempts == 0
        ? 0 : _preparationTotalMilliseconds / PreparationAttempts;
    public string LatestEvent => _historyCount == 0 ? "NONE" :
        _history[(_historyNext + _history.Length - 1) % _history.Length];

    public IReadOnlyList<string> RecentEvents
    {
        get
        {
            var entries = new string[_historyCount];
            for (int i = 0; i < entries.Length; i++)
                entries[i] = _history[(_historyNext + _history.Length - _historyCount + i) % _history.Length];
            return Array.AsReadOnly(entries);
        }
    }

    internal void RecordRequest(SceneTransitionKind kind, SceneQueueResult result)
    {
        switch (result)
        {
            case SceneQueueResult.Queued: Queued++; break;
            case SceneQueueResult.Duplicate: Duplicates++; break;
            case SceneQueueResult.Busy: Busy++; break;
            case SceneQueueResult.Invalid: Invalid++; break;
            default: throw new ArgumentOutOfRangeException(nameof(result));
        }
        Trace(kind + " " + result);
    }

    internal void RecordPreparation(bool committed, double elapsedMilliseconds,
        MajorSceneKind scene, SceneDestination? destination, string? failure)
    {
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedMilliseconds));
        PreparationAttempts++;
        LastPreparationMilliseconds = elapsedMilliseconds;
        MaximumPreparationMilliseconds = Math.Max(MaximumPreparationMilliseconds, elapsedMilliseconds);
        _preparationTotalMilliseconds += elapsedMilliseconds;
        if (committed) SuccessfulCommits++; else FailedPreparations++;
        Trace((committed ? "COMMITTED " : "FAILED ") + scene + " " +
            (destination?.LevelId ?? "TITLE") +
            (committed ? "" : " " + (failure ?? "UNKNOWN")));
    }

    private void Trace(string entry)
    {
        _history[_historyNext] = entry;
        _historyNext = (_historyNext + 1) % _history.Length;
        _historyCount = Math.Min(_historyCount + 1, _history.Length);
    }
}

/// <summary>Read-only observation of the currently active route test world.</summary>
public readonly record struct SceneFlowProbeSnapshot(
    string LevelId, string EntranceId, long SessionId,
    long LevelEntries, long WorldId, long WorldTick);

public enum SceneFlowSmokeOperation { ChangeLevel, Restart, ExpectedFailure }
public readonly record struct SceneFlowSmokeCommand(
    SceneFlowSmokeOperation Operation, string LevelId, string EntranceId);

/// <summary>
/// A bounded, developer-invoked sequence of REAL scene-flow requests. The host
/// executes each command through GameFlowController. This class only validates
/// the resulting scene/session/world identities; it never updates simulation.
/// </summary>
public sealed class SceneFlowSmokeRunner
{
    private static readonly SceneFlowSmokeOperation[] Plan =
    [
        SceneFlowSmokeOperation.ChangeLevel,
        SceneFlowSmokeOperation.Restart,
        SceneFlowSmokeOperation.ExpectedFailure,
        SceneFlowSmokeOperation.ChangeLevel,
        SceneFlowSmokeOperation.ChangeLevel,
        SceneFlowSmokeOperation.Restart,
        SceneFlowSmokeOperation.ChangeLevel,
        SceneFlowSmokeOperation.ChangeLevel
    ];

    private SceneFlowProbeSnapshot _before;
    private SceneFlowSmokeCommand _pending;
    private bool _awaitingResult;
    private int _index;

    public bool IsRunning { get; private set; }
    public bool HasRun { get; private set; }
    public bool Passed { get; private set; }
    public int PassedChecks { get; private set; }
    public int TotalChecks => Plan.Length;
    public string Status { get; private set; } = "NOT RUN";

    public void Start(SceneFlowProbeSnapshot start)
    {
        if (IsRunning) throw new InvalidOperationException("Validation is already running.");
        Validate(start);
        IsRunning = true;
        HasRun = true;
        Passed = false;
        _index = 0;
        PassedChecks = 0;
        _awaitingResult = false;
        Status = "STARTED";
    }

    public void Abort(string reason)
    {
        if (!IsRunning) return;
        IsRunning = false;
        Passed = false;
        Status = "ABORTED: " + reason;
    }

    /// <summary>
    /// Return the next action only when the previous transition fully ended.
    /// The host calls this before stepping the world and gates simulation for
    /// the duration of the automated sequence.
    /// </summary>
    public SceneFlowSmokeCommand? Advance(SceneFlowProbeSnapshot current,
        bool curtainActive, bool? lastTransitionSucceeded)
    {
        if (!IsRunning) return null;
        Validate(current);
        if (curtainActive) return null;

        if (_awaitingResult)
        {
            if (lastTransitionSucceeded != (_pending.Operation != SceneFlowSmokeOperation.ExpectedFailure))
                return Fail("Unexpected commit outcome at step " + (_index + 1));
            if (current.SessionId != _before.SessionId)
                return Fail("Gameplay session changed");
            if (_pending.Operation == SceneFlowSmokeOperation.ExpectedFailure)
            {
                if (current.LevelId != _before.LevelId ||
                    current.EntranceId != _before.EntranceId ||
                    current.WorldId != _before.WorldId ||
                    current.WorldTick != _before.WorldTick ||
                    current.LevelEntries != _before.LevelEntries)
                    return Fail("Failed load changed the live world");
            }
            else
            {
                string expectedLevel = _pending.Operation == SceneFlowSmokeOperation.Restart
                    ? _before.LevelId : _pending.LevelId;
                string expectedEntrance = _pending.Operation == SceneFlowSmokeOperation.Restart
                    ? _before.EntranceId : _pending.EntranceId;
                if (current.LevelId != expectedLevel ||
                    current.EntranceId != expectedEntrance ||
                    current.WorldId == _before.WorldId ||
                    current.WorldTick != 0 ||
                    current.LevelEntries != _before.LevelEntries + 1)
                    return Fail("World/entrance/entry invariant failed");
            }
            PassedChecks++;
            _index++;
            _awaitingResult = false;
            if (_index == Plan.Length)
            {
                IsRunning = false;
                Passed = true;
                Status = "PASS " + PassedChecks + "/" + TotalChecks;
                return null;
            }
        }

        _before = current;
        string alternate = current.LevelId == GameLevelIds.RouteA
            ? GameLevelIds.RouteB : GameLevelIds.RouteA;
        SceneFlowSmokeOperation operation = Plan[_index];
        _pending = new SceneFlowSmokeCommand(operation, alternate,
            _index % 2 == 0 ? "WEST" : "EAST");
        _awaitingResult = true;
        Status = "RUN " + (_index + 1) + "/" + TotalChecks + " " + operation;
        return _pending;
    }

    private SceneFlowSmokeCommand? Fail(string reason)
    {
        IsRunning = false;
        Passed = false;
        Status = "FAIL " + (_index + 1) + ": " + reason;
        return null;
    }

    private static void Validate(SceneFlowProbeSnapshot snapshot)
    {
        if (snapshot.LevelId is not (GameLevelIds.RouteA or GameLevelIds.RouteB) ||
            string.IsNullOrWhiteSpace(snapshot.EntranceId) ||
            snapshot.SessionId <= 0 || snapshot.LevelEntries < 1 ||
            snapshot.WorldId <= 0 || snapshot.WorldTick < 0)
            throw new ArgumentException("The smoke test needs an active route world.", nameof(snapshot));
    }
}
