using System;

namespace PlatformerSandbox.Foundation.Performance;

/// <summary>Bounded readiness for frozen benchmark fixtures. No FPS-based selection.</summary>
public sealed class BenchmarkWarmupPolicy
{
    public int Minimum { get; }
    public int Maximum { get; }
    public int StableFrames { get; }
    public int Observed { get; private set; }
    public int Streak { get; private set; }
    public string Status { get; private set; } = "warming";
    public BenchmarkWarmupPolicy(int minimum, int maximum, int stableFrames = 12)
    {
        if (minimum < 1 || maximum < minimum || stableFrames < 1) throw new ArgumentOutOfRangeException();
        Minimum = minimum; Maximum = maximum; StableFrames = stableFrames;
    }
    public bool Observe(bool ready)
    {
        if (Status != "warming") return true;
        Observed++;
        Streak = ready ? Streak + 1 : 0;
        if (Observed >= Minimum && Streak >= StableFrames) Status = "stable";
        else if (Observed >= Maximum) Status = "max_reached";
        return Status != "warming";
    }
}
