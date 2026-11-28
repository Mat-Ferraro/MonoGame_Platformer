using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Foundation.Spatial;

/// <summary>Inclusive broadphase bounds, including zero-area point/ray queries.</summary>
public readonly record struct SpatialBounds
{
    public double Left { get; }
    public double Bottom { get; }
    public double Right { get; }
    public double Top { get; }
    public SpatialBounds(double left, double bottom, double right, double top)
    {
        if (!double.IsFinite(left) || !double.IsFinite(bottom) || !double.IsFinite(right) ||
            !double.IsFinite(top) || left > right || bottom > top)
            throw new ArgumentException("Invalid spatial bounds.");
        Left=left; Bottom=bottom; Right=right; Top=top;
    }
    public static SpatialBounds From(Aabb box) => box.IsValid
        ? new(box.Left,box.Bottom,box.Right,box.Top) : throw new ArgumentException("Invalid AABB.");
    public bool Intersects(SpatialBounds b) => Left<=b.Right && Right>=b.Left && Bottom<=b.Top && Top>=b.Bottom;
    public SpatialBounds Union(SpatialBounds b) => new(Math.Min(Left,b.Left),Math.Min(Bottom,b.Bottom),Math.Max(Right,b.Right),Math.Max(Top,b.Top));
    public SpatialBounds Expand(double amount)
    {
        if (!double.IsFinite(amount) || amount<0) throw new ArgumentOutOfRangeException(nameof(amount));
        return new(Left-amount,Bottom-amount,Right+amount,Top+amount);
    }
    public static SpatialBounds Swept(Aabb start, Vector2D displacement) => From(start).Union(From(start.Translated(displacement)));
    public double DistanceTo(Vector2D point)
    {
        double dx=Math.Max(0,Math.Max(Left-point.X,point.X-Right)), dy=Math.Max(0,Math.Max(Bottom-point.Y,point.Y-Top));
        double scale=Math.Max(dx,dy);
        return scale==0 || double.IsInfinity(scale) ? scale : scale*Math.Sqrt((dx/scale)*(dx/scale)+(dy/scale)*(dy/scale));
    }
}

public enum SpatialQueryMode { Reference, Indexed, Verify }
public readonly record struct SpatialEntry<T>(int Id, SpatialBounds Bounds, T Value, ulong Categories=ulong.MaxValue, bool Dynamic=false);
public readonly record struct SpatialFilter(ulong Categories=ulong.MaxValue, bool IncludeStatic=true, bool IncludeDynamic=true, int? ExcludeId=null)
{
    public static SpatialFilter All => new(ulong.MaxValue,true,true,null);
    public bool Matches<T>(SpatialEntry<T> entry) => (Categories & entry.Categories)!=0 && (entry.Dynamic?IncludeDynamic:IncludeStatic) && ExcludeId!=entry.Id;
}
public readonly record struct SpatialQueryStats(int Objects, int Cells, int Oversized, int Examined, int Matches, bool LinearFallback);

/// <summary>Single-owner mutable index. Results are sorted by stable ID. Scratch belongs to the caller;
/// no query mutates entries. Oversized objects and queries use conservative bounded linear handling.</summary>
public sealed class SpatialIndex<T>
{
    private readonly Dictionary<int,SpatialEntry<T>> _entries=new();
    private readonly Dictionary<(long X,long Y),HashSet<int>> _cells=new();
    private readonly HashSet<int> _oversized=new();
    private readonly double _size;
    private readonly int _limit;
    public SpatialIndex(double cellSize=4, int maxCellsPerOperation=4096)
    {
        if (!double.IsFinite(cellSize)||cellSize<=0) throw new ArgumentOutOfRangeException(nameof(cellSize));
        if (maxCellsPerOperation<1) throw new ArgumentOutOfRangeException(nameof(maxCellsPerOperation));
        _size=cellSize; _limit=maxCellsPerOperation;
    }
    public int Count=>_entries.Count;
    public int OccupiedCells=>_cells.Count;
    public int OversizedCount=>_oversized.Count;
    public double CellSize=>_size;
    private bool Range(SpatialBounds b,out long x0,out long y0,out long x1,out long y1)
    {
        double a=Math.Floor(b.Left/_size), c=Math.Floor(b.Right/_size), d=Math.Floor(b.Bottom/_size), e=Math.Floor(b.Top/_size);
        x0=y0=x1=y1=0;
        // Limit integer conversion to the range of exactly representable doubles, also avoiding loop overflow.
        const double max=4503599627370496d;
        if (a < -max || c > max || d < -max || e > max || !double.IsFinite(a+c+d+e)) return false;
        if ((c-a+1)*(e-d+1)>_limit) return false;
        x0=(long)a; x1=(long)c; y0=(long)d; y1=(long)e; return true;
    }
    private void Membership(SpatialEntry<T> entry,bool add)
    {
        if (!Range(entry.Bounds,out long x0,out long y0,out long x1,out long y1))
        { if(add) _oversized.Add(entry.Id); else _oversized.Remove(entry.Id); return; }
        for(long y=y0;y<=y1;y++) for(long x=x0;x<=x1;x++)
        {
            var key=(x,y);
            if(add) { if(!_cells.TryGetValue(key,out var ids)) _cells.Add(key,ids=new()); ids.Add(entry.Id); }
            else if(_cells.TryGetValue(key,out var ids)) { ids.Remove(entry.Id); if(ids.Count==0) _cells.Remove(key); }
        }
    }
    public void Add(SpatialEntry<T> entry)
    {
        if(!_entries.TryAdd(entry.Id,entry)) throw new ArgumentException("Duplicate spatial ID.",nameof(entry));
        Membership(entry,true);
    }
    public bool Remove(int id)
    { if(!_entries.Remove(id,out var old)) return false; Membership(old,false); return true; }
    public void Update(SpatialEntry<T> entry)
    {
        if(!_entries.TryGetValue(entry.Id,out var old)) throw new KeyNotFoundException("Unknown spatial ID.");
        if(old.Bounds!=entry.Bounds) { Membership(old,false); Membership(entry,true); }
        _entries[entry.Id]=entry;
    }
    public void Clear() { _entries.Clear(); _cells.Clear(); _oversized.Clear(); }
    public SpatialQueryStats Query(SpatialBounds bounds,List<SpatialEntry<T>> output,HashSet<int> scratch,
        SpatialQueryMode mode=SpatialQueryMode.Indexed, SpatialFilter? filter=null)
    {
        ArgumentNullException.ThrowIfNull(output); ArgumentNullException.ThrowIfNull(scratch);
        if(!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        output.Clear(); scratch.Clear(); var f=filter??SpatialFilter.All;
        bool bounded=Range(bounds,out long x0,out long y0,out long x1,out long y1);
        bool linear=mode==SpatialQueryMode.Reference||!bounded;
        void Consider(SpatialEntry<T> entry) { if(f.Matches(entry)&&bounds.Intersects(entry.Bounds)) output.Add(entry); }
        if(linear) { foreach(var entry in _entries.Values) Consider(entry); }
        else
        {
            for(long y=y0;y<=y1;y++) for(long x=x0;x<=x1;x++)
                if(_cells.TryGetValue((x,y),out var ids)) foreach(int id in ids) scratch.Add(id);
            foreach(int id in _oversized) scratch.Add(id);
            foreach(int id in scratch) Consider(_entries[id]);
        }
        output.Sort((a,b)=>a.Id.CompareTo(b.Id));
        if(mode==SpatialQueryMode.Verify&&!linear)
        {
            int expected=0;
            foreach(var entry in _entries.Values)
                if(f.Matches(entry)&&bounds.Intersects(entry.Bounds))
                { expected++; if(!scratch.Contains(entry.Id)) throw new InvalidOperationException($"Spatial index omitted ID {entry.Id}."); }
            if(expected!=output.Count) throw new InvalidOperationException("Spatial query mismatch.");
        }
        return new(Count,OccupiedCells,OversizedCount,linear?Count:scratch.Count,output.Count,!bounded);
    }
    public SpatialQueryStats Radius(Vector2D center,double radius,List<SpatialEntry<T>> output,HashSet<int> scratch,
        SpatialQueryMode mode=SpatialQueryMode.Indexed,SpatialFilter? filter=null)
    {
        if(!double.IsFinite(radius)||radius<0) throw new ArgumentOutOfRangeException(nameof(radius));
        var stats=Query(new SpatialBounds(center.X,center.Y,center.X,center.Y).Expand(radius),output,scratch,mode,filter);
        output.RemoveAll(x=>x.Bounds.DistanceTo(center)>radius);
        return stats with { Matches=output.Count };
    }
    public SpatialEntry<T>? Nearest(Vector2D center,double radius,List<SpatialEntry<T>> output,HashSet<int> scratch,SpatialQueryMode mode=SpatialQueryMode.Indexed,SpatialFilter? filter=null)
    {
        Radius(center,radius,output,scratch,mode,filter); SpatialEntry<T>? best=null; double distance=double.PositiveInfinity;
        foreach(var entry in output) { double d=entry.Bounds.DistanceTo(center); if(best is null||d<distance) {best=entry;distance=d;} }
        return best;
    }
    public SpatialQueryStats Point(Vector2D point,List<SpatialEntry<T>> output,HashSet<int> scratch,SpatialQueryMode mode=SpatialQueryMode.Indexed,SpatialFilter? filter=null) => Query(new(point.X,point.Y,point.X,point.Y),output,scratch,mode,filter);
    public SpatialQueryStats RayCandidates(Vector2D start,Vector2D end,List<SpatialEntry<T>> output,HashSet<int> scratch,SpatialQueryMode mode=SpatialQueryMode.Indexed,SpatialFilter? filter=null) => Query(new(Math.Min(start.X,end.X),Math.Min(start.Y,end.Y),Math.Max(start.X,end.X),Math.Max(start.Y,end.Y)),output,scratch,mode,filter);
}
