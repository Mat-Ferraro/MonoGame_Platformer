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
    private bool _percentilesDirty = true;
    private double _p50, _p95, _p99;

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
            RefreshPercentiles();
            return _p95;
        }
    }

    public double P50 { get { RefreshPercentiles(); return _p50; } }
    public double P99 { get { RefreshPercentiles(); return _p99; } }
    private void RefreshPercentiles()
    {
        if (!_percentilesDirty) return;
        _percentilesDirty = false;
        if (Count == 0) { _p50 = _p95 = _p99 = 0; return; }
        Array.Copy(_samples, _scratch, Count);
        Array.Sort(_scratch, 0, Count);
        _p50 = _scratch[(int)Math.Ceiling(Count * .50) - 1];
        _p95 = _scratch[(int)Math.Ceiling(Count * .95) - 1];
        _p99 = _scratch[(int)Math.Ceiling(Count * .99) - 1];
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
        _percentilesDirty = true;
        _next = (_next + 1) % Capacity;
    }

    public void Clear()
    {
        _percentilesDirty = true;
        Count = 0;
        Latest = 0;
        _sum = 0;
        _next = 0;
    }
}
