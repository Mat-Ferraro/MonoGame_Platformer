namespace PlatformerSandbox.Simulation.Motion.Constraints;

/// <summary>
/// Designer-facing target for releasing the jump button before the held-jump apex.
/// MinimumHeight is the exact peak reached by the RELEASED ascent when release
/// happens at t=0 in the unobstructed reference trajectory.
/// </summary>
/// <param name="MinimumHeight">Must be finite, positive, and smaller than the held-jump height.</param>
public readonly record struct JumpReleaseConstraints(double MinimumHeight);
