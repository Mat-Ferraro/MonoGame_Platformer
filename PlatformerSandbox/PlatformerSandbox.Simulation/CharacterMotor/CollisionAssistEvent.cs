namespace PlatformerSandbox.Simulation.CharacterMotor;

public enum CollisionAssistKind { CeilingCorner, GroundStep, LedgeCatch }

/// <summary>
/// An intentional gameplay correction. Not a physical contact or numerical recovery.
/// Events are published only with a successfully committed motor update.
/// </summary>
public readonly record struct CollisionAssistEvent(
    int ColliderId, double WorldTimeSeconds, double HorizontalCorrectionUnits,
    double VerticalCorrectionUnits = 0.0,
    CollisionAssistKind Kind = CollisionAssistKind.CeilingCorner);
