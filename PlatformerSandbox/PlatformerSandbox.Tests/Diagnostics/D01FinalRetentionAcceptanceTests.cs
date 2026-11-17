using System;
using System.IO;
using System.Linq;
using PlatformerSandbox.Diagnostics;
using Xunit;

namespace PlatformerSandbox.Tests.Diagnostics;

/// <summary>
/// Final D01 acceptance coverage for retention. All tests operate in their own
/// disposable temporary directories and never touch a player's real log folder.
/// </summary>
public sealed class D01FinalRetentionAcceptanceTests
{
    private static readonly DiagnosticBuildInfo Build = new(
        "TestGame", "1.0.0", "test-build", ".NET", "test-os", "x64");

    [Fact]
    public void StartupPrunesOnlyOldDiagnosticFilesAndPreservesActiveSession()
    {
        string folder = NewFolder();
        try
        {
            string[] sessionSeeds = Seed(folder, "session_", ".jsonl", 6);
            string[] crashSeeds = Seed(folder, "crash_", ".txt", 5);
            string[] stallSeeds = Seed(folder, "stall_", ".txt", 5);
            string unrelated = Path.Combine(folder, "keep-this-file.txt");
            string marker = Path.Combine(folder, "run_test.running");
            File.WriteAllText(unrelated, "not a diagnostic report");
            File.WriteAllText(marker, "run-marker must not be deleted by retention");

            using (var log = NewSession(folder, maxSessions: 3, maxCrashes: 2, maxStalls: 2))
            {
                Assert.True(log.IsFileLoggingAvailable);
                Assert.Equal(3, Directory.GetFiles(folder, "session_*.jsonl").Length);
                Assert.Equal(2, Directory.GetFiles(folder, "crash_*.txt").Length);
                Assert.Equal(2, Directory.GetFiles(folder, "stall_*.txt").Length);
                Assert.True(File.Exists(log.CurrentSessionPath));
                Assert.True(File.Exists(sessionSeeds[^1])); // newest prior log retained
                Assert.False(File.Exists(sessionSeeds[0])); // oldest prior log pruned
                Assert.True(File.Exists(crashSeeds[^1]));
                Assert.False(File.Exists(crashSeeds[0]));
                Assert.True(File.Exists(stallSeeds[^1]));
                Assert.False(File.Exists(stallSeeds[0]));
                Assert.True(File.Exists(unrelated));
                Assert.True(File.Exists(marker));
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void NewCrashAndStallReportsPruneTheirOwnCategoriesIndependently()
    {
        string folder = NewFolder();
        try
        {
            using (var log = NewSession(folder, maxCrashes: 2, maxStalls: 2))
            {
                string? newestCrash = null;
                string? newestStall = null;
                for (int i = 0; i < 6; ++i)
                {
                    newestCrash = log.WriteCrashReport(
                        new InvalidOperationException("crash " + i), "test", Build);
                    newestStall = log.WriteStallReport(
                        "Game1.Update", TimeSpan.FromSeconds(i + 2), Build);
                    Assert.NotNull(newestCrash);
                    Assert.NotNull(newestStall);
                }

                Assert.Equal(2, Directory.GetFiles(folder, "crash_*.txt").Length);
                Assert.Equal(2, Directory.GetFiles(folder, "stall_*.txt").Length);
                Assert.True(File.Exists(newestCrash));
                Assert.True(File.Exists(newestStall));
                Assert.Contains("crash 5", File.ReadAllText(newestCrash!));
                Assert.Contains("Game1.Update", File.ReadAllText(newestStall!));
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void SessionRotationRetainsActiveWriterAndNeverExceedsFileCountLimit()
    {
        string folder = NewFolder();
        try
        {
            using (var log = new FileDiagnosticSession(new DiagnosticSettings
            {
                LogDirectory = folder,
                RecentEventCapacity = 8,
                MaxSessionLogFiles = 2,
                MaxSessionFileBytes = 4096
            }))
            {
                for (int i = 0; i < 16; ++i)
                {
                    log.Log(DiagnosticSeverity.Information, "rotation.test",
                        "event " + i + " " + new string('x', 1800));
                    Assert.InRange(Directory.GetFiles(folder, "session_*.jsonl").Length, 1, 2);
                }

                string current = Assert.IsType<string>(log.CurrentSessionPath);
                Assert.True(File.Exists(current));
                Assert.True(log.IsFileLoggingAvailable);
                // The active file is intentionally shared for reading, not deletion.
                using var stream = new FileStream(current, FileMode.Open,
                    FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                Assert.Contains("rotation.test", reader.ReadToEnd());
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    [Fact]
    public void ExportAlwaysKeepsNewZipEvenWhenOldFilesHaveFutureTimestamps()
    {
        string folder = NewFolder();
        try
        {
            string reports = Path.Combine(folder, "Reports");
            Directory.CreateDirectory(reports);
            for (int i = 0; i < 6; ++i)
            {
                string prior = Path.Combine(reports, $"diagnostics_prior_{i}.zip");
                File.WriteAllText(prior, "older diagnostic archive placeholder");
                File.SetLastWriteTimeUtc(prior, DateTime.UtcNow.AddDays(60 + i));
            }

            using (var log = NewSession(folder))
            {
                var exporter = new DiagnosticReportExporter(log, Build);
                var result = exporter.Export();
                Assert.True(result.Success, result.Error);
                string created = Assert.IsType<string>(result.FilePath);
                Assert.True(File.Exists(created), "A successful export must not prune itself");
                Assert.Equal(6, Directory.GetFiles(reports, "diagnostics_*.zip").Length);
                Assert.Empty(Directory.GetFiles(reports, "*.tmp"));
            }
        }
        finally { Directory.Delete(folder, recursive: true); }
    }

    private static FileDiagnosticSession NewSession(string directory,
        int maxSessions = 8, int maxCrashes = 8, int maxStalls = 8) =>
        new(new DiagnosticSettings
        {
            LogDirectory = directory,
            MaxSessionLogFiles = maxSessions,
            MaxCrashReports = maxCrashes,
            MaxStallReports = maxStalls
        });

    private static string[] Seed(string directory, string prefix, string extension, int count)
    {
        var paths = new string[count];
        for (int i = 0; i < count; ++i)
        {
            paths[i] = Path.Combine(directory, prefix + $"seed_{i:D2}" + extension);
            File.WriteAllText(paths[i], "old diagnostic file " + i);
            File.SetLastWriteTimeUtc(paths[i], DateTime.UtcNow.AddMinutes(-20 + i));
        }
        return paths;
    }

    private static string NewFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "D01_final_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
