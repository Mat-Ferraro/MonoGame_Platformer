using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

public sealed record LightingOccluder
{
    public string Id { get; init; } = "";
    public LightPoint[] Vertices { get; init; } = Array.Empty<LightPoint>();
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Vertices is null || Vertices.Length is < 3 or > 64)
            throw new ArgumentException("An occluder requires a name and 3–64 polygon vertices.");
        foreach (LightPoint p in Vertices) p.Validate();
        if (Math.Abs(ShadowGeometry.SignedDoubleArea(Vertices)) < 1e-9)
            throw new ArgumentException("Occluder polygon has zero area.");
        for (int i = 0; i < Vertices.Length; i++)
        {
            LightPoint a = Vertices[i], b = Vertices[(i + 1) % Vertices.Length];
            if (ShadowGeometry.DistanceSquared(a, b) < 1e-12)
                throw new ArgumentException("Occluder edges cannot have zero length.");
            for (int j = i + 1; j < Vertices.Length; j++)
            {
                if ((j == i + 1) || (i == 0 && j == Vertices.Length - 1)) continue;
                if (ShadowGeometry.SegmentsIntersect(a, b, Vertices[j], Vertices[(j + 1) % Vertices.Length]))
                    throw new ArgumentException("Self-intersecting occluder polygon.");
            }
        }
    }
}

/// <summary>World-space hard shadow quad extruded from a light-facing polygon edge.</summary>
public readonly record struct ShadowQuad(LightPoint A, LightPoint B, LightPoint FarB, LightPoint FarA);

/// <summary>A counterclockwise or clockwise triangle within a validated simple occluder hull.</summary>
public readonly record struct LightTriangle(LightPoint A, LightPoint B, LightPoint C);

public static class ShadowGeometry
{
    public static double DistanceSquared(LightPoint a, LightPoint b) =>
        (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y);

    public static double SignedDoubleArea(IReadOnlyList<LightPoint> points)
    {
        double sum = 0;
        for (int i = 0; i < points.Count; i++)
        {
            LightPoint a = points[i], b = points[(i + 1) % points.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }
        return sum;
    }

    private static double Cross(LightPoint a, LightPoint b, LightPoint c) =>
        (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);

    internal static bool SegmentsIntersect(LightPoint a, LightPoint b, LightPoint c, LightPoint d)
    {
        const double epsilon = 1e-10;
        double abC = Cross(a, b, c), abD = Cross(a, b, d);
        double cdA = Cross(c, d, a), cdB = Cross(c, d, b);
        return (abC * abD < -epsilon && cdA * cdB < -epsilon);
    }

    public static bool Contains(LightingOccluder occluder, LightPoint p)
    {
        bool inside = false;
        var vertices = occluder.Vertices;
        for (int i = 0, j = vertices.Length - 1; i < vertices.Length; j = i++)
        {
            LightPoint a = vertices[i], b = vertices[j];
            if (((a.Y > p.Y) != (b.Y > p.Y)) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                inside = !inside;
        }
        return inside;
    }

    public static bool IsOccluded(LightPoint source, LightPoint sample,
        IReadOnlyList<LightingOccluder> blockers)
    {
        source.Validate(); sample.Validate();
        foreach (LightingOccluder blocker in blockers)
        {
            if (Contains(blocker, source)) continue; // undefined inside hull: safe unshadowed fallback
            if (Contains(blocker, sample)) return true;
            for (int i = 0; i < blocker.Vertices.Length; i++)
                if (SegmentsIntersect(source, sample, blocker.Vertices[i],
                    blocker.Vertices[(i + 1) % blocker.Vertices.Length])) return true;
        }
        return false;
    }

    /// <summary>
    /// Triangulates a simple occluder polygon (including concave outlines) without
    /// inventing depth information. Used to draw a complete light blocker into each
    /// per-light mask and to draw its visible stone surface as a separate receiver.
    /// </summary>
    public static IReadOnlyList<LightTriangle> Triangulate(LightingOccluder occluder)
    {
        ArgumentNullException.ThrowIfNull(occluder);
        occluder.Validate();
        LightPoint[] points = occluder.Vertices;
        int winding = SignedDoubleArea(points) > 0 ? 1 : -1;
        var remaining = new List<int>();
        for (int i = 0; i < points.Length; i++) remaining.Add(i);
        var result = new List<LightTriangle>(points.Length - 2);
        int safety = points.Length * points.Length;
        while (remaining.Count > 3 && safety-- > 0)
        {
            bool clipped = false;
            for (int i = 0; i < remaining.Count; i++)
            {
                int ia = remaining[(i + remaining.Count - 1) % remaining.Count];
                int ib = remaining[i];
                int ic = remaining[(i + 1) % remaining.Count];
                LightPoint a = points[ia], b = points[ib], c = points[ic];
                if (winding * Cross(a, b, c) <= 1e-10) continue;
                bool containsOtherVertex = false;
                foreach (int other in remaining)
                {
                    if (other == ia || other == ib || other == ic) continue;
                    if (WithinTriangle(points[other], a, b, c, winding))
                    {
                        containsOtherVertex = true;
                        break;
                    }
                }
                if (containsOtherVertex) continue;
                result.Add(new LightTriangle(a, b, c));
                remaining.RemoveAt(i);
                clipped = true;
                break;
            }
            if (!clipped)
                throw new ArgumentException("Occluder polygon cannot be triangulated. Check overlapping/collinear geometry.", nameof(occluder));
        }
        if (remaining.Count != 3)
            throw new ArgumentException("Occluder triangulation did not converge.", nameof(occluder));
        result.Add(new LightTriangle(points[remaining[0]], points[remaining[1]], points[remaining[2]]));
        return result;
    }

    private static bool WithinTriangle(LightPoint p, LightPoint a, LightPoint b, LightPoint c, int winding)
    {
        const double epsilon = 1e-10;
        return winding * Cross(a, b, p) >= -epsilon &&
               winding * Cross(b, c, p) >= -epsilon &&
               winding * Cross(c, a, p) >= -epsilon;
    }

    /// <summary>Projects light-facing edges. Light-inside-hull is deliberately skipped.</summary>
    public static IReadOnlyList<ShadowQuad> Project(RadialLight light,
        LightingOccluder occluder, double lengthMultiplier = 1.5)
    {
        light.Validate(); occluder.Validate();
        if (!double.IsFinite(lengthMultiplier) || lengthMultiplier <= 0 || lengthMultiplier > 8)
            throw new ArgumentOutOfRangeException(nameof(lengthMultiplier));
        if (Contains(occluder, light.Position)) return Array.Empty<ShadowQuad>();
        var result = new List<ShadowQuad>();
        int n = occluder.Vertices.Length;
        bool ccw = SignedDoubleArea(occluder.Vertices) > 0;
        for (int i = 0; i < n; i++)
        {
            LightPoint a = occluder.Vertices[i], b = occluder.Vertices[(i + 1) % n];
            LightPoint midpoint = new((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            double dx = b.X - a.X, dy = b.Y - a.Y;
            // Outward normals for CCW polygon: (dy,-dx); opposite for CW.
            double normalX = ccw ? dy : -dy;
            double normalY = ccw ? -dx : dx;
            if (normalX * (light.Position.X - midpoint.X) +
                normalY * (light.Position.Y - midpoint.Y) <= 1e-10) continue;
            LightPoint farA = Extend(a, light.Position, light.Radius * lengthMultiplier);
            LightPoint farB = Extend(b, light.Position, light.Radius * lengthMultiplier);
            result.Add(new ShadowQuad(a, b, farB, farA));
        }
        return result;
    }

    private static LightPoint Extend(LightPoint edge, LightPoint light, double projection)
    {
        double dx = edge.X - light.X, dy = edge.Y - light.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-8 || !double.IsFinite(length)) return edge;
        return new LightPoint(edge.X + dx / length * projection,
            edge.Y + dy / length * projection);
    }
}
