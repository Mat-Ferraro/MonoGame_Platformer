namespace PlatformerSandbox.Simulation.CharacterMotor;

/// <summary>Optional, designer-bounded gameplay help; never numerical repair.</summary>
public sealed class CollisionAssistancePolicy
{
    public static CollisionAssistancePolicy Disabled { get; } = new(0.0, 0);

    public double CeilingCornerCorrectionUnits { get; }
    public int MaximumCeilingCorrectionsPerJump { get; }
    public double GroundStepHeightUnits { get; }
    public double GroundStepInwardUnits { get; }
    public double LedgeCatchHorizontalUnits { get; }
    public double LedgeCatchVerticalUnits { get; }

    public bool CeilingCornersEnabled => CeilingCornerCorrectionUnits > 0.0 &&
        MaximumCeilingCorrectionsPerJump > 0;
    public bool GroundStepEnabled => GroundStepHeightUnits > 0.0 &&
        GroundStepInwardUnits > 0.0;
    public bool LedgeCatchEnabled => LedgeCatchHorizontalUnits > 0.0 &&
        LedgeCatchVerticalUnits > 0.0;
    public bool AnyEnabled => CeilingCornersEnabled || GroundStepEnabled || LedgeCatchEnabled;

    /// <param name="ceilingCornerCorrectionUnits">Maximum sideways underside correction.</param>
    /// <param name="maximumCeilingCorrectionsPerJump">Separate from collision-event budget.</param>
    /// <param name="groundStepHeightUnits">Maximum height of a small static step.</param>
    /// <param name="groundStepInwardUnits">Forward toe placement onto the step.</param>
    /// <param name="ledgeCatchHorizontalUnits">Maximum lateral distance for a falling ledge catch.</param>
    /// <param name="ledgeCatchVerticalUnits">Maximum height above a ledge when catching it.</param>
    public CollisionAssistancePolicy(
        double ceilingCornerCorrectionUnits, int maximumCeilingCorrectionsPerJump = 2,
        double groundStepHeightUnits = 0.0, double groundStepInwardUnits = 0.0,
        double ledgeCatchHorizontalUnits = 0.0, double ledgeCatchVerticalUnits = 0.0)
    {
        Validate(ceilingCornerCorrectionUnits, nameof(ceilingCornerCorrectionUnits));
        Validate(groundStepHeightUnits, nameof(groundStepHeightUnits));
        Validate(groundStepInwardUnits, nameof(groundStepInwardUnits));
        Validate(ledgeCatchHorizontalUnits, nameof(ledgeCatchHorizontalUnits));
        Validate(ledgeCatchVerticalUnits, nameof(ledgeCatchVerticalUnits));
        if (maximumCeilingCorrectionsPerJump < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumCeilingCorrectionsPerJump));
        CeilingCornerCorrectionUnits = ceilingCornerCorrectionUnits;
        MaximumCeilingCorrectionsPerJump = maximumCeilingCorrectionsPerJump;
        GroundStepHeightUnits = groundStepHeightUnits;
        GroundStepInwardUnits = groundStepInwardUnits;
        LedgeCatchHorizontalUnits = ledgeCatchHorizontalUnits;
        LedgeCatchVerticalUnits = ledgeCatchVerticalUnits;
    }

    private static void Validate(double value, string name)
    {
        if (!double.IsFinite(value) || value < 0.0)
            throw new ArgumentOutOfRangeException(name);
    }
}
