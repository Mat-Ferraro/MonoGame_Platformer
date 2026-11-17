using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PlatformerSandbox.Diagnostics;

public sealed record DiagnosticExportResult(bool Success, string? FilePath, string? Error,
    IReadOnlyList<string> IncludedFiles);

/// <summary>
/// Creates a local, opt-in, review-before-sharing ZIP. Never uploads anything.
/// Uses only known diagnostic filename patterns; avoids arbitrary file paths.
/// </summary>
public sealed class DiagnosticReportExporter
{
    private const int MaxFileBytes = 8 * 1024 * 1024;
    private const int MaxTotalBytes = 24 * 1024 * 1024;
    private readonly FileDiagnosticSession _session;
    private readonly DiagnosticBuildInfo _build;
    private readonly Func<DiagnosticContextSnapshot>? _context;

    public DiagnosticReportExporter(FileDiagnosticSession session, DiagnosticBuildInfo build,
        Func<DiagnosticContextSnapshot>? context = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _build = build ?? throw new ArgumentNullException(nameof(build));
        _context = context;
    }

    public DiagnosticExportResult Export()
    {
        string reportsDir = Path.Combine(_session.LogDirectory, "Reports");
        string? working = null;
        try
        {
            Directory.CreateDirectory(reportsDir);
            string filename = $"diagnostics_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.zip";
            string finished = Path.Combine(reportsDir, filename);
            working = finished + ".tmp";
            var included = new List<string>();
            var skipped = new List<string>();
            int budget = MaxTotalBytes;
            using (var file = new FileStream(working, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                // Defensive fixed filename categories, latest first; no recursive scan.
                foreach (var spec in new[] {
                    (Pattern: "crash_*.txt", Take: 2),
                    (Pattern: "stall_*.txt", Take: 2),
                    (Pattern: "session_*.jsonl", Take: 3) })
                {
                    foreach (FileInfo info in new DirectoryInfo(_session.LogDirectory)
                        .EnumerateFiles(spec.Pattern).OrderByDescending(x => x.LastWriteTimeUtc)
                        .ThenByDescending(x => x.Name, StringComparer.Ordinal).Take(spec.Take))
                    {
                        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                        { skipped.Add(info.Name + " (linked file excluded)"); continue; }
                        if (info.Length > MaxFileBytes || info.Length > budget)
                        { skipped.Add(info.Name + " (size limit)"); continue; }
                        // Shared-read: the active game session may have a writer open.
                        try
                        {
                            using var read = new FileStream(info.FullName, FileMode.Open, FileAccess.Read,
                                FileShare.ReadWrite | FileShare.Delete);
                            using var input = new StreamReader(read, Encoding.UTF8, true);
                            string content = input.ReadToEnd();
                            if (Encoding.UTF8.GetByteCount(content) > MaxFileBytes ||
                                Encoding.UTF8.GetByteCount(content) > budget)
                            { skipped.Add(info.Name + " (size changed)"); continue; }
                            budget -= Encoding.UTF8.GetByteCount(content);
                            WriteText(archive, info.Name, RedactUserHome(content));
                            included.Add(info.Name);
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                        { skipped.Add(info.Name + " (unavailable: " + e.GetType().Name + ")"); }
                    }
                }
                if (_context is not null)
                {
                    try
                    {
                        DiagnosticContextSnapshot ctx = _context();
                        var state = new StringBuilder();
                        state.AppendLine("Current diagnostic snapshot (last committed state):");
                        foreach (var item in ctx.LastCommittedState.OrderBy(p => p.Key, StringComparer.Ordinal))
                            state.Append(item.Key).Append('=').AppendLine(item.Value);
                        state.AppendLine("In-flight attempt (NOT committed):");
                        state.AppendLine(ctx.PendingOperation?.ToString() ?? "none");
                        state.AppendLine("Recent activity:");
                        foreach (var item in ctx.RecentActivity)
                            state.AppendLine(item.ToString());
                        WriteText(archive, "gameplay-context.txt", RedactUserHome(state.ToString()));
                        included.Add("gameplay-context.txt");
                    }
                    catch (Exception e) when (e is not OutOfMemoryException)
                    { skipped.Add("gameplay-context.txt (unavailable: " + e.GetType().Name + ")"); }
                }
                WriteText(archive, "README-REVIEW-BEFORE-SHARING.txt",
                    "PlatformerSandbox diagnostic bundle (manually exported; never uploaded).\n" +
                    "User home paths are replaced where detected. Other personal data, file paths, " +
                    "or names may still appear. REVIEW each file before sharing.\n" +
                    "The bundle may include an active session and a suspected-stall report.\n" +
                    "Stall reports do not contain the blocked main thread's stack trace.\n");
                WriteText(archive, "manifest.json", JsonSerializer.Serialize(new
                {
                    Application = _build.Application,
                    _build.Version, _build.BuildId, _build.Runtime,
                    _build.OperatingSystem, _build.ProcessArchitecture,
                    ExportedUtc = DateTimeOffset.UtcNow,
                    Included = included, Skipped = skipped
                }, new JsonSerializerOptions { WriteIndented = true }));
            }
            File.Move(working, finished);
            working = null;
            // Bound the number of manually generated exports as well.
            try
            {
                foreach (FileInfo old in new DirectoryInfo(reportsDir)
                    .EnumerateFiles("diagnostics_*.zip")
                    // Always keep the ZIP we just created, even if older exports
                    // have future timestamps because the machine clock changed.
                    .OrderByDescending(x => string.Equals(x.FullName, finished,
                        StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(x => x.LastWriteTimeUtc)
                    .ThenByDescending(x => x.Name, StringComparer.Ordinal)
                    .Skip(6))
                {
                    try { old.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { /* export already exists; pruning is best-effort */ }
            catch (UnauthorizedAccessException) { /* preserve the exported bundle */ }
            _session.Log(DiagnosticSeverity.Information, "diagnostics.export.created",
                "A diagnostic ZIP was created locally; review it before sharing.",
                new Dictionary<string, string> { ["files"] = included.Count.ToString() });
            return new DiagnosticExportResult(true, finished, null, included);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or
            System.Security.SecurityException or InvalidOperationException or ArgumentException)
        {
            return new DiagnosticExportResult(false, null,
                e.GetType().Name + ": " + e.Message, Array.Empty<string>());
        }
        finally
        {
            if (working is not null)
            {
                try { File.Delete(working); } catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void WriteText(ZipArchive archive, string name, string content)
    {
        ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content);
    }

    private static string RedactUserHome(string input)
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(home)) return input;
        // Plain-text stack traces and JSON-escaped strings use different slashes.
        return input.Replace(home.Replace("\\", "\\\\"), "<USER_HOME>", StringComparison.OrdinalIgnoreCase)
                    .Replace(home, "<USER_HOME>", StringComparison.OrdinalIgnoreCase);
    }
}
