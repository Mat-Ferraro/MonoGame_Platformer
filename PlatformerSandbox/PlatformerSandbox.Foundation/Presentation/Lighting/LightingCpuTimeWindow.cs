using System;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Fixed-size sample history for CPU-side lighting command submission.
/// No per-frame history allocation or unbounded all-time peak retention.
/// P95 uses the nearest-rank definition; not a GPU elapsed-time query.
/// Presentation diagnostics only: never feeds authoritative simulation.
/// </summary>
public sealed class LightingCpuTimeWindow
{
    private readonly double[] _samples;
    private readonly double[] _scratch;
    private int _next;
    private double _sum;

    public int Capacity => _samples.Length;
    public int Count { get; private set; }
    public double Latest { get; private set; }
    public double Mean => Count == 0 ? 0 : _sum / Count;
    public double Peak
    {
        get
        {
            double peak = 0;
            for (int i = 0; i < Count; i++) peak = Math.Max(peak, _samples[i]);
            return peak;
        }
    }
    public double P95
    {
        get
        {
            if (Count == 0) return 0;
            Array.Copy(_samples, _scratch, Count);
            Array.Sort(_scratch, 0, Count);
            return _scratch[(int)Math.Ceiling(Count * .95) - 1];
        }
    }

    public LightingCpuTimeWindow(int capacity = 120)
    {
        if (capacity is < 1 or > 3600)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _samples = new double[capacity];
        _scratch = new double[capacity];
    }

    public void Add(double milliseconds)
    {
        if (!double.IsFinite(milliseconds) || milliseconds < 0)
            throw new ArgumentOutOfRangeException(nameof(milliseconds));
        if (Count == Capacity)
            _sum -= _samples[_next];
        else
            Count++;
        _samples[_next] = milliseconds;
        _sum += milliseconds;
        Latest = milliseconds;
        _next = (_next + 1) % Capacity;
    }

    public void Clear()
    {
        Count = 0;
        Latest = 0;
        _sum = 0;
        _next = 0;
    }
}
