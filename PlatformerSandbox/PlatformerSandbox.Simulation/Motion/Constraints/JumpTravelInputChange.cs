using PlatformerSandbox.Simulation.Motion.Profiles;

namespace PlatformerSandbox.Simulation.Motion.Constraints;

/// <summary>
/// A reference input change at an exact time since jump takeoff.
/// The first change must occur at t=0; changes are strictly time-ordered.
/// This is a design/test input schedule, not a polling or input-buffer service.
/// </summary>
public readonly record struct JumpTravelInputChange(
    double TimeSinceTakeoffSeconds,
    HorizontalDirection Direction);
