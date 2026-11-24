namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Immutable, GPU-independent decisions for the production material fast path.
/// Neutral map / unobstructed visibility textures are byte-for-byte equivalent
/// to previously clearing full-screen render targets to their constant colors.
/// This planner deliberately never caches dynamically generated visibility.
/// </summary>
public readonly record struct ProductionMaterialMapPassPlan(
    bool CaptureNormal, bool CaptureEdge);

public static class ProductionMaterialPassPlanner
{
    /// <summary>Only rasterize authored maps; missing maps use 1x1 fallbacks.</summary>
    public static ProductionMaterialMapPassPlan Maps(bool hasNormalDraw, bool hasEdgeDraw) =>
        new(hasNormalDraw, hasEdgeDraw);

    /// <summary>
    /// A backlight rim must still consider occlusion even if the front face
    /// opts out of receiving shadows. Shadow budgets and casting rules apply
    /// independently. Caller still checks for nearby other-wall occluders.
    /// </summary>
    public static bool NeedsVisibility(bool receivesProjectedShadows, bool backRim,
        bool lightCastsShadows, bool withinShadowBudget) =>
        (receivesProjectedShadows || backRim) && lightCastsShadows && withinShadowBudget;

    /// <summary>Rims require the binary visibility gate; only front/receiver
    /// lighting consumes filtered visibility. Behind slots have no front light.</summary>
    public static bool FilterVisibility(bool backRim, double softnessPixels) =>
        !backRim && softnessPixels > 0;
}
