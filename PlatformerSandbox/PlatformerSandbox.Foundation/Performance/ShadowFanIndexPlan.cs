using System;
using System.Runtime.InteropServices;

namespace PlatformerSandbox.Foundation.Performance;

/// <summary>Headless, lossless triangle-list to indexed-fan conversion. No GPU dependency.</summary>
public static class ShadowFanIndexPlan
{
    public const int MaxTriangles = 16384;
    public static ushort[] BuildIndices(int triangles)
    {
        if (triangles < 1 || triangles > MaxTriangles) throw new ArgumentOutOfRangeException(nameof(triangles));
        var indices = new ushort[triangles*3];
        for (int i=0;i<triangles;i++)
        { indices[3*i]=0; indices[3*i+1]=(ushort)(i+1); indices[3*i+2]=(ushort)(i+2); }
        return indices;
    }
    public static bool TryCompact<T>(ReadOnlySpan<T> source, Span<T> destination, int triangles) where T: unmanaged
    {
        if (triangles < 1 || triangles > MaxTriangles || source.Length < triangles*3 || destination.Length < triangles+2) return false;
        destination[0]=source[0];
        for(int i=0;i<triangles;i++) destination[i+1]=source[3*i+1];
        destination[triangles+1]=source[triangles*3-1];
        // Full bit equality: preserves winding, color, signed zero and closing vertex.
        var a=MemoryMarshal.AsBytes(source[..(triangles*3)]);
        var b=MemoryMarshal.AsBytes(destination[..(triangles+2)]);
        int stride=a.Length/(triangles*3);
        for(int i=0;i<triangles;i++)
            if(!a.Slice(3*i*stride,stride).SequenceEqual(b.Slice(0,stride)) ||
               !a.Slice((3*i+1)*stride,2*stride).SequenceEqual(b.Slice((i+1)*stride,2*stride))) return false;
        return true;
    }
}
