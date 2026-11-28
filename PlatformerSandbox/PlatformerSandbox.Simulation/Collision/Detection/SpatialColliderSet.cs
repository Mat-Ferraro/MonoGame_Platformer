using System.Collections;
using PlatformerSandbox.Simulation.Motion.Integration;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Spatial;

namespace PlatformerSandbox.Simulation.Collision.Detection;

/// <summary>Immutable, validated world-owned collider collection. The ordinary enumeration is unchanged;
/// only explicitly bounded sweep queries can use its spatial index. Ordinal IDs preserve authored order.</summary>
public sealed class SpatialColliderSet : IReadOnlyList<StaticAabbCollider>
{
    private readonly StaticAabbCollider[] _items;
    private readonly Lazy<SpatialIndex<StaticAabbCollider>> _index;
    public SpatialColliderSet(IReadOnlyList<StaticAabbCollider> source,double cellSize=4)
    {
        ArgumentNullException.ThrowIfNull(source);
        _items=source.ToArray(); var ids=new HashSet<int>();
        foreach(var c in _items) { ArgumentNullException.ThrowIfNull(c); if(!ids.Add(c.Id)) throw new ArgumentException("Collider IDs must be unique.",nameof(source)); }
        if(!double.IsFinite(cellSize)||cellSize<=0) throw new ArgumentOutOfRangeException(nameof(cellSize));
        _index=new(()=>{var result=new SpatialIndex<StaticAabbCollider>(cellSize); for(int i=0;i<_items.Length;i++) result.Add(new(i,SpatialBounds.From(_items[i].Bounds),_items[i])); return result;});
    }
    public int Count=>_items.Length;
    public StaticAabbCollider this[int i]=>_items[i];
    public IEnumerator<StaticAabbCollider> GetEnumerator()=>((IEnumerable<StaticAabbCollider>)_items).GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator()=>GetEnumerator();
    public static SpatialColliderSet Wrap(IReadOnlyList<StaticAabbCollider> items)=>items as SpatialColliderSet??new(items);
    public IReadOnlyList<StaticAabbCollider> Query(SpatialBounds bounds,SpatialQueryMode mode)
    {
        if(mode==SpatialQueryMode.Reference) return this;
        var output=new List<SpatialEntry<StaticAabbCollider>>();
        var stats=_index.Value.Query(bounds,output,new HashSet<int>(),mode);
        SpatialCollisionDiagnostics.Record(stats);
        return output.Select(e=>e.Value).ToArray();
    }
    // Uncertain numerical envelopes retain the original detector behavior. No exceptions from the actual solver are caught.
    internal static IReadOnlyList<StaticAabbCollider> Select(IReadOnlyList<StaticAabbCollider> source,Func<SpatialBounds> envelope,double tolerance=0)
    {
        if(source is not SpatialColliderSet set||SpatialQueryPolicy.Collision==SpatialQueryMode.Reference) return source;
        SpatialBounds bounds;
        try
        {
            bounds=envelope();
            double magnitude=Math.Max(1,Math.Max(Math.Max(Math.Abs(bounds.Left),Math.Abs(bounds.Right)),Math.Max(Math.Abs(bounds.Bottom),Math.Abs(bounds.Top))));
            bounds=bounds.Expand(Math.Max(tolerance,32*(Math.BitIncrement(magnitude)-magnitude)));
        }
        catch(Exception ex) when(ex is ArgumentException or OverflowException) { return source; }
        return set.Query(bounds,SpatialQueryPolicy.Collision);
    }
    internal static T? Verify<T>(IReadOnlyList<StaticAabbCollider> source,T? result,Func<T?> reference) where T:struct
    {
        if(source is SpatialColliderSet && SpatialQueryPolicy.Collision==SpatialQueryMode.Verify)
        {
            using var mode=SpatialQueryPolicy.Use(SpatialQueryMode.Reference);
            if(!EqualityComparer<T?>.Default.Equals(result,reference()))
                throw new InvalidOperationException("PO05 collision result differs from reference.");
        }
        return result;
    }
    internal static SpatialBounds Parabolic(Aabb start,Vector2D v,Vector2D a,double t)
    {
        var bounds=SpatialBounds.Swept(start,ConstantAcceleration2D.Evaluate(Vector2D.Zero,v,a,t).Position);
        foreach(double turn in new[]{a.X==0?double.NaN:-v.X/a.X,a.Y==0?double.NaN:-v.Y/a.Y})
            if(turn>0&&turn<t) bounds=bounds.Union(SpatialBounds.From(start.Translated(ConstantAcceleration2D.Evaluate(Vector2D.Zero,v,a,turn).Position)));
        return bounds;
    }
}
public static class SpatialCollisionDiagnostics
{
    private static long _queries,_examined,_matches;
    public static (long Queries,long Examined,long Matches) Snapshot => (Interlocked.Read(ref _queries),Interlocked.Read(ref _examined),Interlocked.Read(ref _matches));
    internal static void Record(SpatialQueryStats stats) { Interlocked.Increment(ref _queries); Interlocked.Add(ref _examined,stats.Examined); Interlocked.Add(ref _matches,stats.Matches); }
}
