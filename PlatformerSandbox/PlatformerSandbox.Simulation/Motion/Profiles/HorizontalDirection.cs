namespace PlatformerSandbox.Simulation.Motion.Profiles;

/// <summary>
/// Digital input held constant for one horizontal reference trajectory.
/// An input change creates a new trajectory from the current sampled state.
/// </summary>
public enum HorizontalDirection
{
    Left = -1,
    Neutral = 0,
    Right = 1
}
