using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Hard-shadow visibility for a point light in a 2D world plane. Unlike projecting
/// independent shadow quads, the radial visibility boundary is a single, ordered,
/// star-shaped polygon: nearest-hit rays correctly handle concave corners,
/// intersecting shadow wedges, and changes in light position.
/// This is presentation geometry, independent of graphics and gameplay.
/// </summary>
public static class ShadowVisibility
{
    private const double AngularEpsilon = 0.00015;
    private const double IntersectionEpsilon = 1e-9;

    /// <summary>
    /// Computes points on the visible boundary in increasing polar-angle order.
    /// Render a triangle fan from <paramref name="light"/>.Position to consecutive
    /// boundary points, then use that fan as a per-light visibility mask.
    /// Circle samples avoid angular gaps when no blocker edge is nearby.
    /// Samples either side of each vertex preserve sharp silhouette transitions.
    /// </summary>
    public static IReadOnlyList<LightPoint> BuildPolygon(
        RadialLight light, IReadOnlyList<LightingOccluder> occluders,
        int radialSamples = 192, bool respectLitFaces = false)
    {
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(occluders);
        light.Validate();
        if (radialSamples is < 32 or > 512)
            throw new ArgumentOutOfRangeException(nameof(radialSamples));

        // Keep the mask slightly outside the light's actual falloff texture,
        // preventing the visibility polygon itself from clipping the soft rim.
        double radius = light.Radius * 1.02;
        var relevant = new List<(LightingOccluder Hull, bool ExitOnly, double Winding)>();
        var angles = new List<double>(radialSamples + 192);
        for (int i = 0; i < radialSamples; i++)
            angles.Add(i * (Math.PI * 2 / radialSamples));

        int extraAngles = 0;
        foreach (LightingOccluder hull in occluders)
        {
            if (hull is null) throw new ArgumentException("Null occluder.", nameof(occluders));
            hull.Validate();
            // Do not discard a caster just because the light origin overlaps it.
            // Rays originating inside a solid must stop at the first EXIT edge;
            // otherwise the light suddenly shines through the entire object
            // as soon as the source crosses its silhouette.
            if (!WithinLightBounds(hull, light.Position, radius)) continue;
            relevant.Add((hull, respectLitFaces && ReceiverWallShadowPolicy.StartsAtExit(light, hull),
                Math.Sign(ShadowGeometry.SignedDoubleArea(hull.Vertices))));
            // Bounded adaptive corner refinement: very complex scenes still have
            // a predictable cost and can use the radial samples as a fallback.
            foreach (LightPoint vertex in hull.Vertices)
            {
                if (extraAngles >= 768) break;
                double angle = Math.Atan2(vertex.Y - light.Position.Y,
                    vertex.X - light.Position.X);
                angles.Add(Normalize(angle - AngularEpsilon));
                angles.Add(Normalize(angle));
                angles.Add(Normalize(angle + AngularEpsilon));
                extraAngles += 3;
            }
        }

        angles.Sort();
        var points = new List<LightPoint>(angles.Count);
        double previous = double.NegativeInfinity;
        foreach (double angle in angles)
        {
            if (angle - previous < 1e-10) continue;
            previous = angle;
            double dx = Math.Cos(angle), dy = Math.Sin(angle);
            double reach = radius;
            foreach (var entry in relevant)
            {
                LightPoint[] vertices = entry.Hull.Vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    LightPoint a = vertices[i];
                    LightPoint b = vertices[(i + 1) % vertices.Length];
                    if (entry.ExitOnly && !ReceiverWallShadowPolicy.IsExitEdge(a, b, dx, dy, entry.Winding))
                        continue;
                    if (RaySegmentIntersection(light.Position, dx, dy, a, b,
                        out double hit) && hit < reach)
                        reach = hit;
                }
            }
            points.Add(new LightPoint(light.Position.X + dx * reach,
                light.Position.Y + dy * reach));
        }
        return points;
    }

    /// <summary>Returns whether the straight line from the light to the sample is clear.</summary>
    public static bool CanIlluminate(RadialLight light, LightPoint sample,
        IReadOnlyList<LightingOccluder> occluders, bool respectLitFaces = false)
    {
        ArgumentNullException.ThrowIfNull(light);
        ArgumentNullException.ThrowIfNull(occluders);
        light.Validate(); sample.Validate();
        double dx = sample.X - light.Position.X, dy = sample.Y - light.Position.Y;
        double reach = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(reach) || reach > light.Radius) return false;
        if (reach < IntersectionEpsilon) return true;
        dx /= reach; dy /= reach;
        foreach (LightingOccluder hull in occluders)
        {
            if (hull is null) throw new ArgumentException("Null occluder.", nameof(occluders));
            hull.Validate();
            if (!WithinLightBounds(hull, light.Position, reach)) continue;
            // Samples inside a solid cannot be reached from outside it, but a
            // ray originating inside can travel within that solid until it
            // crosses an edge. The same rule is used by the visibility fan.
            bool exitOnly = respectLitFaces && ReceiverWallShadowPolicy.StartsAtExit(light, hull);
            if (!exitOnly && !SourceInsideOrOnBoundary(hull, light.Position) &&
                ShadowGeometry.Contains(hull, sample)) return false;
            LightPoint[] vertices = hull.Vertices;
            double winding = Math.Sign(ShadowGeometry.SignedDoubleArea(vertices));
            for (int i = 0; i < vertices.Length; i++)
            {
                LightPoint a = vertices[i], b = vertices[(i + 1) % vertices.Length];
                if (exitOnly && !ReceiverWallShadowPolicy.IsExitEdge(a, b, dx, dy, winding)) continue;
                if (RaySegmentIntersection(light.Position, dx, dy, a, b, out double hit) &&
                    hit <= reach - IntersectionEpsilon) return false;
            }
        }
        return true;
    }

    internal static bool RaySegmentIntersection(LightPoint origin, double dx, double dy,
        LightPoint a, LightPoint b, out double distance)
    {
        double ex = b.X - a.X, ey = b.Y - a.Y;
        double det = dx * ey - dy * ex;
        distance = double.PositiveInfinity;
        if (Math.Abs(det) < 1e-12) return false;
        double ox = a.X - origin.X, oy = a.Y - origin.Y;
        double rayT = (ox * ey - oy * ex) / det;
        double segmentT = (ox * dy - oy * dx) / det;
        if (rayT < IntersectionEpsilon || segmentT < -1e-9 || segmentT > 1 + 1e-9)
            return false;
        distance = rayT;
        return true;
    }

    private static bool WithinLightBounds(LightingOccluder hull, LightPoint source, double radius)
    {
        // Exact nearest point on the occluder's AABB; no missed long edges with
        // both endpoints outside the light radius.
        double minX = double.PositiveInfinity, maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity, maxY = double.NegativeInfinity;
        foreach (LightPoint p in hull.Vertices)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        double x = Math.Clamp(source.X, minX, maxX);
        double y = Math.Clamp(source.Y, minY, maxY);
        double dx = x - source.X, dy = y - source.Y;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static bool SourceInsideOrOnBoundary(LightingOccluder hull, LightPoint source)
    {
        if (ShadowGeometry.Contains(hull, source)) return true;
        foreach (LightPoint p in hull.Vertices)
            if (ShadowGeometry.DistanceSquared(source, p) < 1e-12) return true;
        // Avoid noisy sliver masks while dragging a light exactly over an edge.
        for (int i = 0; i < hull.Vertices.Length; i++)
        {
            LightPoint a = hull.Vertices[i], b = hull.Vertices[(i + 1) % hull.Vertices.Length];
            double ex = b.X - a.X, ey = b.Y - a.Y;
            double t = Math.Clamp(((source.X - a.X) * ex +
                (source.Y - a.Y) * ey) / (ex * ex + ey * ey), 0, 1);
            double sx = source.X - (a.X + t * ex), sy = source.Y - (a.Y + t * ey);
            if (sx * sx + sy * sy < 1e-12) return true;
        }
        return false;
    }

    private static double Normalize(double angle)
    {
        const double twoPi = Math.PI * 2;
        angle %= twoPi;
        return angle < 0 ? angle + twoPi : angle;
    }
}
