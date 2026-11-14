namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>
/// Exact analytical time at which a body ceases overlapping any connected,
/// same-height supporting floor. BodyLeftAtDeparture is an X coordinate,
/// used to align the event boundary against floating-point root rounding.
/// </summary>
public readonly record struct GroundSupportDeparture(
    double TimeSeconds, double BodyLeftAtDeparture);
