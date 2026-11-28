using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Simulation.Physics;

public enum PhysicalContactAxis { X, Y }

/// <summary>Axis-aligned active face constraint: Lower's velocity must be <= Upper's velocity.
/// Input edges must already have been validated by collision detection and filtering.</summary>
public readonly record struct AxisContactConstraint
{
    public AxisContactConstraint(PhysicalBodyId lower, PhysicalBodyId upper, PhysicalContactAxis axis)
    {
        _ = new PhysicalBodyPair(lower, upper);
        if (!Enum.IsDefined(axis)) throw new ArgumentOutOfRangeException(nameof(axis));
        Lower = lower; Upper = upper; Axis = axis;
    }
    public PhysicalBodyId Lower { get; }
    public PhysicalBodyId Upper { get; }
    public PhysicalContactAxis Axis { get; }
    public static AxisContactConstraint FromNormal(PhysicalBodyId a,PhysicalBodyId b,Vector2D normal)
    {
        if (normal == new Vector2D(1,0)) return new(b,a,PhysicalContactAxis.X);
        if (normal == new Vector2D(-1,0)) return new(a,b,PhysicalContactAxis.X);
        if (normal == new Vector2D(0,1)) return new(b,a,PhysicalContactAxis.Y);
        if (normal == new Vector2D(0,-1)) return new(a,b,PhysicalContactAxis.Y);
        throw new ArgumentException("Only exact AABB face normals are supported.",nameof(normal));
    }
}

/// <summary>Unsatisfiable instantaneous velocity bounds at a movable body. This is not yet a
/// gameplay crush event: the coupled solver must first validate time, contacts and legal escape.</summary>
public readonly record struct CompressionConstraint(PhysicalBodyId Body, PhysicalContactAxis Axis,
    PhysicalBodyId LowerAnchor, PhysicalBodyId UpperAnchor, double MinimumVelocity, double MaximumVelocity);

public static class CompressionConstraintGraph
{
    private readonly record struct Bound(PhysicalBodyId Anchor,double Velocity);
    private static double Component(Vector2D v,PhysicalContactAxis axis)=>axis==PhysicalContactAxis.X?v.X:v.Y;
    private static IEnumerable<PhysicalBodyId> Ordered(IEnumerable<PhysicalBodyId> ids)=>ids.OrderBy(x=>x.Kind).ThenBy(x=>x.LocalId);

    /// <summary>Find incompatible prescribed boundary velocities propagated through arbitrary
    /// active contact chains. A player's desired movement is NOT a prescribed velocity unless
    /// explicitly declared immovable by the caller. Anchors do not propagate through other anchors.
    /// No axis cross-coupling, position correction, death, filtering or movement is performed here.</summary>
    public static IReadOnlyList<CompressionConstraint> Find(IEnumerable<AxisContactConstraint> constraints,
        IReadOnlyDictionary<PhysicalBodyId,Vector2D> prescribed)
    {
        ArgumentNullException.ThrowIfNull(constraints);ArgumentNullException.ThrowIfNull(prescribed);
        var all=constraints.Distinct().ToArray();
        foreach(var edge in all) { _=new PhysicalBodyPair(edge.Lower,edge.Upper); if(!Enum.IsDefined(edge.Axis)) throw new ArgumentException("Invalid constraint axis."); }
        foreach(var anchor in prescribed.Keys) if(!anchor.IsValid) throw new ArgumentException("Invalid prescribed body.");
        var result=new List<CompressionConstraint>();
        foreach(var axis in new[]{PhysicalContactAxis.X,PhysicalContactAxis.Y})
        {
            var outgoing=new Dictionary<PhysicalBodyId,List<PhysicalBodyId>>();
            var incoming=new Dictionary<PhysicalBodyId,List<PhysicalBodyId>>();
            var nodes=new HashSet<PhysicalBodyId>();
            foreach(var e in all.Where(e=>e.Axis==axis))
            {
                nodes.Add(e.Lower);nodes.Add(e.Upper);
                if(!outgoing.TryGetValue(e.Lower,out var next)) outgoing.Add(e.Lower,next=new());next.Add(e.Upper);
                if(!incoming.TryGetValue(e.Upper,out var prev)) incoming.Add(e.Upper,prev=new());prev.Add(e.Lower);
            }
            // Fixed geometry and prescribed platforms cannot silently turn into movable participants.
            foreach(var id in nodes)
                if((id.Kind is PhysicalBodyKind.Static or PhysicalBodyKind.KinematicPlatform)&&!prescribed.ContainsKey(id))
                    throw new ArgumentException("Every fixed/prescribed boundary requires a velocity.");
            var lower=new Dictionary<PhysicalBodyId,Bound>();var upper=new Dictionary<PhysicalBodyId,Bound>();
            void Propagate(PhysicalBodyId source,Dictionary<PhysicalBodyId,List<PhysicalBodyId>> graph,
                Dictionary<PhysicalBodyId,Bound> bounds,bool minimum)
            {
                double value=Component(prescribed[source],axis);
                var visited=new HashSet<PhysicalBodyId>{source};var queue=new Queue<PhysicalBodyId>();queue.Enqueue(source);
                while(queue.Count>0)
                {
                    var current=queue.Dequeue();
                    if(!graph.TryGetValue(current,out var adjacent)) continue;
                    foreach(var next in Ordered(adjacent))
                    {
                        if(!visited.Add(next)||prescribed.ContainsKey(next)) continue;
                        if(!bounds.TryGetValue(next,out var old)||(minimum?value>old.Velocity:value<old.Velocity)) bounds[next]=new(source,value);
                        queue.Enqueue(next);
                    }
                }
            }
            foreach(var source in Ordered(prescribed.Keys.Where(nodes.Contains)))
            { Propagate(source,outgoing,lower,true);Propagate(source,incoming,upper,false); }
            foreach(var id in Ordered(nodes))
                if(lower.TryGetValue(id,out var lo)&&upper.TryGetValue(id,out var hi)&&lo.Velocity>hi.Velocity)
                    result.Add(new(id,axis,lo.Anchor,hi.Anchor,lo.Velocity,hi.Velocity));
        }
        return result.AsReadOnly();
    }
}
