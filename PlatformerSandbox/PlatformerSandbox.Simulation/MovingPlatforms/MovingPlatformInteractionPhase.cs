namespace PlatformerSandbox.Simulation.MovingPlatforms;

/// <summary>Which portion of a proposed moving-platform interaction was resolved.</summary>
public enum MovingPlatformInteractionPhase
{
    FreeFlightCompleted,
    RidingCompleted,
    InterruptedByContact,
    DepartedSupport
}
