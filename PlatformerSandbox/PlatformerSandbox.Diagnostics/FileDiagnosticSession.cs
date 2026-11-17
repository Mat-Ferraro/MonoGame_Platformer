using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlatformerSandbox.Diagnostics;

/// <summary>
/// Per-process JSONL session log + human-readable crash reports. Thread safe.
/// IO failures are isolated from gameplay, and retained in LastStorageError.
/// Synchronous low-volume event logging is intentional for first-phase diagnostics;
/// never call this for every physics contact or every game tick.
/// </summary>
public sealed class FileDiagnosticSession : IDiagnosticLog, IDisposable
{
    private const int MaxMessageChars = 2048;
    private const int MaxExceptionCharsInEvent = 12 * 1024;
    private readonly object _gate = new();
    private readonly DiagnosticSettings _settings;
    private readonly DiagnosticEntry?[] _recent;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
    private int _nextRecent;
    private int _recentCount;
    private bool _disposed;
    private FileStream? _stream;
    private StreamWriter? _writer;
    private string? _currentSessionPath;

    public FileDiagnosticSession(DiagnosticSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        _settings = settings;
        _recent = new DiagnosticEntry[settings.RecentEventCapacity];
        lock (_gate)
        {
            TryStorage(() =>
            {
                Directory.CreateDirectory(_settings.LogDirectory);
                OpenNewSessionFile();
                PruneFiles("session_*.jsonl", _settings.MaxSessionLogFiles, _currentSessionPath);
                PruneFiles("crash_*.txt", _settings.MaxCrashReports, null);
                PruneFiles("stall_*.txt", _settings.MaxStallReports, null);
            });
        }
    }

    public string LogDirectory => _settings.LogDirectory;
    public string? CurrentSessionPath { get { lock (_gate) return _currentSessionPath; } }
    public string? LastStorageError { get; private set; }
    public bool IsFileLoggingAvailable { get { lock (_gate) return _writer is not null; } }

    public void Log(DiagnosticSeverity severity, string eventId, string message,
        IReadOnlyDictionary<string, string>? properties = null) =>
        Record(severity, eventId, message, null, properties);

    public void LogException(DiagnosticSeverity severity, string eventId, Exception exception,
        IReadOnlyDictionary<string, string>? properties = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Record(severity, eventId, exception.Message, exception, properties);
    }

    private void Record(DiagnosticSeverity severity, string eventId, string message,
        Exception? exception, IReadOnlyDictionary<string, string>? properties)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventId);
        ArgumentNullException.ThrowIfNull(message);
        var normalized = new DiagnosticEntry(
            DateTimeOffset.UtcNow, severity, Limit(eventId, 96), Limit(message, MaxMessageChars),
            exception is null ? null : Limit(exception.ToString(), MaxExceptionCharsInEvent),
            CopyProperties(properties));

        lock (_gate)
        {
            if (_disposed) return;
            _recent[_nextRecent] = normalized;
            _nextRecent = (_nextRecent + 1) % _recent.Length;
            _recentCount = Math.Min(_recentCount + 1, _recent.Length);
            TryStorage(() => WriteEntry(normalized));
        }
    }

    public IReadOnlyList<DiagnosticEntry> RecentSnapshot()
    {
        lock (_gate) return SnapshotLocked();
    }

    /// <summary>
    /// Write a distinct crash report. This method never rethrows a disk/permission
    /// exception; the original game failure must remain the debugger's exception.
    /// </summary>
    public string? WriteCrashReport(Exception exception, string source, DiagnosticBuildInfo build,
        DiagnosticContextSnapshot? context = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(build);
        lock (_gate)
        {
            if (_disposed) return null;
            string? writtenPath = null;
            TryStorage(() =>
            {
                Directory.CreateDirectory(_settings.LogDirectory);
                string reportPath = Path.Combine(_settings.LogDirectory,
                    $"crash_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.txt");
                string text = ComposeReport(exception, source, build, SnapshotLocked(), context);
                // CreateNew ensures a concurrently captured crash cannot overwrite another.
                using (var file = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
                {
                    writer.Write(text);
                    writer.Flush();
                    file.Flush(flushToDisk: true);
                }
                writtenPath = reportPath;
                _stream?.Flush(flushToDisk: true);
                PruneFiles("crash_*.txt", _settings.MaxCrashReports, reportPath);
            });
            return writtenPath;
        }
    }

    /// <summary>
    /// Non-fatal suspected stall report. The watchdog runs on another thread:
    /// the blocked game's stack trace cannot be reliably captured here.
    /// </summary>
    public string? WriteStallReport(string operation, TimeSpan elapsed, DiagnosticBuildInfo build,
        DiagnosticContextSnapshot? context = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(build);
        lock (_gate)
        {
            if (_disposed) return null;
            string? writtenPath = null;
            TryStorage(() =>
            {
                Directory.CreateDirectory(_settings.LogDirectory);
                string reportPath = Path.Combine(_settings.LogDirectory,
                    $"stall_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}.txt");
                string text = ComposeReport(null, $"{operation} (elapsed {elapsed.TotalMilliseconds:F0} ms)",
                    build, SnapshotLocked(), context);
                using (var file = new FileStream(reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(file, new UTF8Encoding(false)))
                {
                    writer.Write(text);
                    writer.Flush();
                    file.Flush(flushToDisk: true);
                }
                writtenPath = reportPath;
                PruneFiles("stall_*.txt", _settings.MaxStallReports, reportPath);
            });
            return writtenPath;
        }
    }

    private void WriteEntry(DiagnosticEntry entry)
    {
        if (_writer is null || _stream is null) return;
        string json = JsonSerializer.Serialize(entry, JsonOptions);
        long requiredBytes = Encoding.UTF8.GetByteCount(json) + Environment.NewLine.Length;
        if (_stream.Length > 0 && _stream.Length + requiredBytes > _settings.MaxSessionFileBytes)
        {
            _writer.Dispose();
            _writer = null;
            _stream = null;
            OpenNewSessionFile();
            PruneFiles("session_*.jsonl", _settings.MaxSessionLogFiles, _currentSessionPath);
        }
        _writer?.WriteLine(json); // AutoFlush; reports survive ordinary process crashes.
    }

    private void OpenNewSessionFile()
    {
        string path = Path.Combine(_settings.LogDirectory,
            $"session_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{Environment.ProcessId}_{Guid.NewGuid():N}.jsonl");
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        _stream = stream;
        _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        _currentSessionPath = path;
    }

    private static string ComposeReport(Exception? exception, string source, DiagnosticBuildInfo build,
        IReadOnlyList<DiagnosticEntry> events, DiagnosticContextSnapshot? context)
    {
        var sb = new StringBuilder(4096);
        sb.AppendLine($"{build.Application} D01 diagnostic {(exception is null ? "suspected stall" : "crash")} report (manual sharing only)");
        sb.AppendLine($"UTC: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine($"Source: {source}");
        sb.AppendLine($"Application: {build.Application}");
        sb.AppendLine($"Version: {build.Version}");
        sb.AppendLine($"Build ID: {build.BuildId}");
        sb.AppendLine($".NET: {build.Runtime}");
        sb.AppendLine($"OS: {build.OperatingSystem}");
        sb.AppendLine($"Architecture: {build.ProcessArchitecture}");
        sb.AppendLine();
        if (exception is null)
        {
            sb.AppendLine("=== Suspected stall (NOT an exception) ===");
            sb.AppendLine("An Update/Draw operation exceeded the watchdog threshold.");
            sb.AppendLine("The monitoring thread cannot capture the blocked game thread's stack.");
            sb.AppendLine("The operation may eventually complete; this report does not change gameplay state.");
        }
        else
        {
            sb.AppendLine("=== Exception (including inner exceptions and stack) ===");
            sb.AppendLine(exception.ToString());
        }
        sb.AppendLine();
        sb.AppendLine($"=== Most recent {events.Count} diagnostic events (oldest first) ===");
        foreach (DiagnosticEntry item in events)
        {
            sb.Append('[').Append(item.TimestampUtc.ToString("O")).Append("] ")
                .Append(item.Severity).Append(' ').Append(item.EventId).Append(": ")
                .AppendLine(item.Message);
            if (item.Properties is not null)
                foreach (var property in item.Properties)
                    sb.Append("  ").Append(property.Key).Append('=').AppendLine(property.Value);
        }
        if (context is not null)
        {
            sb.AppendLine();
            sb.AppendLine("=== Last known committed gameplay state (not a live solver read) ===");
            sb.AppendLine($"Captured UTC: {context.CapturedUtc:O}");
            foreach (var item in context.LastCommittedState.OrderBy(p => p.Key, StringComparer.Ordinal))
                sb.Append(item.Key).Append(" = ").AppendLine(item.Value);
            sb.AppendLine();
            sb.AppendLine("=== In-flight attempt (NOT committed; may have thrown) ===");
            if (context.PendingOperation is DiagnosticPendingOperation pending)
                sb.Append('[').Append(pending.BeganUtc.ToString("O")).Append("] ")
                    .Append(pending.Kind).Append(" tick=")
                    .Append(pending.WorldTick?.ToString() ?? "none").Append(' ')
                    .AppendLine(pending.Detail);
            else sb.AppendLine("none");
            sb.AppendLine();
            sb.AppendLine($"=== Recent gameplay activity ({context.RecentActivity.Count}, oldest first) ===");
            foreach (var item in context.RecentActivity)
                sb.Append('[').Append(item.TimestampUtc.ToString("O")).Append("] ")
                    .Append(item.Kind).Append(" tick=")
                    .Append(item.WorldTick?.ToString() ?? "none").Append(' ')
                    .AppendLine(item.Detail);
        }
        sb.AppendLine();
        sb.AppendLine("Reports may contain source file paths. Review before sharing.");
        return sb.ToString();
    }

    private DiagnosticEntry[] SnapshotLocked()
    {
        var output = new DiagnosticEntry[_recentCount];
        for (int i = 0; i < output.Length; i++)
            output[i] = _recent[(_nextRecent - _recentCount + _recent.Length + i) % _recent.Length]!;
        return output;
    }

    private static IReadOnlyDictionary<string, string>? CopyProperties(
        IReadOnlyDictionary<string, string>? properties)
    {
        if (properties is null || properties.Count == 0) return null;
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in properties)
        {
            if (copy.Count >= 16) break;
            copy[Limit(kv.Key, 96)] = Limit(kv.Value ?? "", 256);
        }
        return copy;
    }

    private static string Limit(string s, int max) => s.Length <= max ? s : s[..max] + " [truncated]";

    private void PruneFiles(string pattern, int allowed, string? preserve)
    {
        var files = new DirectoryInfo(_settings.LogDirectory).EnumerateFiles(pattern)
            .OrderByDescending(f => string.Equals(f.FullName, preserve, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(f => f.LastWriteTimeUtc).ThenByDescending(f => f.Name, StringComparer.Ordinal)
            .ToArray();
        int retained = 0;
        foreach (FileInfo file in files)
        {
            if (++retained <= allowed || string.Equals(file.FullName, preserve, StringComparison.OrdinalIgnoreCase))
                continue;
            // Failure to remove an old log should not block useful diagnostics.
            try { file.Delete(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                LastStorageError = "Log retention cleanup failed: " + e.Message;
            }
        }
    }

    private void TryStorage(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException or NotSupportedException)
        {
            LastStorageError = ex.GetType().Name + ": " + ex.Message;
            try { Console.Error.WriteLine("[Diagnostics] " + LastStorageError); }
            catch { /* console may not exist in a packaged game */ }
            // If session writer failed, release it and keep bounded in-memory history.
            try { _writer?.Dispose(); } catch (IOException) { }
            _writer = null;
            _stream = null;
            _currentSessionPath = null;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            TryStorage(() =>
            {
                _writer?.Flush();
                _stream?.Flush(flushToDisk: true);
                _writer?.Dispose();
                _stream = null;
                _writer = null;
            });
        }
    }
}
