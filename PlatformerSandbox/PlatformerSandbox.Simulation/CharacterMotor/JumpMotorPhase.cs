namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>Current motion authority of one headless jump motor.</summary>
public enum JumpMotorPhase
{
    /// <summary>Following the exact, unobstructed designer reference until its first interruption or end.</summary>
    Reference = 0,
    /// <summary>Following fresh, constant-acceleration analytical segments after interruption/end.</summary>
    FreeMotion = 1,
    /// <summary>Stop-on-impact policy has stopped motion until an external controller creates a new motor.</summary>
    Halted = 2
}
