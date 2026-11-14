namespace PlatformerSandbox.Simulation.Motion.Constraints;

/// <summary>
/// Designer-facing constraints for an unobstructed, single-axis horizontal
/// reference trajectory. Positive X means right, and distances are world units.
///
/// TimeToReverseSeconds is the TOTAL time for an uninterrupted change from
/// +MaximumSpeed to -MaximumSpeed (or vice versa). The implementation first
/// brakes to zero, then accelerates to full speed in the requested direction.
/// That construction requires TimeToReverseSeconds > TimeToMaximumSpeedSeconds.
///
/// This is a baseline full-strength digital direction model. Analog input,
/// external impulses, environmental constraints, and locomotion-state selection
/// belong to later modules and do not alter this mathematical contract.
/// </summary>
public readonly record struct HorizontalMotionConstraints(
    double MaximumSpeed,
    double TimeToMaximumSpeedSeconds,
    double TimeToStopSeconds,
    double TimeToReverseSeconds);
