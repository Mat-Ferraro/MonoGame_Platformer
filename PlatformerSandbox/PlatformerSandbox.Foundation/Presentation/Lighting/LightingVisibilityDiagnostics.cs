using System;
using System.Collections.Generic;
using System.Linq;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

public enum AuthoredLightVisibility { Disabled, OutsideView, BudgetCulled, Active }

/// <summary>Diagnostic only. Active means submitted to the light planner;
/// it does not imply an unoccluded contribution at every receiver pixel.</summary>
public static class LightingVisibilityDiagnostics
{
    public static AuthoredLightVisibility Classify(RadialLight light,
        LightingViewport viewport, IReadOnlyList<RadialLight> selected,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(selected);
        if (!enabled) return AuthoredLightVisibility.Disabled;
        if (!viewport.InfluencesViewport(light)) return AuthoredLightVisibility.OutsideView;
        return selected.Any(item => item.Id == light.Id)
            ? AuthoredLightVisibility.Active : AuthoredLightVisibility.BudgetCulled;
    }
}
