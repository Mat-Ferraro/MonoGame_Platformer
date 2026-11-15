using Microsoft.Xna.Framework.Input;
using PlatformerSandbox.Gameplay.Input.Local;

namespace PlatformerSandbox.Input;

/// <summary>
/// Host-only, configurable local bindings. No MonoGame types cross into
/// Gameplay or Simulation; full rebinding UI/persistence is a later task.
/// </summary>
public sealed class MonoGameLocalInputBindings
{
    public Keys LeftKey { get; init; } = Keys.A;
    public Keys LeftAlternateKey { get; init; } = Keys.Left;
    public Keys RightKey { get; init; } = Keys.D;
    public Keys RightAlternateKey { get; init; } = Keys.Right;
    public Keys JumpKey { get; init; } = Keys.Space;
    public Buttons GamepadJumpButton { get; init; } = Buttons.A;

    public LocalDeviceInput CaptureKeyboard(KeyboardState keyboard) => new(
        IsConnected: true,
        LeftDown: keyboard.IsKeyDown(LeftKey) || keyboard.IsKeyDown(LeftAlternateKey),
        RightDown: keyboard.IsKeyDown(RightKey) || keyboard.IsKeyDown(RightAlternateKey),
        JumpDown: keyboard.IsKeyDown(JumpKey));

    public LocalDeviceInput CaptureGamepad(GamePadState gamepad) => new(
        IsConnected: gamepad.IsConnected,
        LeftDown: gamepad.DPad.Left == ButtonState.Pressed,
        RightDown: gamepad.DPad.Right == ButtonState.Pressed,
        JumpDown: gamepad.IsButtonDown(GamepadJumpButton),
        StickX: gamepad.IsConnected ? gamepad.ThumbSticks.Left.X : 0.0);
}
