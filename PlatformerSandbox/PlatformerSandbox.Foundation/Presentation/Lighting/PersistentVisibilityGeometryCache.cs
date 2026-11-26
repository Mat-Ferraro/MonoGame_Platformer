using System;
using System.Collections.Generic;
using System.Linq;
using PlatformerSandbox.Foundation.Performance;

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
    private readonly Dictionary<int, List<LinkedListNode<Entry>>> _index = new();
    private static int Fingerprint(RadialLight light, IReadOnlyList<LightingOccluder> blockers, int samples, bool litFaces) =>
        HashCode.Combine(light.Position, light.Radius, samples, litFaces,
            VisibilityGeometryFingerprint.BlockersHash(light, blockers, litFaces));
    // One bounded bucket pool; neither lookup path allocates an iterator.
    private readonly Stack<List<LinkedListNode<Entry>>> _bucketPool = new();
    private void ReleaseBucket(List<LinkedListNode<Entry>> bucket)
    {
        bucket.Clear();
        if (_bucketPool.Count < Limit) _bucketPool.Push(bucket);
    }
    private LinkedListNode<Entry>? FindMatch(int hash, RadialLight light,
        IReadOnlyList<LightingOccluder> blockers, int samples, bool litFaces, bool priorOnly)
    {
        if (EnginePerformanceOptions.HashedVisibility)
        {
            if (_index.TryGetValue(hash, out var bucket))
                for (int i = 0; i < bucket.Count; i++)
                {
                    var node = bucket[i];
                    if ((!priorOnly || node.Value.Frame != _frame) && Matches(node.Value, light, blockers, samples, litFaces))
                        return node;
                }
        }
        else
        {
            for (var node = _entries.First; node is not null; node = node.Next)
                if ((!priorOnly || node.Value.Frame != _frame) && Matches(node.Value, light, blockers, samples, litFaces))
                    return node;
        }
        return null;
    }
    private void RemoveLast()
    {
        var node = _entries.Last!;
        if (_index.TryGetValue(node.Value.Hash, out var bucket))
        {
            bucket.Remove(node);
            if (bucket.Count == 0) { _index.Remove(node.Value.Hash); ReleaseBucket(bucket); }
        }
        _entries.RemoveLast();
    }
    public void BeginFrame() => _frame++;
    private sealed record Hull(LightPoint[] Vertices, bool ExitOnly);
    private sealed record Entry(LightPoint Position, double Radius, int Samples,
        bool RespectLitFaces, Hull[] Hulls, IReadOnlyList<LightPoint> Polygon, long Frame, int Hash);
    public void Clear()
    {
        foreach (var bucket in _index.Values) ReleaseBucket(bucket);
        _entries.Clear(); _index.Clear(); _frame = 0;
    }
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
                !(EnginePerformanceOptions.HashedVisibility ? VisibilityGeometryFingerprint.Same(entry.Hulls[i].Vertices, hull) : entry.Hulls[i].Vertices.AsSpan().SequenceEqual(hull.Vertices))) return false;
        }
        return true;
    }
    public bool TryGet(RadialLight light, IReadOnlyList<LightingOccluder> blockers,
        int samples, bool respectLitFaces, out IReadOnlyList<LightPoint> polygon)
    {
        var node = FindMatch(EnginePerformanceOptions.HashedVisibility ? Fingerprint(light, blockers, samples, respectLitFaces) : 0,
            light, blockers, samples, respectLitFaces, priorOnly: true);
        if (node is not null)
        {
            _entries.Remove(node); _entries.AddFirst(node);
            polygon = node.Value.Polygon; return true;
        }
        polygon = Array.Empty<LightPoint>(); return false;
    }
    public void Store(RadialLight light, IReadOnlyList<LightingOccluder> blockers,
        int samples, bool respectLitFaces, IReadOnlyList<LightPoint> polygon)
    {
        // Do not fill the bounded cache with duplicates from one frame when
        // frame-local reuse is independently disabled.
        int hash = EnginePerformanceOptions.HashedVisibility ? Fingerprint(light, blockers, samples, respectLitFaces) : 0;
        if (FindMatch(hash, light, blockers, samples, respectLitFaces, priorOnly: false) is not null) return;
        var hulls = blockers.Select(h => new Hull(h.CaptureValidatedVertices(),
            respectLitFaces && ReceiverWallShadowPolicy.StartsAtExit(light, h))).ToArray();
        if (_entries.Count >= Limit) RemoveLast();
        var added = _entries.AddFirst(new Entry(light.Position, light.Radius, samples,
            respectLitFaces, hulls, Array.AsReadOnly(polygon.ToArray()), _frame, hash));
        if (EnginePerformanceOptions.HashedVisibility)
        {
            if (!_index.TryGetValue(hash, out var bucket)) _index.Add(hash, bucket = _bucketPool.Count > 0 ? _bucketPool.Pop() : new());
            bucket.Add(added);
        }
    }
}
