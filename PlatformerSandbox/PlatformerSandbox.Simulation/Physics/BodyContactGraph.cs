using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.Collision.Detection;

namespace PlatformerSandbox.Simulation.Physics;

/// <summary>Symmetric stable key, scoped to a single world. Kinds prevent ID aliasing.</summary>
public readonly record struct PhysicalBodyPair
{
    public PhysicalBodyPair(PhysicalBodyId first, PhysicalBodyId second)
    {
        if (!first.IsValid || !second.IsValid || first == second) throw new ArgumentException("Invalid body pair.");
        if (Compare(first, second) > 0) (first, second) = (second, first);
        First = first; Second = second;
    }
    public PhysicalBodyId First { get; }
    public PhysicalBodyId Second { get; }
    public bool Contains(PhysicalBodyId id) => First == id || Second == id;
    internal static int Compare(PhysicalBodyId a, PhysicalBodyId b) => a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : a.LocalId.CompareTo(b.LocalId);
}

/// <summary>World-owned exclusion set. Caller validates safe separation before restoring a pair.</summary>
public sealed class BodyPairExclusions
{
    private readonly HashSet<PhysicalBodyPair> _pairs = new();
    public bool Contains(PhysicalBodyPair pair) => _pairs.Contains(pair);
    public void Set(PhysicalBodyPair pair, bool excluded)
    {
        if (!pair.First.IsValid || !pair.Second.IsValid || pair.First == pair.Second) throw new ArgumentException("Invalid body pair.");
        if (excluded) _pairs.Add(pair); else _pairs.Remove(pair);
    }
    public bool Has(PhysicalBodyId id) => _pairs.Any(p => p.Contains(id));
    public void Remove(PhysicalBodyId id) => _pairs.RemoveWhere(p => p.Contains(id));
    public void Clear() => _pairs.Clear();
}

/// <summary>Relative linear CCD query, not a motion resolver. The normal points from B towards A.
/// Initial penetration must be handled separately. No approximation of curved character motion.</summary>
public readonly record struct PhysicalBodySweepHit(PhysicalBodyId A, PhysicalBodyId B, double TimeOfImpact,
    Vector2D Normal, Aabb BoundsA, Aabb BoundsB);

public static class PhysicalBodySweep
{
    public static PhysicalBodySweepHit? Linear(PhysicalBodySnapshot a, Vector2D deltaA, PhysicalBodySnapshot b, Vector2D deltaB)
    {
        _ = new PhysicalBodyPair(a.Id, b.Id);
        var hit = AabbCollisionDetector.Sweep(a.Bounds, deltaA - deltaB, new StaticAabbCollider(b.Id.LocalId, b.Bounds));
        if (hit is null) return null;
        var h = hit.Value;
        return new PhysicalBodySweepHit(a.Id,b.Id,h.TimeOfImpact,h.Normal,
            a.Bounds.Translated(deltaA*h.TimeOfImpact),b.Bounds.Translated(deltaB*h.TimeOfImpact));
    }
}

/// <summary>One connected island of movable participants and its boundary anchors.
/// Static and prescribed kinematic platforms are anchors, never bridges between otherwise separate islands.</summary>
public sealed record PhysicalContactIsland(IReadOnlyList<PhysicalBodyId> Participants, IReadOnlyList<PhysicalBodyPair> Edges);

public static class BodyContactGraph
{
    private static bool Movable(PhysicalBodyId id) => id.Kind is PhysicalBodyKind.Dynamic or PhysicalBodyKind.KinematicCharacter;
    private static IEnumerable<PhysicalBodyId> Ordered(IEnumerable<PhysicalBodyId> ids) => ids.OrderBy(x => x.Kind).ThenBy(x => x.LocalId);
    /// <summary>Build from eligible contact edges supplied by a caller. This does not classify compression or solve constraints.</summary>
    public static IReadOnlyList<PhysicalContactIsland> Build(IEnumerable<PhysicalBodyPair> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        var all = edges.Distinct().OrderBy(p => p.First.Kind).ThenBy(p => p.First.LocalId).ThenBy(p => p.Second.Kind).ThenBy(p => p.Second.LocalId).ToArray();
        var adjacency = new Dictionary<PhysicalBodyId,List<PhysicalBodyPair>>();
        foreach (var e in all)
        {
            if (!e.First.IsValid || !e.Second.IsValid || e.First == e.Second) throw new ArgumentException("Invalid contact edge.");
            foreach (var id in new[]{e.First,e.Second}.Where(Movable))
            {
                if (!adjacency.TryGetValue(id, out var list)) adjacency.Add(id, list = new());
                list.Add(e);
            }
        }
        var seen = new HashSet<PhysicalBodyId>(); var result = new List<PhysicalContactIsland>();
        foreach (var root in Ordered(adjacency.Keys))
        {
            if (!seen.Add(root)) continue;
            var queue = new Queue<PhysicalBodyId>(); queue.Enqueue(root);
            var bodies = new List<PhysicalBodyId>(); var connected = new HashSet<PhysicalBodyPair>();
            while (queue.Count > 0)
            {
                var id = queue.Dequeue(); bodies.Add(id);
                foreach (var e in adjacency[id])
                {
                    connected.Add(e); var other = e.First == id ? e.Second : e.First;
                    if (Movable(other) && seen.Add(other)) queue.Enqueue(other);
                }
            }
            result.Add(new(Array.AsReadOnly(Ordered(bodies).ToArray()),Array.AsReadOnly(all.Where(connected.Contains).ToArray())));
        }
        return result.AsReadOnly();
    }
    /// <summary>Exact face adjacency only. No corner contacts or positive-area overlaps.
    /// Observational candidates, not authoritative constraints (may be filtered/non-solid).</summary>
    public static bool TouchingFaces(Aabb a, Aabb b) =>
        ((a.Right == b.Left || a.Left == b.Right) && a.Bottom < b.Top && a.Top > b.Bottom) ||
        ((a.Top == b.Bottom || a.Bottom == b.Top) && a.Left < b.Right && a.Right > b.Left);
}
