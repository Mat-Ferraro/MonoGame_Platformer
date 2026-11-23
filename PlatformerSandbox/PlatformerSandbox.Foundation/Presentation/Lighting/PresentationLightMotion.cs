using System;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Opt-in visual light motion. This NEVER changes simulation geometry.</summary>
public enum PresentationLightMotionAxis { Static, Horizontal, Vertical }

/// <summary>
/// Authoring-time light animation. Position is evaluated from its immutable
/// authored anchor, so pausing, resetting and frame-rate changes cannot drift.
/// DistanceWorld is the maximum displacement either side of the anchor.
/// </summary>
public readonly record struct PresentationLightMotion(
    PresentationLightMotionAxis Axis, double DistanceWorld, double CyclesPerSecond)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Axis) || !double.IsFinite(DistanceWorld) ||
            !double.IsFinite(CyclesPerSecond) ||
            DistanceWorld < 0 || DistanceWorld > 10 ||
            CyclesPerSecond < 0 || CyclesPerSecond > 2 ||
            (Axis != PresentationLightMotionAxis.Static &&
             (DistanceWorld <= 0 || CyclesPerSecond <= 0)))
            throw new ArgumentOutOfRangeException(nameof(Axis), "Invalid presentation light motion.");
    }

    public LightPoint At(LightPoint anchor, double elapsedSeconds)
    {
        Validate(); anchor.Validate();
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (Axis == PresentationLightMotionAxis.Static) return anchor;
        // Smooth, deterministic, periodic motion; no accumulated position error.
        double phase = (elapsedSeconds * CyclesPerSecond) % 1.0;
        double offset = DistanceWorld * Math.Sin(2 * Math.PI * phase);
        return Axis == PresentationLightMotionAxis.Horizontal
            ? anchor with { X = anchor.X + offset }
            : anchor with { Y = anchor.Y + offset };
    }
}
