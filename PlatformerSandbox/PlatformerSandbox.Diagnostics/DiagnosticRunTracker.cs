using System.Diagnostics;
using System.Text.Json;

namespace PlatformerSandbox.Diagnostics;

/// <summary>
/// One marker per process. A marker is removed ONLY by MarkCleanExit(); simply
/// disposing diagnostics during exception unwinding must not hide a crash.
/// An old marker means an unclean termination, not proof of an application bug.
/// </summary>
internal sealed record DiagnosticRunMarker(int ProcessId, long ProcessStartUtcTicks);

public sealed class DiagnosticRunTracker : IDisposable
{
    private readonly string? _markerPath;
    private bool _clean;

    public DiagnosticRunTracker(string logDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        try
        {
            Directory.CreateDirectory(logDirectory);
            // Ignore live processes, so two game instances don't accuse each other.
            var stale = new List<string>();
            foreach (string path in Directory.EnumerateFiles(logDirectory, "run_*.running").Take(256))
            {
                try
                {
                    DiagnosticRunMarker? marker = JsonSerializer.Deserialize<DiagnosticRunMarker>(File.ReadAllText(path));
                    if (marker is null || !IsOriginalProcessAlive(marker)) stale.Add(path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
                {
                    // A broken marker is not evidence of an unclean game shutdown.
                    LastError = "Could not inspect run marker: " + e.GetType().Name;
                }
            }
            PreviousUncleanExitCount = stale.Count;
            foreach (string stalePath in stale)
            {
                try { File.Delete(stalePath); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                { LastError = "Could not remove stale run marker: " + e.GetType().Name; }
            }
            using Process current = Process.GetCurrentProcess();
            var ownMarker = new DiagnosticRunMarker(current.Id, current.StartTime.ToUniversalTime().Ticks);
            _markerPath = Path.Combine(logDirectory,
                $"run_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss_fff}_{current.Id}_{Guid.NewGuid():N}.running");
            File.WriteAllText(_markerPath, JsonSerializer.Serialize(ownMarker));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            LastError = "Run tracking unavailable: " + e.GetType().Name + ": " + e.Message;
        }
    }

    public int PreviousUncleanExitCount { get; }
    public bool HadPreviousUncleanExit => PreviousUncleanExitCount != 0;
    public string? LastError { get; private set; }

    public void MarkCleanExit()
    {
        if (_clean) return;
        if (_markerPath is not null)
        {
            try { File.Delete(_markerPath); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { LastError = "Could not remove current run marker: " + e.GetType().Name; return; }
        }
        _clean = true;
    }

    // Intentionally does not mark clean; crash-reporting catch blocks also dispose.
    public void Dispose() { }

    private static bool IsOriginalProcessAlive(DiagnosticRunMarker marker)
    {
        if (marker.ProcessId <= 0) return false;
        try
        {
            using Process candidate = Process.GetProcessById(marker.ProcessId);
            if (candidate.HasExited) return false;
            // PID reuse must not obscure an unclean exit from an earlier run.
            try { return Math.Abs(candidate.StartTime.ToUniversalTime().Ticks - marker.ProcessStartUtcTicks)
                < TimeSpan.FromSeconds(2).Ticks; }
            catch (Exception e) when (e is InvalidOperationException or System.ComponentModel.Win32Exception)
            { return true; } // If access denied, don't accuse a still-running game.
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return false; }
    }
}
