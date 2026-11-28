using PlatformerSandbox.Foundation.Performance;
using PlatformerSandbox.Foundation.Geometry;
using System.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PlatformerSandbox.Diagnostics;
using PlatformerSandbox.Gameplay.Abilities;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.Input.Local;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.States;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Simulation.Diagnostics;

namespace PlatformerSandbox;

/// <summary>
/// Game-specific adapter to the cross-project diagnostics recorder. This may
/// inspect immutable committed results, but never calls a motor to change state.
/// No per-tick file IO: all recent context stays in a bounded memory ring.
/// </summary>
public sealed class GameDiagnosticBridge
{
    private readonly DiagnosticContextRecorder _recorder;
    private readonly DiagnosticHangWatchdog? _watchdog;
    private readonly Func<DiagnosticExportResult>? _export;
    private readonly bool _previousUncleanExit;
    private string? _reportStatus;
    private string? _lastSceneKey;
    public string? LastCaptureError { get; private set; }

    public GameDiagnosticBridge(DiagnosticContextRecorder recorder,
        DiagnosticHangWatchdog? watchdog = null,
        Func<DiagnosticExportResult>? export = null,
        bool previousUncleanExit = false)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _watchdog = watchdog;
        _export = export;
        _previousUncleanExit = previousUncleanExit;
    }

    public IDisposable? MonitorOperation(string operation) => _watchdog?.BeginOperation(operation);

    public string StatusMessage => _reportStatus ?? (_previousUncleanExit
        ? "PREVIOUS SESSION ENDED UNEXPECTEDLY - DEV TOOLS > EXPORT REPORT"
        : "DEV TOOLS > EXPORT REPORT ZIP");

    public void ExportReport()
    {
        if (_export is null) return;
        try
        {
            DiagnosticExportResult result = _export();
            _reportStatus = result.Success
                ? "REPORT READY IN LOGS/REPORTS: " + System.IO.Path.GetFileName(result.FilePath)
                : "REPORT EXPORT FAILED: " + (result.Error ?? "unknown error");
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _reportStatus = "REPORT EXPORT FAILED: " + e.GetType().Name;
        }
    }

    public void CaptureScene(GameFlowController flow, WorldSession? world,
        MovementGymSession gym)
    {
        Safely(() =>
        {
            string key = $"{flow.ActiveScene}|{flow.Scenes.ActiveDestination?.LevelId}|" +
                $"{flow.Scenes.ActiveDestination?.EntranceId}|{world?.WorldInstanceId}|" +
                $"{flow.IsGameplaySuspended}";
            if (key == _lastSceneKey) return;
            _lastSceneKey = key;
            _recorder.ClearPendingOperation();
            if (world is not null)
                CaptureCommittedWorld(flow, world, gym.FaultSensitivity, world.Snapshot());
            else if (flow.ActiveScene == MajorSceneKind.Gameplay)
                CaptureCommittedGym(flow, gym);
            else
                _recorder.UpdateCommittedState(CommonState(flow, gym.FaultSensitivity));
            _recorder.RecordActivity("scene.state", key, world?.WorldTick);
        });
    }

    public void BeginWorldStep(GameFlowController flow, WorldSession world,
        SimulationFaultSensitivity mode, IReadOnlyList<WorldPlayerCommand> commands,
        IReadOnlyList<CharacterStateRequest> states,
        IReadOnlyList<CharacterAbilityRequest> abilities,
        IReadOnlyList<WorldMotionRequest> motions)
    {
        Safely(() =>
        {
            if (EnginePerformanceOptions.DiagnosticRecords)
            {
                // No deferred references to caller-owned request lists.
                _recorder.BeginOperation("world.step", new WorldAttempt(world.WorldInstanceId, mode,
                    commands.Take(768).ToArray(), states.Take(768).ToArray(),
                    abilities.Take(768).ToArray(), motions.Take(768).ToArray()), FormatAttempt, world.WorldTick + 1);
                return;
            }
            var detail = new StringBuilder(256);
            detail.Append("worldId=").Append(world.WorldInstanceId)
                .Append(" faultMode=").Append(mode).Append(" commands=");
            foreach (WorldPlayerCommand command in commands)
                detail.Append(" [P").Append(command.PlayerId).Append(' ')
                    .Append(command.Direction).Append(" press=").Append(command.JumpPressed)
                    .Append(" release=").Append(command.JumpReleased).Append(']');
            foreach (CharacterStateRequest request in states)
                detail.Append(" [state:").Append(request.ToString()).Append(']');
            foreach (CharacterAbilityRequest request in abilities)
                detail.Append(" [ability:").Append(request.ToString()).Append(']');
            foreach (WorldMotionRequest request in motions)
                detail.Append(" [motion:P").Append(request.PlayerId).Append(' ')
                    .Append(request.Kind).Append(' ')
                    .Append(Number(request.HorizontalSpeed)).Append(']');
            _recorder.BeginOperation("world.step", detail.ToString(), world.WorldTick + 1);
        });
    }

    public void CompleteWorldStep(GameFlowController flow, WorldSession world,
        SimulationFaultSensitivity mode, WorldStepResult committed)
    {
        Safely(() =>
        {
            CaptureCommittedWorld(flow, world, mode, committed);
            if (EnginePerformanceOptions.DiagnosticRecords)
            {
                _recorder.CommitOperation((Id: world.WorldInstanceId, Tick: committed.WorldTick,
                    Players: committed.Players.Count, Events: committed.Events.Count),
                    static v => $"worldId={v.Id} committedTick={v.Tick} players={v.Players} events={v.Events}");
            }
            else _recorder.CommitOperation($"worldId={world.WorldInstanceId} " +
                $"committedTick={committed.WorldTick} players={committed.Players.Count} " +
                $"events={committed.Events.Count}");
            int recorded = 0;
            foreach (WorldGameplayEvent e in committed.Events)
            {
                // Always retain classified crush details, even if an unusually
                // busy tick has exhausted the ordinary event diagnostic budget.
                if (++recorded > 12 && e.Kind is not (WorldGameplayEventKind.PlayerCrushed or WorldGameplayEventKind.PlayerDied or WorldGameplayEventKind.PlayerRespawned)) continue;
                if (EnginePerformanceOptions.DiagnosticRecords)
                    _recorder.RecordActivity("world.event", e, FormatEvent, committed.WorldTick);
                else _recorder.RecordActivity("world.event", FormatEvent(e), committed.WorldTick);
            }
        });
    }

    private static string FormatEvent(WorldGameplayEvent e) => $"id={e.WorldInstanceId}/" +
                    $"{e.WorldTick}/{e.Sequence} kind={e.Kind} player={e.PlayerId} " +
                    $"collider={e.ColliderId?.ToString() ?? "none"} " +
                    $"trigger={e.TriggerId?.ToString() ?? "none"} " +
                    $"at=({Number(e.Position.X)},{Number(e.Position.Y)})" +
                    (e.Crush is { } crush ?
                        $" crushAxis={crush.Axis} pushingCollider={crush.PushingColliderId}" +
                        $" opposingCollider={crush.OpposingColliderId} crushTime={Number(crush.WorldTimeSeconds)}" : "");

    public void BeginGymStep(GameFlowController flow, MovementGymSession gym,
        LocalPlayerInput input)
    {
        Safely(() =>
        {
            var value = (Mode: gym.FaultSensitivity, Input: input, Seconds: gym.ElapsedSeconds);
            if (EnginePerformanceOptions.DiagnosticRecords)
                _recorder.BeginOperation("gym.step", value, static v => FormatGymAttempt(v.Mode, v.Input, v.Seconds));
            else _recorder.BeginOperation("gym.step", FormatGymAttempt(value.Mode, value.Input, value.Seconds));
        });
    }

    private static string FormatGymAttempt(SimulationFaultSensitivity mode, LocalPlayerInput input, double seconds) =>
        $"mode={mode} direction={input.Direction} jumpPress={input.JumpPressed} jumpRelease={input.JumpReleased} " +
        $"source={input.SourceId} elapsed={Number(seconds)}";

    public void CompleteGymStep(GameFlowController flow, MovementGymSession gym)
    {
        Safely(() =>
        {
            CaptureCommittedGym(flow, gym);
            if (EnginePerformanceOptions.DiagnosticRecords)
                _recorder.CommitOperation(gym.ElapsedSeconds, static seconds => $"gymElapsed={Number(seconds)}");
            else _recorder.CommitOperation($"gymElapsed={Number(gym.ElapsedSeconds)}");
        });
    }

    private void CaptureCommittedWorld(GameFlowController flow, WorldSession world,
        SimulationFaultSensitivity mode, WorldStepResult committed)
    {
        var captured = new WorldContext(CaptureCommon(flow, mode), world.WorldInstanceId, committed.WorldTick,
            committed.WorldTimeSeconds, committed.Players.Count, committed.Players.Take(4).ToArray());
        if (EnginePerformanceOptions.DiagnosticRecords) _recorder.UpdateCommittedState(captured, FormatWorld);
        else _recorder.UpdateCommittedState(FormatWorld(captured));
    }
    private sealed record WorldContext(SceneContext Scene, long Id, long Tick, double Seconds, int PlayerCount,
        WorldPlayerSnapshot[] Players);
    private static IReadOnlyDictionary<string, string> FormatWorld(WorldContext captured)
    {
        Dictionary<string, string> state = FormatCommon(captured.Scene);
        state["world.instanceId"] = captured.Id.ToString(CultureInfo.InvariantCulture);
        state["world.committedTick"] = captured.Tick.ToString(CultureInfo.InvariantCulture);
        state["world.timeSeconds"] = Number(captured.Seconds);
        state["world.playerCount"] = captured.PlayerCount.ToString(CultureInfo.InvariantCulture);
        int count = 0;
        foreach (WorldPlayerSnapshot player in captured.Players)
        {
            if (++count > 4) break;
            string prefix = $"player.{player.PlayerId}.";
            state[prefix + "bounds"] = $"({Number(player.Bounds.Left)},{Number(player.Bounds.Bottom)}," +
                $"{Number(player.Bounds.Right)},{Number(player.Bounds.Top)})";
            state[prefix + "velocity"] = $"({Number(player.Velocity.X)},{Number(player.Velocity.Y)})";
            state[prefix + "grounded"] = player.IsGrounded.ToString();
            state[prefix + "jumping"] = player.IsJumping.ToString();
            state[prefix + "movingSupportId"] = player.MovingSupportId?.ToString() ?? "none";
            state[prefix + "state"] = player.State.ToString();
            state[prefix + "windX"] = Number(player.Motion.EnvironmentVelocityX);
            state[prefix + "impulseX"] = Number(player.Motion.ImpulseVelocityX);
            state[prefix + "dashX"] = Number(player.Motion.DashVelocityX);
        }
        return state;
    }

    private void CaptureCommittedGym(GameFlowController flow, MovementGymSession gym)
    {
        var captured = new GymContext(CaptureCommon(flow, gym.FaultSensitivity), gym.ElapsedSeconds,
            gym.PlayerBounds, gym.PlayerVelocity, gym.MovingSupportId, gym.LastStepRecoveryCount);
        if (EnginePerformanceOptions.DiagnosticRecords) _recorder.UpdateCommittedState(captured, FormatGym);
        else _recorder.UpdateCommittedState(FormatGym(captured));
    }
    private readonly record struct GymContext(SceneContext Scene, double Seconds, Aabb Bounds,
        Vector2D Velocity, int? Support, int Recoveries);
    private static IReadOnlyDictionary<string, string> FormatGym(GymContext captured)
    {
        Dictionary<string, string> state = FormatCommon(captured.Scene);
        state["gym.elapsedSeconds"] = Number(captured.Seconds);
        state["gym.playerBounds"] = $"({Number(captured.Bounds.Left)}," +
            $"{Number(captured.Bounds.Bottom)},{Number(captured.Bounds.Right)}," +
            $"{Number(captured.Bounds.Top)})";
        state["gym.playerVelocity"] = $"({Number(captured.Velocity.X)}," +
            $"{Number(captured.Velocity.Y)})";
        state["gym.movingSupportId"] = captured.Support?.ToString() ?? "none";
        state["gym.lastStepRecoveryCount"] = captured.Recoveries.ToString();
        return state;
    }

    private static Dictionary<string, string> CommonState(GameFlowController flow,
        SimulationFaultSensitivity mode) => FormatCommon(CaptureCommon(flow, mode));
    private readonly record struct SceneContext(MajorSceneKind Kind, string Level, string Entrance,
        bool Suspended, bool Transition, SimulationFaultSensitivity Mode);
    private static SceneContext CaptureCommon(GameFlowController flow, SimulationFaultSensitivity mode) => new(
        flow.ActiveScene, flow.Scenes.ActiveDestination?.LevelId ?? "none", flow.Scenes.ActiveDestination?.EntranceId ?? "none",
        flow.IsGameplaySuspended, flow.IsTransitionActive, mode);
    private static Dictionary<string, string> FormatCommon(SceneContext scene) => new(StringComparer.Ordinal)
    {
        ["scene.kind"] = scene.Kind.ToString(),
        ["scene.levelId"] = scene.Level,
        ["scene.entranceId"] = scene.Entrance,
        ["scene.suspended"] = scene.Suspended.ToString(),
        ["scene.transitionActive"] = scene.Transition.ToString(),
        ["simulation.hostFaultMode"] = scene.Mode.ToString()
    };

    private sealed record WorldAttempt(long Id, SimulationFaultSensitivity Mode, WorldPlayerCommand[] Commands,
        CharacterStateRequest[] States, CharacterAbilityRequest[] Abilities, WorldMotionRequest[] Motions);
    private static string FormatAttempt(WorldAttempt attempt)
    {
            var detail = new StringBuilder(256);
            detail.Append("worldId=").Append(attempt.Id)
                .Append(" faultMode=").Append(attempt.Mode).Append(" commands=");
            foreach (WorldPlayerCommand command in attempt.Commands)
                detail.Append(" [P").Append(command.PlayerId).Append(' ')
                    .Append(command.Direction).Append(" press=").Append(command.JumpPressed)
                    .Append(" release=").Append(command.JumpReleased).Append(']');
            foreach (CharacterStateRequest request in attempt.States)
                detail.Append(" [state:").Append(request.ToString()).Append(']');
            foreach (CharacterAbilityRequest request in attempt.Abilities)
                detail.Append(" [ability:").Append(request.ToString()).Append(']');
            foreach (WorldMotionRequest request in attempt.Motions)
                detail.Append(" [motion:P").Append(request.PlayerId).Append(' ')
                    .Append(request.Kind).Append(' ')
                    .Append(Number(request.HorizontalSpeed)).Append(']');
        return detail.ToString();
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    // Optional diagnostics must not introduce a new way for gameplay to fail.
    private void Safely(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            LastCaptureError = e.GetType().Name + ": " + e.Message;
        }
    }
}
