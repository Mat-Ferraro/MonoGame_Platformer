using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

internal static class VisibilityGeometryFingerprint
{
    private sealed class Fingerprint { public int Value; public double MinX, MinY, MaxX, MaxY; }
    private static readonly ConditionalWeakTable<LightPoint[], Fingerprint> Hashes = new();
    private static Fingerprint For(LightPoint[] immutable) => Hashes.GetValue(immutable, static points =>
    {
        var h = new HashCode();
        var result = new Fingerprint { MinX=double.PositiveInfinity, MinY=double.PositiveInfinity,
            MaxX=double.NegativeInfinity, MaxY=double.NegativeInfinity };
        foreach (var point in points)
        {
            h.Add(point);
            result.MinX=Math.Min(result.MinX,point.X); result.MaxX=Math.Max(result.MaxX,point.X);
            result.MinY=Math.Min(result.MinY,point.Y); result.MaxY=Math.Max(result.MaxY,point.Y);
        }
        result.Value=h.ToHashCode(); return result;
    });
    internal static int HullHash(LightPoint[] immutable) => For(immutable).Value;
    internal static bool SegmentMayTouch(LightPoint a, LightPoint b, LightingOccluder blocker)
    {
        var bounds=For(blocker.CaptureValidatedVertices());
        const double pad=1e-6;
        return Math.Max(a.X,b.X)+pad>=bounds.MinX && Math.Min(a.X,b.X)-pad<=bounds.MaxX &&
            Math.Max(a.Y,b.Y)+pad>=bounds.MinY && Math.Min(a.Y,b.Y)-pad<=bounds.MaxY;
    }
    internal static int BlockersHash(RadialLight light, IReadOnlyList<LightingOccluder> blockers, bool litFaces)
    {
        var h = new HashCode();
        h.Add(blockers.Count);
        foreach (var blocker in blockers)
        {
            h.Add(HullHash(blocker.CaptureValidatedVertices()));
            h.Add(litFaces && ReceiverWallShadowPolicy.StartsAtExit(light, blocker));
        }
        return h.ToHashCode();
    }
    internal static bool Same(LightPoint[] saved, LightingOccluder current) =>
        ReferenceEquals(saved, current.CaptureValidatedVertices()) || saved.AsSpan().SequenceEqual(current.Vertices);
}
