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
        ? "PREVIOUS SESSION ENDED UNEXPECTEDLY - CTRL SHIFT F12 EXPORT REPORT"
        : "CTRL SHIFT F12: EXPORT REPORT ZIP");

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
            _recorder.CommitOperation($"worldId={world.WorldInstanceId} " +
                $"committedTick={committed.WorldTick} players={committed.Players.Count} " +
                $"events={committed.Events.Count}");
            int recorded = 0;
            foreach (WorldGameplayEvent e in committed.Events)
            {
                if (++recorded > 12) break;
                _recorder.RecordActivity("world.event", $"id={e.WorldInstanceId}/" +
                    $"{e.WorldTick}/{e.Sequence} kind={e.Kind} player={e.PlayerId} " +
                    $"collider={e.ColliderId?.ToString() ?? "none"} " +
                    $"trigger={e.TriggerId?.ToString() ?? "none"} " +
                    $"at=({Number(e.Position.X)},{Number(e.Position.Y)})", committed.WorldTick);
            }
        });
    }

    public void BeginGymStep(GameFlowController flow, MovementGymSession gym,
        LocalPlayerInput input)
    {
        Safely(() => _recorder.BeginOperation("gym.step",
            $"mode={gym.FaultSensitivity} direction={input.Direction} " +
            $"jumpPress={input.JumpPressed} jumpRelease={input.JumpReleased} " +
            $"source={input.SourceId} elapsed={Number(gym.ElapsedSeconds)}"));
    }

    public void CompleteGymStep(GameFlowController flow, MovementGymSession gym)
    {
        Safely(() =>
        {
            CaptureCommittedGym(flow, gym);
            _recorder.CommitOperation($"gymElapsed={Number(gym.ElapsedSeconds)}");
        });
    }

    private void CaptureCommittedWorld(GameFlowController flow, WorldSession world,
        SimulationFaultSensitivity mode, WorldStepResult committed)
    {
        Dictionary<string, string> state = CommonState(flow, mode);
        state["world.instanceId"] = world.WorldInstanceId.ToString(CultureInfo.InvariantCulture);
        state["world.committedTick"] = committed.WorldTick.ToString(CultureInfo.InvariantCulture);
        state["world.timeSeconds"] = Number(committed.WorldTimeSeconds);
        state["world.playerCount"] = committed.Players.Count.ToString(CultureInfo.InvariantCulture);
        int count = 0;
        foreach (WorldPlayerSnapshot player in committed.Players)
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
        _recorder.UpdateCommittedState(state);
    }

    private void CaptureCommittedGym(GameFlowController flow, MovementGymSession gym)
    {
        Dictionary<string, string> state = CommonState(flow, gym.FaultSensitivity);
        state["gym.elapsedSeconds"] = Number(gym.ElapsedSeconds);
        state["gym.playerBounds"] = $"({Number(gym.PlayerBounds.Left)}," +
            $"{Number(gym.PlayerBounds.Bottom)},{Number(gym.PlayerBounds.Right)}," +
            $"{Number(gym.PlayerBounds.Top)})";
        state["gym.playerVelocity"] = $"({Number(gym.PlayerVelocity.X)}," +
            $"{Number(gym.PlayerVelocity.Y)})";
        state["gym.movingSupportId"] = gym.MovingSupportId?.ToString() ?? "none";
        state["gym.lastStepRecoveryCount"] = gym.LastStepRecoveryCount.ToString();
        _recorder.UpdateCommittedState(state);
    }

    private static Dictionary<string, string> CommonState(GameFlowController flow,
        SimulationFaultSensitivity mode) => new(StringComparer.Ordinal)
    {
        ["scene.kind"] = flow.ActiveScene.ToString(),
        ["scene.levelId"] = flow.Scenes.ActiveDestination?.LevelId ?? "none",
        ["scene.entranceId"] = flow.Scenes.ActiveDestination?.EntranceId ?? "none",
        ["scene.suspended"] = flow.IsGameplaySuspended.ToString(),
        ["scene.transitionActive"] = flow.IsTransitionActive.ToString(),
        ["simulation.hostFaultMode"] = mode.ToString()
    };

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
