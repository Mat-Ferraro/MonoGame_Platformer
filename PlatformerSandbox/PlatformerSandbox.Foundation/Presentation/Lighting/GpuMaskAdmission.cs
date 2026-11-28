using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Bounded consecutive-frame observation. A fingerprint can admit work,
/// never validate a cached mask. Multiple uses within one frame count only once.</summary>
public sealed class GpuMaskAdmission
{
    public const int DefaultStableFrames = 12;
    private readonly int _capacity, _stableFrames;
    private Dictionary<int,int> _previous = new(), _current = new();
    public GpuMaskAdmission(int capacity = 96, int stableFrames = DefaultStableFrames)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (stableFrames <= 0) throw new ArgumentOutOfRangeException(nameof(stableFrames));
        _capacity=capacity; _stableFrames=stableFrames;
    }
    public void BeginFrame() { (_previous,_current)=(_current,_previous); _current.Clear(); }
    public bool Observe(int fingerprint)
    {
        if (_current.TryGetValue(fingerprint,out int observed)) return observed>=_stableFrames;
        if (_current.Count>=_capacity) return false;
        int count=_previous.TryGetValue(fingerprint,out int previous)?Math.Min(_stableFrames,previous+1):1;
        _current.Add(fingerprint,count);
        return count>=_stableFrames;
    }
    public void Clear() { _previous.Clear(); _current.Clear(); }
}
