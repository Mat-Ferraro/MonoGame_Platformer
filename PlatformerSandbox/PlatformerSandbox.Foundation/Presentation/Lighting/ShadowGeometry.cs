using PlatformerSandbox.Foundation.Performance;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

public sealed record LightingOccluder
{
    public const int MaxVertices = 64;
    public string Id { get; init; } = "";
    // Compound contours have distinct IDs but share one artwork owner.
    public string? OwnerId { get; init; }
    public string Owner => OwnerId ?? Id;
    /// <summary>Presentation depth plane that owns this occluder.</summary>
    public string LayerId { get; init; } = "PLAYER";
    // Only visible, lit artwork supplies face metadata. Geometry-only blockers
    // retain strict solid occlusion; motion/compound copies preserve metadata.
    public SceneLightingLayer? LitFaceLayer { get; init; }
    public double LitFaceDepthZ { get; init; }
    public LightPoint[] Vertices { get; init; } = Array.Empty<LightPoint>();
    // A record's vertices array is publicly mutable. A reference-only cache would
    // silently accept edited or invalid geometry. Verify the exact saved values.
    private sealed class ValidatedHull { public LightPoint[]? Vertices; }
    private static readonly ConditionalWeakTable<LightingOccluder, ValidatedHull> ValidationCache = new();
    public void Validate()
    {
        if (!EnginePerformanceOptions.LightingValidation || Vertices is null)
        { ValidateExhaustive(); return; }
        var cached = ValidationCache.GetValue(this, static _ => new ValidatedHull());
        lock (cached)
        {
            if (cached.Vertices is not null && Vertices.AsSpan().SequenceEqual(cached.Vertices)) return;
            ValidateExhaustive();
            cached.Vertices = (LightPoint[])Vertices.Clone();
        }
    }
    // Internal cache keys can share this private defensive copy. Never expose
    // it publicly, or permit consumers to modify it.
    internal LightPoint[] CaptureValidatedVertices()
    {
        if (!EnginePerformanceOptions.LightingValidation) { ValidateExhaustive(); return (LightPoint[])Vertices.Clone(); }
        Validate();
        var cached = ValidationCache.GetValue(this, static _ => new ValidatedHull());
        lock (cached) return cached.Vertices!;
    }
    public void ValidateExhaustive()
    {
        if (string.IsNullOrWhiteSpace(Id) || (OwnerId is not null && string.IsNullOrWhiteSpace(OwnerId)) || !SceneLightingLayer.IsId(LayerId) ||
            Vertices is null || Vertices.Length is < 3 or > MaxVertices)
            throw new ArgumentException("An occluder requires a name and 3–64 polygon vertices.");
        if (LitFaceLayer is not null)
        {
            LitFaceLayer.Validate();
            if (LitFaceLayer.Id != LayerId || !double.IsFinite(LitFaceDepthZ) ||
                LitFaceDepthZ is < -100 or > 100)
                throw new ArgumentException("Invalid occluder face routing metadata.");
        }
        foreach (LightPoint p in Vertices) p.Validate();
        if (Math.Abs(ShadowGeometry.SignedDoubleArea(Vertices)) < 1e-9)
            throw new ArgumentException("Occluder polygon has zero area.");
        for (int i = 0; i < Vertices.Length; i++)
        {
            LightPoint a = Vertices[i], b = Vertices[(i + 1) % Vertices.Length];
            if (ShadowGeometry.DistanceSquared(a, b) < 1e-12)
                throw new ArgumentException("Occluder edges cannot have zero length.");
            LightPoint c = Vertices[(i + 2) % Vertices.Length];
            double turn = (b.X-a.X)*(c.Y-b.Y)-(b.Y-a.Y)*(c.X-b.X);
            if (Math.Abs(turn) <= 1e-10 &&
                (b.X-a.X)*(c.X-b.X)+(b.Y-a.Y)*(c.Y-b.Y) < 0)
                throw new ArgumentException("Occluder adjacent edges cannot backtrack.");
            for (int j = i + 1; j < Vertices.Length; j++)
            {
                if ((j == i + 1) || (i == 0 && j == Vertices.Length - 1)) continue;
                if (ShadowGeometry.EdgesTouchOrCross(a, b, Vertices[j], Vertices[(j + 1) % Vertices.Length]))
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

    public static bool EdgesTouchOrCross(LightPoint a, LightPoint b, LightPoint c, LightPoint d)
    {
        const double eps = 1e-10;
        bool On(LightPoint p, LightPoint q, LightPoint r) => Math.Abs(Cross(p,q,r)) <= eps &&
            r.X >= Math.Min(p.X,q.X)-eps && r.X <= Math.Max(p.X,q.X)+eps &&
            r.Y >= Math.Min(p.Y,q.Y)-eps && r.Y <= Math.Max(p.Y,q.Y)+eps;
        double x = Cross(a,b,c), y = Cross(a,b,d), z = Cross(c,d,a), w = Cross(c,d,b);
        return ((x > eps && y < -eps || x < -eps && y > eps) &&
                (z > eps && w < -eps || z < -eps && w > eps)) ||
            On(a,b,c) || On(a,b,d) || On(c,d,a) || On(c,d,b);
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
            if (IsOccludedBy(source, sample, blocker)) return true;
        return false;
    }

    public static bool IsOccludedBy(LightPoint source, LightPoint sample, LightingOccluder blocker)
    {
        source.Validate(); sample.Validate();
        bool sourceInside = Contains(blocker, source);
        bool sampleInside = Contains(blocker, sample);
        if (sourceInside != sampleInside) return true;
        for (int i = 0; i < blocker.Vertices.Length; i++)
            if (SegmentsIntersect(source, sample, blocker.Vertices[i],
                blocker.Vertices[(i + 1) % blocker.Vertices.Length])) return true;
        return false;
    }

    /// <summary>
    /// Triangulates a simple occluder polygon (including concave outlines) without
    /// inventing depth information. Used to draw a complete light blocker into each
    /// per-light mask and to draw its visible stone surface as a separate receiver.
    /// </summary>
    private sealed class CachedTriangles
    {
        public LightPoint[]? Snapshot;
        public IReadOnlyList<LightTriangle>? Triangles;
    }
    private static readonly ConditionalWeakTable<LightPoint[], CachedTriangles> TriangleCache = new();

    public static IReadOnlyList<LightTriangle> Triangulate(LightingOccluder occluder)
    {
        ArgumentNullException.ThrowIfNull(occluder);
        // Scene-owned vertex arrays are immutable after publication; moving hulls
        // publish a fresh array. Weak keys do not retain unloaded scenes.
        if (occluder.Vertices is null || string.IsNullOrWhiteSpace(occluder.Id) ||
            !SceneLightingLayer.IsId(occluder.LayerId))
            throw new ArgumentException("Invalid occluder identity or vertices.");
        var cache = TriangleCache.GetValue(occluder.Vertices, _ => new CachedTriangles());
        lock (cache)
        {
            if (cache.Snapshot is not null && cache.Triangles is not null &&
                occluder.Vertices.AsSpan().SequenceEqual(cache.Snapshot)) return cache.Triangles;
            var result = TriangulateCore(occluder);
            cache.Snapshot = (LightPoint[])occluder.Vertices.Clone();
            cache.Triangles = result;
            return result;
        }
    }

    private static IReadOnlyList<LightTriangle> TriangulateCore(LightingOccluder occluder)
    {
        ArgumentNullException.ThrowIfNull(occluder);
        occluder.Validate();
        LightPoint[] points = occluder.Vertices;
        int winding = SignedDoubleArea(points) > 0 ? 1 : -1;
        var remaining = new List<int>();
        for (int i = 0; i < points.Length; i++) remaining.Add(i);
        // Remove redundant straight-line points, but keep actual authored edges
        // for visibility and rim lighting. Reversed overlapping edges are invalid.
        for (int i = remaining.Count - 1; i >= 0 && remaining.Count > 3; i--)
        {
            int k = i % remaining.Count;
            LightPoint a = points[remaining[(k + remaining.Count - 1) % remaining.Count]];
            LightPoint b = points[remaining[k]];
            LightPoint c = points[remaining[(k + 1) % remaining.Count]];
            if (Math.Abs(Cross(a,b,c)) <= 1e-10 &&
                (b.X-a.X)*(c.X-b.X)+(b.Y-a.Y)*(c.Y-b.Y) >= 0)
                remaining.RemoveAt(k);
        }
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
        return result.AsReadOnly();
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
