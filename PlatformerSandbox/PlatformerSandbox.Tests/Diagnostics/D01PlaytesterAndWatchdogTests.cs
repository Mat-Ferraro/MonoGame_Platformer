using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using PlatformerSandbox.Diagnostics;
using Xunit;

namespace PlatformerSandbox.Tests.Diagnostics;

public sealed class D01PlaytesterAndWatchdogTests
{
    private static readonly DiagnosticBuildInfo Build = new("TestGame", "1.0", "test-build",
        ".NET", "test-os", "x64");

    [Fact]
    public void CleanExitRemovesMarkerAndDoesNotReportPreviousFailure()
    {
        string folder = NewFolder();
        try
        {
            using (var tracker = new DiagnosticRunTracker(folder))
            {
                Assert.False(tracker.HadPreviousUncleanExit);
                Assert.Single(Directory.GetFiles(folder, "run_*.running"));
                tracker.MarkCleanExit();
            }
            Assert.Empty(Directory.GetFiles(folder, "run_*.running"));
            using var next = new DiagnosticRunTracker(folder);
            Assert.False(next.HadPreviousUncleanExit);
            next.MarkCleanExit();
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void DeadProcessMarkerIsReportedAndCleared()
    {
        string folder = NewFolder();
        try
        {
            File.WriteAllText(Path.Combine(folder, "run_dead.running"),
                JsonSerializer.Serialize(new { ProcessId = int.MaxValue, ProcessStartUtcTicks = 0L }));
            using var tracker = new DiagnosticRunTracker(folder);
            Assert.True(tracker.HadPreviousUncleanExit);
            Assert.Equal(1, tracker.PreviousUncleanExitCount);
            Assert.False(Directory.GetFiles(folder, "run_*.running")
                .Any(path => Path.GetFileName(path) == "run_dead.running"));
            tracker.MarkCleanExit();
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ConcurrentLiveSessionsAreNotMistakenForCrashes()
    {
        string folder = NewFolder();
        try
        {
            using var first = new DiagnosticRunTracker(folder);
            using var second = new DiagnosticRunTracker(folder);
            Assert.False(second.HadPreviousUncleanExit);
            Assert.Equal(2, Directory.GetFiles(folder, "run_*.running").Length);
            first.MarkCleanExit();
            second.MarkCleanExit();
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ExportCreatesReviewableZipWithReportAndActiveSession()
    {
        string folder = NewFolder();
        try
        {
            using var log = NewSession(folder);
            string privatePath = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile), "private-source.cs");
            log.Log(DiagnosticSeverity.Warning, "test.event", "Event in " + privatePath);
            string crash = Assert.IsType<string>(log.WriteCrashReport(
                new InvalidOperationException("Issue in " + privatePath), "test", Build));
            var recorder = new DiagnosticContextRecorder();
            recorder.UpdateCommittedState(new Dictionary<string, string> { ["world.committedTick"] = "450" });
            var exporter = new DiagnosticReportExporter(log, Build, recorder.Snapshot);
            DiagnosticExportResult result = exporter.Export();
            Assert.True(result.Success, result.Error);
            Assert.True(File.Exists(result.FilePath));
            using var archive = ZipFile.OpenRead(result.FilePath!);
            Assert.Contains(archive.Entries, entry => entry.Name == Path.GetFileName(crash));
            Assert.Contains(archive.Entries, entry => entry.Name.StartsWith("session_", StringComparison.Ordinal));
            Assert.Contains(archive.Entries, entry => entry.Name == "gameplay-context.txt");
            Assert.Contains(archive.Entries, entry => entry.Name == "README-REVIEW-BEFORE-SHARING.txt");
            Assert.Contains(archive.Entries, entry => entry.Name == "manifest.json");
            var crashEntry = archive.Entries.Single(x => x.Name == Path.GetFileName(crash));
            using var reader = new StreamReader(crashEntry.Open());
            string text = reader.ReadToEnd();
            Assert.Contains("InvalidOperationException", text);
            Assert.False(text.Contains(privatePath, StringComparison.OrdinalIgnoreCase));
            Assert.Contains("<USER_HOME>", text);
            Assert.Empty(Directory.GetFiles(Path.Combine(folder, "Reports"), "*.tmp"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ExportWorksWithoutPriorCrashAndDoesNotUpload()
    {
        string folder = NewFolder();
        try
        {
            using var log = NewSession(folder);
            var exporter = new DiagnosticReportExporter(log, Build);
            var result = exporter.Export();
            Assert.True(result.Success, result.Error);
            Assert.StartsWith(Path.Combine(folder, "Reports"), result.FilePath!);
            using var archive = ZipFile.OpenRead(result.FilePath!);
            Assert.Contains(archive.Entries, x => x.Name == "manifest.json");
            Assert.Contains(archive.Entries, x => x.Name.StartsWith("session_", StringComparison.Ordinal));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void WatchdogOnlyEmitsOncePerStalledActiveOperation()
    {
        string folder = NewFolder();
        try
        {
            using var log = NewSession(folder);
            var clock = new ManualClock();
            var recorder = new DiagnosticContextRecorder();
            recorder.UpdateCommittedState(new Dictionary<string, string> { ["world.committedTick"] = "450" });
            recorder.BeginOperation("world.step", "P1 right + wind", 451);
            using var watchdog = new DiagnosticHangWatchdog(log, Build, recorder.Snapshot,
                stallThreshold: TimeSpan.FromSeconds(2), slowThreshold: TimeSpan.FromSeconds(1),
                clock: clock, startTimer: false);
            watchdog.CheckOnce(); // idle must not be a hang
            Assert.Empty(Directory.GetFiles(folder, "stall_*.txt"));
            using (watchdog.BeginOperation("Game1.Update"))
            {
                clock.Advance(TimeSpan.FromSeconds(1));
                watchdog.CheckOnce();
                Assert.Empty(Directory.GetFiles(folder, "stall_*.txt"));
                clock.Advance(TimeSpan.FromSeconds(2));
                watchdog.CheckOnce();
                watchdog.CheckOnce();
                Assert.Single(Directory.GetFiles(folder, "stall_*.txt"));
                string report = File.ReadAllText(Directory.GetFiles(folder, "stall_*.txt")[0]);
                Assert.Contains("suspected stall", report);
                Assert.Contains("Game1.Update", report);
                Assert.Contains("world.committedTick = 450", report);
                Assert.Contains("world.step tick=451", report);
                Assert.Contains("cannot capture the blocked game thread", report);
            }
            clock.Advance(TimeSpan.FromSeconds(15));
            watchdog.CheckOnce(); // no active operation
            Assert.Single(Directory.GetFiles(folder, "stall_*.txt"));
            using (watchdog.BeginOperation("Game1.Draw"))
            {
                clock.Advance(TimeSpan.FromSeconds(3));
                watchdog.CheckOnce();
            }
            Assert.Equal(2, Directory.GetFiles(folder, "stall_*.txt").Length);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ShortOperationDoesNotGenerateSlowOrStallEvents()
    {
        string folder = NewFolder();
        try
        {
            using var log = NewSession(folder);
            var clock = new ManualClock();
            using var watchdog = new DiagnosticHangWatchdog(log, Build,
                stallThreshold: TimeSpan.FromSeconds(2), slowThreshold: TimeSpan.FromMilliseconds(250),
                clock: clock, startTimer: false);
            using (watchdog.BeginOperation("Game1.Update"))
                clock.Advance(TimeSpan.FromMilliseconds(16));
            watchdog.CheckOnce();
            Assert.Empty(Directory.GetFiles(folder, "stall_*.txt"));
            Assert.DoesNotContain(log.RecentSnapshot(), x => x.EventId == "performance.slow-operation");
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void LongCompletedOperationRecordsSlowWarningWithoutCallingItAFatalCrash()
    {
        string folder = NewFolder();
        try
        {
            using var log = NewSession(folder);
            var clock = new ManualClock();
            using var watchdog = new DiagnosticHangWatchdog(log, Build,
                stallThreshold: TimeSpan.FromSeconds(8), slowThreshold: TimeSpan.FromMilliseconds(250),
                clock: clock, startTimer: false);
            using (watchdog.BeginOperation("Game1.Update"))
                clock.Advance(TimeSpan.FromMilliseconds(350));
            Assert.Contains(log.RecentSnapshot(), x => x.EventId == "performance.slow-operation");
            Assert.Empty(Directory.GetFiles(folder, "crash_*.txt"));
            Assert.Empty(Directory.GetFiles(folder, "stall_*.txt"));
        }
        finally { Directory.Delete(folder, true); }
    }

    private static FileDiagnosticSession NewSession(string folder) => new(new DiagnosticSettings
    {
        LogDirectory = folder
    });
    private static string NewFolder()
    {
        string path = Path.Combine(Path.GetTempPath(), "D01_phase34_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan elapsed) => _ticks += elapsed.Ticks;
    }
}
