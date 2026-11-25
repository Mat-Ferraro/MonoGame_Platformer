using System;
using System.Collections.Generic;
using System.Linq;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Bounded world-space visibility cache. A hit requires exact source
/// geometry, exact ordered caster vertices, and identical entry/exit policies.
/// No camera, color, intensity or bloom information participates in a radial fan.
/// Caller supplies validated scene snapshots. Stored vertices are defensive copies.
/// </summary>
public sealed class PersistentVisibilityGeometryCache
{
    private const int Limit = 128;
    private readonly LinkedList<Entry> _entries = new();
    private long _frame;
    public void BeginFrame() => _frame++;
    private sealed record Hull(LightPoint[] Vertices, bool ExitOnly);
    private sealed record Entry(LightPoint Position, double Radius, int Samples,
        bool RespectLitFaces, Hull[] Hulls, IReadOnlyList<LightPoint> Polygon, long Frame);
    public void Clear() { _entries.Clear(); _frame = 0; }
    private static bool Matches(Entry entry, RadialLight light, IReadOnlyList<LightingOccluder> blockers,
        int samples, bool respectLitFaces)
    {
        if (entry.Position != light.Position || entry.Radius != light.Radius ||
            entry.Samples != samples || entry.RespectLitFaces != respectLitFaces ||
            entry.Hulls.Length != blockers.Count) return false;
        for (int i = 0; i < blockers.Count; i++)
        {
            var hull = blockers[i];
            if (entry.Hulls[i].ExitOnly != (respectLitFaces && ReceiverWallShadowPolicy.StartsAtExit(light, hull)) ||
                !entry.Hulls[i].Vertices.AsSpan().SequenceEqual(hull.Vertices)) return false;
        }
        return true;
    }
    public bool TryGet(RadialLight light, IReadOnlyList<LightingOccluder> blockers,
        int samples, bool respectLitFaces, out IReadOnlyList<LightPoint> polygon)
    {
        for (var node = _entries.First; node is not null; node = node.Next)
        {
            Entry entry = node.Value;
            if (entry.Frame == _frame || !Matches(entry, light, blockers, samples, respectLitFaces)) continue;
            _entries.Remove(node); _entries.AddFirst(node);
            polygon = entry.Polygon; return true;
        }
        polygon = Array.Empty<LightPoint>(); return false;
    }
    public void Store(RadialLight light, IReadOnlyList<LightingOccluder> blockers,
        int samples, bool respectLitFaces, IReadOnlyList<LightPoint> polygon)
    {
        // Do not fill the bounded cache with duplicates from one frame when
        // frame-local reuse is independently disabled.
        foreach (Entry entry in _entries)
            if (Matches(entry, light, blockers, samples, respectLitFaces)) return;
        var hulls = blockers.Select(h => new Hull(h.CaptureValidatedVertices(),
            respectLitFaces && ReceiverWallShadowPolicy.StartsAtExit(light, h))).ToArray();
        if (_entries.Count >= Limit) _entries.RemoveLast();
        _entries.AddFirst(new Entry(light.Position, light.Radius, samples,
            respectLitFaces, hulls, Array.AsReadOnly(polygon.ToArray()), _frame));
    }
}
