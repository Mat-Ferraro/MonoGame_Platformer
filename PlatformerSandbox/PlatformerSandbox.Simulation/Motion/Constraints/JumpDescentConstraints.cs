namespace PlatformerSandbox.Simulation.Motion.Constraints;

/// <summary>
/// Determines how a cut jump descends after reaching its new apex.
/// This does not control collisions, gravity outside the reference flight,
/// or runtime landing behavior.
/// </summary>
public enum JumpDescentPolicy
{
    /// <summary>
    /// Retain the held jump's analytical fall-displacement law:
    /// fall displacement = held.Height * (fallElapsed / held.FallTimeSeconds)^p.
    /// This shortens the cut jump's fall time as its peak height decreases.
    /// For p=2 it preserves the held jump's downward gravity exactly.
    /// </summary>
    PreserveReferenceFallLaw = 0,

    /// <summary>
    /// Reach takeoff height in the specified duration measured from the CUT apex.
    /// Retains the held jump's power-curve exponent, but changes descent dynamics.
    /// </summary>
    SpecifyDescentDuration = 1
}

/// <summary>
/// Designer-facing descent policy for a jump interrupted by early release.
/// For PreserveReferenceFallLaw, DescentDurationSeconds must be null.
/// For SpecifyDescentDuration it must be finite and positive.
/// A default value selects PreserveReferenceFallLaw.
/// </summary>
public readonly record struct JumpDescentConstraints(
    JumpDescentPolicy Policy = JumpDescentPolicy.PreserveReferenceFallLaw,
    double? DescentDurationSeconds = null);
