using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Bounded exact caster snapshot for eligible hard point-light fields.
/// Never compare only identities: hull arrays may be edited in place.</summary>
public static class StaticShadowFieldSnapshot
{
    public const int MaxCasters = 128;
    public static LightingOccluder[] Capture(IReadOnlyList<LightingOccluder> casters)
    {
        ArgumentNullException.ThrowIfNull(casters);
        if(casters.Count>MaxCasters) throw new ArgumentOutOfRangeException(nameof(casters));
        var copy=new LightingOccluder[casters.Count];
        for(int i=0;i<copy.Length;i++)
        {
            var c=casters[i]; c.Validate();
            copy[i]=c with { Vertices=(LightPoint[])c.CaptureValidatedVertices().Clone() };
        }
        return copy;
    }
    public static bool Matches(IReadOnlyList<LightingOccluder> a,IReadOnlyList<LightingOccluder> b)
    {
        if(a.Count!=b.Count || a.Count>MaxCasters) return false;
        for(int i=0;i<a.Count;i++)
        {
            var x=a[i]; var y=b[i];
            if(x.Id!=y.Id || x.OwnerId!=y.OwnerId || x.LayerId!=y.LayerId ||
               x.LitFaceLayer!=y.LitFaceLayer || x.LitFaceDepthZ!=y.LitFaceDepthZ ||
               !x.Vertices.AsSpan().SequenceEqual(y.Vertices)) return false;
        }
        return true;
    }
}
