using System;
using System.Collections.Generic;
using System.IO;
using PlatformerSandbox.Diagnostics;
using Xunit;

namespace PlatformerSandbox.Tests.Diagnostics;

public sealed class D01GameplayContextTests
{
    [Fact]
    public void LastCommittedStateIsDefensivelyCopiedAndReplaced()
    {
        var recorder = new DiagnosticContextRecorder();
        var original = new Dictionary<string, string>
        {
            ["scene.levelId"] = "CAVERN_A",
            ["world.committedTick"] = "450"
        };
        recorder.UpdateCommittedState(original);
        DiagnosticContextSnapshot earlier = recorder.Snapshot();
        original["scene.levelId"] = "MUTATED";
        recorder.UpdateCommittedState(new Dictionary<string, string>
        {
            ["scene.levelId"] = "CAVERN_B"
        });
        Assert.Equal("CAVERN_A", earlier.LastCommittedState["scene.levelId"]);
        Assert.Equal("CAVERN_B", recorder.Snapshot().LastCommittedState["scene.levelId"]);
        Assert.False(recorder.Snapshot().LastCommittedState.ContainsKey("world.committedTick"));
    }

    [Fact]
    public void FailedTickIsClearlyUncommittedAndPreviousTickIsPreserved()
    {
        var recorder = new DiagnosticContextRecorder();
        recorder.UpdateCommittedState(new Dictionary<string, string>
        {
            ["world.committedTick"] = "450",
            ["player.1.movingSupportId"] = "8002"
        });
        recorder.BeginOperation("world.step", "P1 right; wind=+6.75", 451);
        DiagnosticContextSnapshot crash = recorder.Snapshot();
        Assert.Equal("450", crash.LastCommittedState["world.committedTick"]);
        Assert.Equal(451L, crash.PendingOperation?.WorldTick);
        Assert.Contains("wind=+6.75", crash.PendingOperation!.Detail);
        Assert.Empty(crash.RecentActivity);
        recorder.CommitOperation("worldId=2 committedTick=451");
        Assert.Null(recorder.Snapshot().PendingOperation);
        Assert.Single(recorder.Snapshot().RecentActivity);
    }

    [Fact]
    public void RecentHistoryIsBoundedAndOrdered()
    {
        var recorder = new DiagnosticContextRecorder(3);
        for (int i = 0; i < 8; i++)
            recorder.RecordActivity("world.event", "event-" + i, i);
        var recent = recorder.Snapshot().RecentActivity;
        Assert.Equal(3, recent.Count);
        Assert.Equal("event-5", recent[0].Detail);
        Assert.Equal("event-7", recent[2].Detail);
    }

    [Fact]
    public void CrashReportContainsCommittedStatePendingAttemptAndHistory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "D01ctx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var recorder = new DiagnosticContextRecorder();
            recorder.UpdateCommittedState(new Dictionary<string, string>
            {
                ["scene.levelId"] = "CAVERN_A",
                ["world.committedTick"] = "450",
                ["player.1.windX"] = "6.75"
            });
            recorder.RecordActivity("world.event", "Landed on collider 8002", 450);
            recorder.BeginOperation("world.step", "P1 right + jump", 451);
            using var logger = new FileDiagnosticSession(new DiagnosticSettings
            {
                LogDirectory = directory
            });
            var build = new DiagnosticBuildInfo("Game", "1", "build", ".NET", "test", "x64");
            using var hooks = new CrashExceptionHooks(logger, build, recorder.Snapshot);
            string file = Assert.IsType<string>(hooks.CaptureFatal(
                new InvalidOperationException("boom"), "Test.GameLoop"));
            string report = File.ReadAllText(file);
            Assert.Contains("scene.levelId = CAVERN_A", report);
            Assert.Contains("world.committedTick = 450", report);
            Assert.Contains("In-flight attempt (NOT committed", report);
            Assert.Contains("world.step tick=451", report);
            Assert.Contains("Landed on collider 8002", report);
            Assert.Contains("boom", report);
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void FaultyContextProviderDoesNotPreventCrashReport()
    {
        string directory = Path.Combine(Path.GetTempPath(), "D01ctx_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var logger = new FileDiagnosticSession(new DiagnosticSettings
            {
                LogDirectory = directory
            });
            var build = new DiagnosticBuildInfo("Game", "1", "build", ".NET", "test", "x64");
            using var hooks = new CrashExceptionHooks(logger, build,
                () => throw new InvalidOperationException("context reader failed"));
            string file = Assert.IsType<string>(hooks.CaptureFatal(new Exception("primary"), "Main"));
            Assert.Contains("primary", File.ReadAllText(file));
            Assert.Contains("context.capture-failed", File.ReadAllText(file));
        }
        finally { Directory.Delete(directory, true); }
    }
}
