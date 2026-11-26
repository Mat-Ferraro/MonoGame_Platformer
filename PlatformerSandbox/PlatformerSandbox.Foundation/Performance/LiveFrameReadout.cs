using System;

namespace PlatformerSandbox.Foundation.Performance;

/// <summary>Wall-clock throughput over consecutive completed draw intervals.
/// Presentation diagnostics only; does not drive the simulation or benchmark.</summary>
public sealed class LiveFrameReadout
{
    private double _elapsed, _update, _draw, _present;
    private int _frames, _ticks;
    public bool Ready { get; private set; }
    public double FramesPerSecond { get; private set; }
    public double TicksPerSecond { get; private set; }
    public double MeanMilliseconds { get; private set; }
    public double UpdateMilliseconds { get; private set; }
    public double DrawMilliseconds { get; private set; }
    public double PresentMilliseconds { get; private set; }
    public double OtherMilliseconds { get; private set; }
    public double WindowMilliseconds { get; private set; }
    public void Add(double interval, int ticks, double update, double draw, double present)
    {
        if (!double.IsFinite(interval) || interval <= 0) return;
        _elapsed += interval;
        _frames++;
        _ticks += Math.Max(0,ticks);
        _update += Math.Max(0,update);
        _draw += Math.Max(0,draw);
        _present += Math.Max(0,present);
        if (_elapsed < 1000) return;
        WindowMilliseconds = _elapsed;
        FramesPerSecond = 1000.0 * _frames / _elapsed;
        TicksPerSecond = 1000.0 * _ticks / _elapsed;
        MeanMilliseconds = _elapsed / _frames;
        UpdateMilliseconds = _update / _frames;
        DrawMilliseconds = _draw / _frames;
        PresentMilliseconds = _present / _frames;
        OtherMilliseconds = Math.Max(0, MeanMilliseconds - UpdateMilliseconds - DrawMilliseconds - PresentMilliseconds);
        Ready = true;
        ClearPending();
    }
    private void ClearPending() { _elapsed = _update = _draw = _present = 0; _frames = _ticks = 0; }
    public void Reset()
    {
        ClearPending(); Ready = false;
        FramesPerSecond = TicksPerSecond = MeanMilliseconds = UpdateMilliseconds = DrawMilliseconds = PresentMilliseconds = OtherMilliseconds = WindowMilliseconds = 0;
    }
}
