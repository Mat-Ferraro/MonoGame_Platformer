using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Bounded cache for one immutable lighting frame. The caller must clear
/// at each new frame. Light samples with a shared logical ID are NOT equivalent
/// unless their full light values match. Hull identity includes transformed geometry.</summary>
public sealed class FrameVisibilityGeometryCache
{
    private const int MaxEntries = 256;
    private readonly Dictionary<(RadialLight Light, int Samples, bool LitFaces), List<Entry>> _entries = new();
    private int _count;
    private sealed record Entry(LightingOccluder[] Blockers, IReadOnlyList<LightPoint> Polygon);
    public void Clear() { _entries.Clear(); _count = 0; }
    public bool TryGet(RadialLight light, IReadOnlyList<LightingOccluder> blockers,
        int samples, bool litFaces, out IReadOnlyList<LightPoint> polygon)
    {
        if (_entries.TryGetValue((light, samples, litFaces), out var entries))
            foreach (var entry in entries)
            {
                if (entry.Blockers.Length != blockers.Count) continue;
                bool same = true;
                for (int i = 0; i < blockers.Count; i++)
                    if (!ReferenceEquals(entry.Blockers[i], blockers[i])) { same = false; break; }
                if (same) { polygon = entry.Polygon; return true; }
            }
        polygon = Array.Empty<LightPoint>();
        return false;
    }
    public void Store(RadialLight light, IReadOnlyList<LightingOccluder> blockers,
        int samples, bool litFaces, IReadOnlyList<LightPoint> polygon)
    {
        if (_count >= MaxEntries) return;
        var key = (light, samples, litFaces);
        if (!_entries.TryGetValue(key, out var entries)) _entries.Add(key, entries = new());
        var copy = new LightingOccluder[blockers.Count];
        for (int i = 0; i < copy.Length; i++) copy[i] = blockers[i];
        entries.Add(new Entry(copy, polygon));
        _count++;
    }
}
