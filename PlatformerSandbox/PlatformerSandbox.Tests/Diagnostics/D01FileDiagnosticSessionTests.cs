using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PlatformerSandbox.Diagnostics;
using Xunit;

namespace PlatformerSandbox.Tests.Diagnostics;

public sealed class D01FileDiagnosticSessionTests
{
    [Fact]
    public void LogsStructuredEventAndKeepsBoundedRecentHistory()
    {
        string directory = NewDirectory();
        try
        {
            using var logger = new FileDiagnosticSession(Options(directory, recent: 2));
            logger.Log(DiagnosticSeverity.Information, "startup", "Game started");
            logger.Log(DiagnosticSeverity.Warning, "level.load", "Bad asset",
                new Dictionary<string, string> { ["level"] = "CAVERN_A" });
            logger.Log(DiagnosticSeverity.Error, "test", "Third event");
            var recent = logger.RecentSnapshot();
            Assert.Equal(2, recent.Count);
            Assert.Equal("level.load", recent[0].EventId);
            Assert.Equal("test", recent[1].EventId);
            string path = Assert.IsType<string>(logger.CurrentSessionPath);
            string[] lines = ReadActiveSessionLines(path);
            Assert.Equal(3, lines.Length);
            using JsonDocument record = JsonDocument.Parse(lines[1]);
            Assert.Equal("Warning", record.RootElement.GetProperty("Severity").GetString());
            Assert.Equal("CAVERN_A", record.RootElement.GetProperty("Properties")
                .GetProperty("level").GetString());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void ActiveSessionCanBeReadBeforeAndAfterMoreWrites()
    {
        string directory = NewDirectory();
        try
        {
            string path;
            using (var logger = new FileDiagnosticSession(Options(directory)))
            {
                path = Assert.IsType<string>(logger.CurrentSessionPath);
                logger.Log(DiagnosticSeverity.Information, "first", "First event");
                Assert.Single(ReadActiveSessionLines(path));

                logger.Log(DiagnosticSeverity.Information, "second", "Second event");
                Assert.Equal(2, ReadActiveSessionLines(path).Length);
            }

            // Once the writer closes, ordinary exclusive-of-writers reads also work.
            Assert.Equal(2, File.ReadAllLines(path).Length);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void CrashReportContainsFullExceptionContextBuildAndPriorEvents()
    {
        string directory = NewDirectory();
        try
        {
            using var logger = new FileDiagnosticSession(Options(directory));
            logger.Log(DiagnosticSeverity.Warning, "world.enter", "Entered wind zone");
            var build = new DiagnosticBuildInfo("TestGame", "1.2", "BUILD-A", ".NET", "Test OS", "x64");
            var exception = new InvalidOperationException("outer fault",
                new ArgumentException("inner fault"));
            string path = Assert.IsType<string>(logger.WriteCrashReport(exception, "Test.Main", build));
            string text = File.ReadAllText(path);
            Assert.Contains("outer fault", text);
            Assert.Contains("inner fault", text);
            Assert.Contains("BUILD-A", text);
            Assert.Contains("world.enter", text);
            Assert.Contains("Test.Main", text);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void FatalCaptureWritesOnlyOneReportWhenInvokedTwice()
    {
        string directory = NewDirectory();
        try
        {
            using var logger = new FileDiagnosticSession(Options(directory));
            var build = new DiagnosticBuildInfo("TestGame", "1", "id", ".NET", "OS", "x64");
            using var hooks = new CrashExceptionHooks(logger, build);
            Assert.NotNull(hooks.CaptureFatal(new Exception("first"), "Main"));
            Assert.Null(hooks.CaptureFatal(new Exception("second"), "AppDomain"));
            Assert.Single(Directory.GetFiles(directory, "crash_*.txt"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void FilePermissionOrInvalidDirectoryDoesNotBreakGameplayLogging()
    {
        string root = NewDirectory();
        try
        {
            string existingFile = Path.Combine(root, "not-a-directory");
            File.WriteAllText(existingFile, "occupied");
            using var logger = new FileDiagnosticSession(Options(existingFile));
            Assert.False(logger.IsFileLoggingAvailable);
            logger.Log(DiagnosticSeverity.Error, "test", "Still running");
            Assert.Single(logger.RecentSnapshot());
            var build = new DiagnosticBuildInfo("Test", "1", "id", ".NET", "OS", "x64");
            Assert.Null(logger.WriteCrashReport(new Exception("test"), "test", build));
            Assert.NotNull(logger.LastStorageError);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void SessionRetentionAndFileRotationAreBounded()
    {
        string directory = NewDirectory();
        try
        {
            var settings = Options(directory) with { MaxSessionLogFiles = 2, MaxSessionFileBytes = 4096 };
            using var logger = new FileDiagnosticSession(settings);
            for (int i = 0; i < 24; i++)
                logger.Log(DiagnosticSeverity.Information, "repeat", new string('x', 1024));
            Assert.InRange(Directory.GetFiles(directory, "session_*.jsonl").Length, 1, 2);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    // File.ReadAllLines uses FileShare.Read and therefore conflicts with the
    // live session writer. Both handles must allow the other's access: the
    // logger opens FileAccess.Write / FileShare.Read, while this reader opens
    // FileAccess.Read / FileShare.ReadWrite.
    private static string[] ReadActiveSessionLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) is not null)
            lines.Add(line);
        return lines.ToArray();
    }

    private static DiagnosticSettings Options(string directory, int recent = 8) => new()
    {
        LogDirectory = directory,
        RecentEventCapacity = recent,
        MaxSessionLogFiles = 4,
        MaxCrashReports = 3
    };

    private static string NewDirectory()
    {
        string dir = Path.Combine(Path.GetTempPath(), "D01_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }
}
