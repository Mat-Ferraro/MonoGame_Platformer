using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Headless mathematical reference for the GPU back-rim calculation.
/// Points and light origins are normalized to the viewport, while the width is
/// in SCREEN pixels. Used to catch directional bleed and diagonal-width errors
/// without requiring a GPU in unit tests. This is presentation-only data.
/// </summary>
public static class BasicPolygonBackRimReference
{
    private static double SmoothStep(double edge0, double edge1, double x)
    {
        double t = Math.Clamp((x - edge0) / (edge1 - edge0), 0.0, 1.0);
        return t * t * (3 - 2 * t);
    }

    /// <returns>Rim intensity factor in [0, 1] before light falloff and color.</returns>
    public static double Sample(BasicShadowShape shape, LightPoint pixel, LightPoint light,
        bool lightInside, int viewportWidth, int viewportHeight, double rimWidthPixels = 4.5)
    {
        shape.Validate();
        if (viewportWidth <= 0 || viewportHeight <= 0 ||
            !double.IsFinite(rimWidthPixels) || rimWidthPixels <= 0 ||
            !double.IsFinite(pixel.X) || !double.IsFinite(pixel.Y) ||
            !double.IsFinite(light.X) || !double.IsFinite(light.Y))
            throw new ArgumentOutOfRangeException(nameof(rimWidthPixels));
        if (!shape.Contains(pixel)) return 0; // GPU uses wall texture alpha for this mask.

        LightPoint[] v = shape.Vertices();
        double px = pixel.X * viewportWidth, py = pixel.Y * viewportHeight;
        double lx = light.X * viewportWidth, ly = light.Y * viewportHeight;
        // Match the shader's BasicWallRimContext vertex-centroid convention;
        // a triangle's vertex centroid differs from its bounding-box center.
        double cx = 0, cy = 0;
        foreach (LightPoint vertex in v)
        {
            cx += vertex.X * viewportWidth;
            cy += vertex.Y * viewportHeight;
        }
        cx /= v.Length;
        cy /= v.Length;
        double insideDx = lx - cx, insideDy = ly - cy;
        double insideLength = Math.Sqrt(insideDx * insideDx + insideDy * insideDy);
        double minDistance = double.PositiveInfinity, nearestDark = double.PositiveInfinity;
        double bestFacing = 0;

        for (int i = 0; i < v.Length; i++)
        {
            LightPoint from = v[i], to = v[(i + 1) % v.Length];
            double ax = from.X * viewportWidth, ay = from.Y * viewportHeight;
            double bx = to.X * viewportWidth, by = to.Y * viewportHeight;
            double dx = bx - ax, dy = by - ay;
            double segmentLengthSquared = Math.Max(.0001, dx * dx + dy * dy);
            double t = Math.Clamp(((px - ax) * dx + (py - ay) * dy) / segmentLengthSquared, 0, 1);
            double qx = ax + t * dx, qy = ay + t * dy;
            double distance = Math.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
            // Positive cross-product winding in screen space => outward=(dy,-dx).
            double outwardX = dy / Math.Sqrt(segmentLengthSquared);
            double outwardY = -dx / Math.Sqrt(segmentLengthSquared);
            double towardX = lightInside ? insideDx : lx - qx;
            double towardY = lightInside ? insideDy : ly - qy;
            double towardLen = Math.Sqrt(towardX * towardX + towardY * towardY);
            double edgeFacing = towardLen > 2.0
                ? (outwardX * towardX + outwardY * towardY) / towardLen : 0;
            if (edgeFacing <= .15) nearestDark = Math.Min(nearestDark, distance);
            if (distance < minDistance)
            {
                minDistance = distance;
                bestFacing = edgeFacing;
            }
        }

        double width = Math.Max(.5, rimWidthPixels);
        if (nearestDark <= minDistance + 2.0 && nearestDark <= width + 2.0)
            return 0; // Dark adjacent side vetoes near-corner wrapping.
        double band = 1 - SmoothStep(0, width, minDistance);
        double facing = SmoothStep(.20, .50, bestFacing);
        return band * facing;
    }

    /// <summary>
    /// Headless reference of the per-light, per-wall visibility contract. The
    /// current wall is excluded by its compact ACTIVE shadow-slot index only;
    /// every other active caster can prevent its rim from seeing the light.
    /// Unlike ordinary wall-face shading, the wall's ReceivesShadows flag does
    /// not bypass this visibility test. Shadow casting remains per light.
    /// </summary>
    public static double SampleVisible(BasicShadowShape wall, LightPoint pixel, LightPoint light,
        bool lightInside, int viewportWidth, int viewportHeight,
        IReadOnlyList<BasicShadowShape> activeBlockers, int wallShadowSlot,
        bool lightCastsShadows, double rimWidthPixels = 4.5)
    {
        ArgumentNullException.ThrowIfNull(activeBlockers);
        if (wallShadowSlot < 0 || wallShadowSlot >= activeBlockers.Count ||
            !activeBlockers[wallShadowSlot].Equals(wall))
            throw new ArgumentOutOfRangeException(nameof(wallShadowSlot),
                "The wall must identify its exact active shadow slot.");
        double rim = Sample(wall, pixel, light, lightInside,
            viewportWidth, viewportHeight, rimWidthPixels);
        if (rim <= 0 || !lightCastsShadows) return rim;
        for (int i = 0; i < activeBlockers.Count; i++)
            if (i != wallShadowSlot && activeBlockers[i].Blocks(light, pixel))
                return 0;
        return rim;
    }
}
