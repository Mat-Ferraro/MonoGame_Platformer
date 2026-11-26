using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Renderer/session-owned bounded cache of exact weighted light records.
/// A changed source record or count gets a new entry. No GPU resources are held.</summary>
public sealed class PreparedAreaLightCache
{
    private const int Capacity = 256;
    private readonly Dictionary<(RadialLight Light, int Count), IReadOnlyList<RadialLight>> _samples = new();
    public int Count => _samples.Count;
    public void Clear() => _samples.Clear();
    public IReadOnlyList<RadialLight> Get(RadialLight light, int count)
    {
        light.Validate();
        if (count is not (1 or 3 or 5 or 7 or 9)) throw new ArgumentOutOfRangeException(nameof(count));
        var key = (light, count);
        if (_samples.TryGetValue(key, out var result)) return result;
        if (_samples.Count >= Capacity) _samples.Clear();
        var built = AreaEmitterSampling.Samples(light, count);
        result = Array.AsReadOnly(built as RadialLight[] ?? System.Linq.Enumerable.ToArray(built));
        _samples.Add(key, result);
        return result;
    }
    public IEnumerable<RadialLight> Expand(IEnumerable<RadialLight> lights, int count)
    {
        foreach (var light in lights)
        {
            if (light.Shape != LightShape.Tube) { yield return light; continue; }
            foreach (var sample in Get(light, AreaEmitterSampling.ResolveCount(light,count))) yield return sample;
        }
    }
}
