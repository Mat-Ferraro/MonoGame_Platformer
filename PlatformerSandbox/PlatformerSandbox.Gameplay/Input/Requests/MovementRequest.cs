using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Gameplay.Input.Requests;

/// <summary>
/// The type of gameplay intent submitted for one fixed simulation update.
/// Jump edges are discrete events; a horizontal request is a continuous
/// directional intent for that update. None of these mutate the motor.
/// </summary>
public enum MovementRequestKind
{
    Horizontal = 0,
    JumpPress = 1,
    JumpRelease = 2
}

/// <summary>
/// Stateless, typed proposal made by an input adapter or future ability.
/// SourceId must be positive and stable. Higher priority wins for horizontal
/// control; equal priorities use the smallest SourceId.
/// </summary>
public readonly record struct MovementRequest(
    MovementRequestKind Kind, int SourceId, int Priority,
    HorizontalDirection Direction)
{
    public static MovementRequest Horizontal(
        int sourceId, HorizontalDirection direction, int priority = 0) =>
        new(MovementRequestKind.Horizontal, sourceId, priority, direction);

    public static MovementRequest JumpPress(int sourceId, int priority = 0) =>
        new(MovementRequestKind.JumpPress, sourceId, priority,
            HorizontalDirection.Neutral);

    public static MovementRequest JumpRelease(int sourceId, int priority = 0) =>
        new(MovementRequestKind.JumpRelease, sourceId, priority,
            HorizontalDirection.Neutral);
}

/// <summary>Stable local source IDs; these are input origins, not PlayerIds.</summary>
public static class MovementRequestSources
{
    public const int Keyboard = 1;
    public const int GamepadOne = 2;
}
