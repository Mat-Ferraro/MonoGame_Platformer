using System;

namespace PlatformerSandbox.Gameplay.Presentation;

/// <summary>
/// Presentation-only visibility policy for development diagnostics.
/// This does not change simulation, inputs, events or collision debugging.
/// </summary>
public enum GameDebugHudMode
{
    Full,
    Compact,
    Hidden
}

public static class GameDebugHudModes
{
    public static GameDebugHudMode Next(GameDebugHudMode current) => current switch
    {
        GameDebugHudMode.Full => GameDebugHudMode.Compact,
        GameDebugHudMode.Compact => GameDebugHudMode.Hidden,
        GameDebugHudMode.Hidden => GameDebugHudMode.Full,
        _ => throw new ArgumentOutOfRangeException(nameof(current), current,
            "Unknown debug HUD mode.")
    };

    /// <summary>
    /// The art-focused LDtk scenes start with minimal clutter. The original
    /// test fixtures start with their full diagnostic panels available.
    /// </summary>
    public static GameDebugHudMode DefaultForScene(bool isLdtkLevel) =>
        isLdtkLevel ? GameDebugHudMode.Compact : GameDebugHudMode.Full;
}
