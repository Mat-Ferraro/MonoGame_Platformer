using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>GPU-independent dimensions, conservative coverage and exact field routing for PO10A.</summary>
public static class LightingRenderWorkPolicy
{
    public static int ScaleDimension(int pixels, int percent)
    {
        if (pixels <= 0 || percent is not (25 or 50 or 75 or 100))
            throw new ArgumentOutOfRangeException();
        return (int)Math.Max(1, ((long)pixels * percent + 99) / 100);
    }

    public static int MaskCapacity(int width, int height, long byteBudget, int maximum)
    {
        if (width <= 0 || height <= 0 || byteBudget < 0 || maximum < 0)
            throw new ArgumentOutOfRangeException();
        // Divide in stages to avoid overflowing even for an invalid giant target.
        return (int)Math.Min(maximum, byteBudget / 4 / width / height);
    }

    public static ProductionMaterialRegion MapRegion(int x, int y, int width, int height,
        int sourceWidth, int sourceHeight, int destinationWidth, int destinationHeight, int padding = 0)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0 || destinationWidth <= 0 || destinationHeight <= 0 || padding < 0)
            throw new ArgumentOutOfRangeException();
        if (width <= 0 || height <= 0) return default;
        double sx = destinationWidth / (double)sourceWidth, sy = destinationHeight / (double)sourceHeight;
        int left = (int)Math.Clamp(Math.Floor(x * sx) - padding, 0, destinationWidth);
        int top = (int)Math.Clamp(Math.Floor(y * sy) - padding, 0, destinationHeight);
        int right = (int)Math.Clamp(Math.Ceiling(((double)x + width) * sx) + padding, 0, destinationWidth);
        int bottom = (int)Math.Clamp(Math.Ceiling(((double)y + height) * sy) + padding, 0, destinationHeight);
        return right <= left || bottom <= top ? default : new(left, top, right-left, bottom-top);
    }

    public static bool SameRouting(IReadOnlyList<RadialLight> lights, SceneLightingLayer a, SceneLightingLayer b)
    {
        if (a.ReceivesShadows != b.ReceivesShadows) return false;
        foreach (var light in lights)
            if (LightLayerRouting.Illuminates(light,a) != LightLayerRouting.Illuminates(light,b)) return false;
        return true;
    }
}
