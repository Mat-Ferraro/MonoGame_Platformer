using System;
using System.Collections.Generic;
using PlatformerSandbox.Gameplay.Input.Requests;
using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Gameplay.Input.Local;

/// <summary>One explicit local device is active for the prototype player.</summary>
public enum LocalInputSource
{
    Keyboard = 0,
    GamepadOne = 1
}

/// <summary>
/// Physical input normalized by the host into simple buttons + one analog axis.
/// IsConnected=false is a neutral/unavailable device; no ghost press is allowed.
/// This type contains no MonoGame or device-specific value types.
/// </summary>
public readonly record struct LocalDeviceInput(
    bool IsConnected, bool LeftDown, bool RightDown,
    bool JumpDown, double StickX = 0.0);

/// <summary>
/// Fixed-step action snapshot for one local player. SourceId identifies the
/// source, not the player or character; no abilities or physics values here.
/// </summary>
public readonly record struct LocalPlayerInput(
    int SourceId, HorizontalDirection Direction,
    bool JumpPressed, bool JumpReleased, bool JumpHeld, bool IsConnected);

/// <summary>Pure configurable translation from physical controls into direction.</summary>
public static class LocalInputNormalizer
{
    public static HorizontalDirection Direction(
        LocalDeviceInput state, double stickThreshold = 0.50)
    {
        Validate(state, stickThreshold);
        if (!state.IsConnected)
            return HorizontalDirection.Neutral;

        bool left = state.LeftDown || state.StickX <= -stickThreshold;
        bool right = state.RightDown || state.StickX >= stickThreshold;
        return left == right ? HorizontalDirection.Neutral
            : left ? HorizontalDirection.Left : HorizontalDirection.Right;
    }

    public static void Validate(LocalDeviceInput state, double stickThreshold)
    {
        if (!double.IsFinite(stickThreshold) ||
            stickThreshold <= 0.0 || stickThreshold > 1.0)
            throw new ArgumentOutOfRangeException(nameof(stickThreshold),
                "Stick activation threshold must be in (0, 1].");
        if (!double.IsFinite(state.StickX) ||
            state.StickX < -1.0 || state.StickX > 1.0)
            throw new ArgumentOutOfRangeException(nameof(state),
                "Stick X must be finite and in [-1, 1].");
    }
}

/// <summary>
/// Production adapter from normalized local commands to typed A08 requests.
/// The synthetic gym harness is not part of the ordinary input path.
/// </summary>
public static class LocalInputRequestAdapter
{
    public static void Populate(List<MovementRequest> destination, LocalPlayerInput input)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (input.SourceId <= 0 || !Enum.IsDefined(input.Direction))
            throw new ArgumentOutOfRangeException(nameof(input));
        destination.Clear();
        destination.Add(MovementRequest.Horizontal(input.SourceId, input.Direction));
        if (input.JumpPressed)
            destination.Add(MovementRequest.JumpPress(input.SourceId));
        if (input.JumpReleased)
            destination.Add(MovementRequest.JumpRelease(input.SourceId));
    }
}

/// <summary>
/// One-source-per-player routing for A11 Phase 1. Press and release edges are
/// emitted exactly once per observed fixed-tick transition. Switching sources
/// seeds the held state; disconnect clears held input and requires a release
/// before another press can be generated on reconnection.
/// </summary>
public sealed class LocalPlayerInputRouter
{
    private readonly double _stickThreshold;
    private bool _previousJumpDown;
    private bool _requireJumpRelease;

    public LocalPlayerInputRouter(double stickThreshold = 0.50)
    {
        LocalInputNormalizer.Validate(default, stickThreshold);
        _stickThreshold = stickThreshold;
    }

    public LocalInputSource Source { get; private set; } = LocalInputSource.Keyboard;

    public int SourceId => Source == LocalInputSource.Keyboard
        ? MovementRequestSources.Keyboard : MovementRequestSources.GamepadOne;

    /// <summary>
    /// Source changes are an explicit input ownership change. The caller
    /// provides the current physical snapshot so a held button never becomes
    /// a false fresh press on the next fixed update.
    /// </summary>
    public void SelectSource(LocalInputSource source, LocalDeviceInput currentState)
    {
        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source));
        LocalInputNormalizer.Validate(currentState, _stickThreshold);
        Source = source;
        _previousJumpDown = currentState.IsConnected && currentState.JumpDown;
        _requireJumpRelease = !currentState.IsConnected;
    }

    public LocalPlayerInput Sample(
        LocalDeviceInput keyboard, LocalDeviceInput gamepad)
    {
        LocalDeviceInput state = Source == LocalInputSource.Keyboard
            ? keyboard : gamepad;
        // Validate before updating any state. Invalid samples are rejected
        // without changing edge detection or movement intent.
        HorizontalDirection direction = LocalInputNormalizer.Direction(
            state, _stickThreshold);
        if (!state.IsConnected)
        {
            bool released = _previousJumpDown;
            _previousJumpDown = false;
            _requireJumpRelease = true;
            return new LocalPlayerInput(SourceId, HorizontalDirection.Neutral,
                false, released, false, false);
        }

        if (_requireJumpRelease)
        {
            if (!state.JumpDown)
                _requireJumpRelease = false;
            _previousJumpDown = false;
            return new LocalPlayerInput(SourceId, direction,
                false, false, false, true);
        }

        bool pressed = state.JumpDown && !_previousJumpDown;
        bool releasedThisTick = !state.JumpDown && _previousJumpDown;
        _previousJumpDown = state.JumpDown;
        return new LocalPlayerInput(SourceId, direction,
            pressed, releasedThisTick, state.JumpDown, true);
    }
}
