using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
using Xunit;

namespace PlatformerSandbox.Tests.Gameplay.World;

public sealed class PO01HeadlessBenchmarkTests
{
    // Opt-in: normal test runs remain functional tests, not timing gates.
    [Fact]
    public void RecordTwoPlayerHeadlessBaselineWhenRequested()
    {
        string? path = Environment.GetEnvironmentVariable("PS_PO_HEADLESS_CSV");
        if (string.IsNullOrWhiteSpace(path)) return;
        var world = SharedWorldClockGym.Create();
        var commands = Array.Empty<WorldPlayerCommand>();
        for (int i = 0; i < 600; i++) world.Step(commands);
        const int count = 3600;
        var rows = new (double Ms, long Bytes)[count];
        long initialTick = world.WorldTick;
        for (int i = 0; i < count; i++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            world.Step(commands);
            _ = world.Snapshot();
            _ = world.MovingPlatforms;
            rows[i] = (Stopwatch.GetElapsedTime(start).TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - before);
        }
        Assert.Equal(initialTick + count, world.WorldTick);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var writer = new StreamWriter(path);
        writer.WriteLine("tick,step_ms,thread_alloc_bytes");
        for (int i = 0; i < count; i++)
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:F6},{2}", initialTick + i + 1, rows[i].Ms, rows[i].Bytes));
    }
}
