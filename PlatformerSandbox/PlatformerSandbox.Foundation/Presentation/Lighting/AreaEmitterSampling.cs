using System;
using System.Collections.Generic;
using System.Linq;
using PlatformerSandbox.Foundation.Performance;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

public enum AreaEmitterFootprint { Line, Disc, Rectangle }

/// <summary>Deterministic quadrature of a finite 2D emitter. Each sample owns
/// a fraction of the source energy, not another independent authored light.
/// Samples deliberately keep the logical ID for the shadow quota.</summary>
public static class AreaEmitterSampling
{
    public const int MaximumSamples = 9;
    public static int ResolveCount(RadialLight light, int count) => count == 1 || EnginePerformanceOptions.AreaSamples != 0
        ? count : light.AreaSampleCount == 0 ? count : light.AreaSampleCount;
    public static int CountForRadialBudget(int radialSamples) => radialSamples >= 192 ? 9 : radialSamples >= 128 ? 5 : 3;
    public static double Extent(RadialLight light) => light.Shape != LightShape.Tube ? 0 :
        light.EmitterFootprint == AreaEmitterFootprint.Rectangle
            ? Math.Sqrt(light.TubeLength*light.TubeLength + light.EmitterWidth*light.EmitterWidth)*.5
            : light.TubeLength*.5;

    public static IReadOnlyList<RadialLight> Samples(RadialLight light, int count = MaximumSamples)
    {
        light.Validate();
        if (count is not (1 or 3 or 5 or 7 or 9)) throw new ArgumentOutOfRangeException(nameof(count));
        if (light.Shape != LightShape.Tube) return new[] { light };
        double d = Math.Sqrt(light.Direction.X*light.Direction.X + light.Direction.Y*light.Direction.Y);
        LightPoint axis = light.Direction*(1/d), side = new(-axis.Y, axis.X);
        var offsets = new List<LightPoint>(count);
        if (count == 1) offsets.Add(new(0,0));
        else if (light.EmitterFootprint == AreaEmitterFootprint.Line)
            for (int i=0;i<count;i++) offsets.Add(axis*((i+.5)/count-.5)*light.TubeLength);
        else if (light.EmitterFootprint == AreaEmitterFootprint.Disc)
        {
            offsets.Add(new(0,0));
            // Equal-weight center plus an evenly spaced outer ring.
            double r = light.TubeLength*.5*Math.Sqrt(count/(2.0*(count-1)));
            for (int i=0;i<count-1;i++)
            {
                double a=2*Math.PI*i/(count-1);
                offsets.Add(axis*(Math.Cos(a)*r)+side*(Math.Sin(a)*r));
            }
        }
        else if (count == 7)
        {
            // Center and six symmetric rectangular witnesses.
            offsets.Add(new(0,0));
            for (int y=-1;y<=1;y++) for (int x=-1;x<=1;x+=2)
                offsets.Add(axis*(x*light.TubeLength/3)+side*(y*light.EmitterWidth/3));
        }
        else if (count == 9)
            for (int y=-1;y<=1;y++) for(int x=-1;x<=1;x++)
                offsets.Add(axis*(x*light.TubeLength/3)+side*(y*light.EmitterWidth/3));
        else
        {
            // Center plus symmetric rectangular witnesses, bounded for lower quality.
            offsets.Add(new(0,0));
            if (count == 3) { offsets.Add(axis*(-light.TubeLength/3)); offsets.Add(axis*(light.TubeLength/3)); }
            else for(int y=-1;y<=1;y+=2) for(int x=-1;x<=1;x+=2)
                offsets.Add(axis*(x*light.TubeLength*.3)+side*(y*light.EmitterWidth*.3));
        }
        return offsets.Select(offset => light with { Shape = LightShape.Point,
            Position = light.Position + offset, Intensity = light.Intensity / offsets.Count }).ToArray();
    }

    public static IEnumerable<RadialLight> Expand(IEnumerable<RadialLight> lights, int count)
    {
        foreach (RadialLight light in lights)
        {
            if (light.Shape != LightShape.Tube) { yield return light; continue; }
            foreach (RadialLight sample in Samples(light,ResolveCount(light,count))) yield return sample;
        }
    }
}
