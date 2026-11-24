using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Normalized art-local Y-down contours, e.g. 0,0;1,0;1,1;0,1.
/// A pipe separates disjoint solid contours. Openings can be concave or between
/// components. Overlapping/touching rings and nested holes are rejected.</summary>
public static class ShadowContourAuthoring
{
    public static IReadOnlyList<LightPoint[]> Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16384)
            throw new ArgumentException("Custom ShadowVertices must be nonempty and bounded.");
        var result = new List<LightPoint[]>();
        int total = 0;
        foreach (string contour in value.Split('|'))
        {
            var vertices = contour.Split(';').Select(point =>
            {
                string[] xy = point.Split(',');
                if (xy.Length != 2 || !double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ||
                    !double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y) ||
                    !double.IsFinite(x) || !double.IsFinite(y) || x < 0 || x > 1 || y < 0 || y > 1)
                    throw new ArgumentException("ShadowVertices expects normalized x,y pairs in [0,1].");
                return new LightPoint(x,y);
            }).ToArray();
            total += vertices.Length;
            if (total > ProductionWallLightRouting.MaxOwnerEdges)
                throw new ArgumentException("Artwork exceeds its total shadow edge budget.");
            var hull = new LightingOccluder { Id = "custom", Vertices = vertices };
            hull.Validate();
            ShadowGeometry.Triangulate(hull);
            foreach (LightPoint[] prior in result)
            {
                var other = new LightingOccluder { Id = "prior", Vertices = prior };
                if (ShadowGeometry.Contains(other, vertices[0]) || ShadowGeometry.Contains(hull, prior[0]))
                    throw new ArgumentException("Custom contours must be disjoint, not nested.");
                for (int i=0;i<vertices.Length;i++)
                    for (int j=0;j<prior.Length;j++)
                        if (ShadowGeometry.EdgesTouchOrCross(vertices[i],vertices[(i+1)%vertices.Length],prior[j],prior[(j+1)%prior.Length]))
                            throw new ArgumentException("Custom contours must not touch or overlap.");
            }
            result.Add(vertices);
        }
        return result.AsReadOnly();
    }
}
