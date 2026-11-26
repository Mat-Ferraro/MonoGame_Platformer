using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Optional presentation quality policy, using projected emitter diameter.
/// Hysteresis is per logical emitter, bounded by the visible-light budget.</summary>
public sealed class AdaptiveAreaSamplePolicy
{
    private static readonly double[] Thresholds = { 12, 32, 64, 128 };
    private readonly Dictionary<string,int> _previous = new(StringComparer.Ordinal);
    public void Clear() => _previous.Clear();
    public static int Select(double pixels, int previous, int maximum)
    {
        if (!double.IsFinite(pixels) || pixels < 0 || maximum is not (1 or 3 or 5 or 7 or 9))
            throw new ArgumentOutOfRangeException();
        int tier = previous is 1 or 3 or 5 or 7 or 9 ? (previous-1)/2 : -1;
        if (tier < 0) { tier=0; while(tier<4 && pixels>=Thresholds[tier]) tier++; }
        else {
            while(tier<4 && pixels>=Thresholds[tier]*1.15) tier++;
            while(tier>0 && pixels<Thresholds[tier-1]*.85) tier--;
        }
        return Math.Min(maximum,1+2*tier);
    }
    public IReadOnlyList<RadialLight> Apply(IReadOnlyList<RadialLight> lights, double pixelsPerUnit, int maximum)
    {
        if (_previous.Count>512) _previous.Clear();
        RadialLight[]? changed=null;
        for(int i=0;i<lights.Count;i++) {
            var light=lights[i];
            if(light.Shape!=LightShape.Tube || light.AreaSampleCount!=0) continue;
            _previous.TryGetValue(light.Id,out int old);
            int count=Select(2*AreaEmitterSampling.Extent(light)*pixelsPerUnit,old,maximum);
            _previous[light.Id]=count;
            if(changed is null) { changed=new RadialLight[lights.Count]; for(int j=0;j<lights.Count;j++) changed[j]=lights[j]; }
            changed[i]=light with { AreaSampleCount=count };
        }
        return changed is null ? lights : changed;
    }
}
