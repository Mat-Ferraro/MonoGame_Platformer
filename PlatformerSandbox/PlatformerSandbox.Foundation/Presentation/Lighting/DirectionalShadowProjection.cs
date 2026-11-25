using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Bounded world-plane shadow projection for simple convex or concave 2D casters.
/// A filled copy of the authored hull and its swept edge quads form the
/// Minkowski sum of the hull with a finite directed segment. This means a
/// pointed obelisk projects a pointed silhouette rather than an arbitrary
/// full-screen rectangular wedge. The projection is presentation-only.
/// </summary>
public static class DirectionalShadowProjection
{
    /// <summary>Conservative culling against the swept world AABB, not the original caster bounds.</summary>
    public static bool IntersectsViewport(LightingOccluder caster, LightPoint direction,
        double reachWorld, LightingViewport view)
    {
        view.Validate();
        double m = Math.Sqrt(direction.X*direction.X + direction.Y*direction.Y);
        if (m < 1e-6 || !double.IsFinite(reachWorld) || reachWorld < .25 || reachWorld > 30)
            throw new ArgumentOutOfRangeException(nameof(reachWorld));
        double dx = direction.X*reachWorld/m, dy = direction.Y*reachWorld/m;
        double minX=double.PositiveInfinity,minY=double.PositiveInfinity;
        double maxX=double.NegativeInfinity,maxY=double.NegativeInfinity;
        foreach (var v in caster.Vertices)
        { minX=Math.Min(minX,v.X); minY=Math.Min(minY,v.Y); maxX=Math.Max(maxX,v.X); maxY=Math.Max(maxY,v.Y); }
        double halfW=view.Width/(2*view.PixelsPerUnit), halfH=view.Height/(2*view.PixelsPerUnit);
        // A 24px guard preserves the potential soft-shadow filtering band.
        double guard=24/view.PixelsPerUnit;
        return minX+Math.Min(0,dx)<=view.Focus.X+halfW+guard &&
            maxX+Math.Max(0,dx)>=view.Focus.X-halfW-guard &&
            minY+Math.Min(0,dy)<=view.Focus.Y+halfH+guard &&
            maxY+Math.Max(0,dy)>=view.Focus.Y-halfH-guard;
    }

    /// <summary>Returns world-space triangles; every successive triple is one triangle.
    /// The input polygon must already be the authored simple shadow hull.</summary>
    public static IReadOnlyList<LightPoint> Triangles(
        LightingOccluder caster, LightPoint direction, double reachWorld, bool exitOnly = false)
    {
        ArgumentNullException.ThrowIfNull(caster);
        direction.Validate();
        double m = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        if (m < 0.000001 || !double.IsFinite(reachWorld) || reachWorld < .25 || reachWorld > 30)
            throw new ArgumentOutOfRangeException(nameof(reachWorld));
        LightPoint travel = direction * (reachWorld / m);
        LightPoint[] vertices = caster.Vertices;
        var output = new List<LightPoint>(3 * (vertices.Length - 2) + 6 * vertices.Length);
        // Preserve the source hull so its immediate-contact shadow follows its
        // authored silhouette, including non-rectangular polygon tops.
        if (!exitOnly)
        {
            foreach (LightTriangle triangle in ShadowGeometry.Triangulate(caster))
            {
                output.Add(triangle.A); output.Add(triangle.B); output.Add(triangle.C);
            }
        }
        // Extrusion is finite. Drawing all edge quads is deliberately safe for
        // simple silhouettes: the union of the source and swept boundary is the
        // finite extrusion, including any genuinely unoccluded concave opening.
        double winding = Math.Sign(ShadowGeometry.SignedDoubleArea(vertices));
        for (int i = 0; i < vertices.Length; i++)
        {
            LightPoint a = vertices[i];
            LightPoint b = vertices[(i + 1) % vertices.Length];
            if (exitOnly && !ReceiverWallShadowPolicy.IsExitEdge(a, b,
                travel.X/reachWorld, travel.Y/reachWorld, winding)) continue;
            LightPoint c = a + travel;
            LightPoint d = b + travel;
            output.Add(a); output.Add(b); output.Add(c);
            output.Add(b); output.Add(d); output.Add(c);
        }
        return output;
    }
}
