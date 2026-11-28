using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Spatial;

namespace PlatformerSandbox.Gameplay.World;

public enum WorldSpatialKind { StaticCollider, Trigger, MovingPlatform, Player, DynamicBody }
public readonly record struct WorldSpatialObject(WorldSpatialKind Kind,int Id,Aabb Bounds);

/// <summary>Detached committed-state query view. Static and dynamic indexes have distinct owners;
/// no staged motor data, graphics or interpolated transforms enter authoritative gameplay queries.</summary>
public sealed class WorldSpatialSnapshot
{
    private readonly SpatialIndex<WorldSpatialObject> _static;
    private readonly SpatialIndex<WorldSpatialObject> _dynamic;
    public long Tick { get; }
    internal WorldSpatialSnapshot(long tick,SpatialIndex<WorldSpatialObject> statics,IEnumerable<WorldSpatialObject> dynamics)
    { Tick=tick; _static=statics; _dynamic=Build(dynamics,true); }
    internal static SpatialIndex<WorldSpatialObject> Build(IEnumerable<WorldSpatialObject> objects,bool dynamic)
    {
        var index=new SpatialIndex<WorldSpatialObject>(); int ordinal=0;
        foreach(var o in objects.OrderBy(o=>o.Kind).ThenBy(o=>o.Id))
            index.Add(new(ordinal++,SpatialBounds.From(o.Bounds),o,1UL<<(int)o.Kind,dynamic));
        return index;
    }
    public IReadOnlyList<WorldSpatialObject> Query(SpatialBounds bounds,ulong categories=ulong.MaxValue,SpatialQueryMode? mode=null)
    {
        var result=new List<WorldSpatialObject>(); var entries=new List<SpatialEntry<WorldSpatialObject>>(); var scratch=new HashSet<int>();
        var filter=new SpatialFilter(categories,true,true,null);
        foreach(var index in new[]{_static,_dynamic})
        { index.Query(bounds,entries,scratch,mode??SpatialQueryPolicy.Current,filter); result.AddRange(entries.Select(e=>e.Value)); }
        return result.AsReadOnly();
    }
    public IReadOnlyList<WorldSpatialObject> Radius(Vector2D center,double radius,ulong categories=ulong.MaxValue,SpatialQueryMode? mode=null)
    {
        if(!double.IsFinite(radius)||radius<0) throw new ArgumentOutOfRangeException(nameof(radius));
        return Query(new SpatialBounds(center.X,center.Y,center.X,center.Y).Expand(radius),categories,mode)
            .Where(o=>SpatialBounds.From(o.Bounds).DistanceTo(center)<=radius).ToArray();
    }
    public IReadOnlyList<WorldSpatialObject> Point(Vector2D point,ulong categories=ulong.MaxValue,SpatialQueryMode? mode=null)
        =>Query(new SpatialBounds(point.X,point.Y,point.X,point.Y),categories,mode);
    public IReadOnlyList<WorldSpatialObject> Swept(Aabb start,Vector2D displacement,ulong categories=ulong.MaxValue,SpatialQueryMode? mode=null)
        =>Query(SpatialBounds.Swept(start,displacement),categories,mode);
    public IReadOnlyList<WorldSpatialObject> RayCandidates(Vector2D start,Vector2D end,ulong categories=ulong.MaxValue,SpatialQueryMode? mode=null)
        =>Query(new SpatialBounds(Math.Min(start.X,end.X),Math.Min(start.Y,end.Y),Math.Max(start.X,end.X),Math.Max(start.Y,end.Y)),categories,mode);
    public WorldSpatialObject? Nearest(Vector2D center,double radius,ulong categories=ulong.MaxValue,SpatialQueryMode? mode=null)
    {
        WorldSpatialObject? best=null; double distance=double.PositiveInfinity;
        foreach(var item in Radius(center,radius,categories,mode))
        { double d=SpatialBounds.From(item.Bounds).DistanceTo(center); if(best is null||d<distance){best=item;distance=d;} }
        return best;
    }
}
