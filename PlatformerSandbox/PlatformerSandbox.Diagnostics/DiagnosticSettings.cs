namespace PlatformerSandbox.Diagnostics;

/// <summary>Storage and memory limits, applied independently of gameplay policy.</summary>
public sealed record DiagnosticSettings
{
    public required string LogDirectory { get; init; }
    public int RecentEventCapacity { get; init; } = 128;
    public int MaxSessionLogFiles { get; init; } = 8;
    public int MaxCrashReports { get; init; } = 8;
    public int MaxStallReports { get; init; } = 8;
    public long MaxSessionFileBytes { get; init; } = 8L * 1024 * 1024;

    public static DiagnosticSettings ForLocalApplication(string appName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appName);
        // Caller controls app identity; do not accept paths or traversal as a name.
        if (appName is "." or ".." || appName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            appName.Contains('/') || appName.Contains('\\'))
            throw new ArgumentException("Application name must be a directory-safe single component.", nameof(appName));

        return new DiagnosticSettings
        {
            LogDirectory = Path.Combine(Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData), appName, "Logs")
        };
    }

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(LogDirectory);
        if (RecentEventCapacity is < 1 or > 2048)
            throw new ArgumentOutOfRangeException(nameof(RecentEventCapacity));
        if (MaxSessionLogFiles is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(MaxSessionLogFiles));
        if (MaxCrashReports is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(MaxCrashReports));
        if (MaxStallReports is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(MaxStallReports));
        if (MaxSessionFileBytes < 4096)
            throw new ArgumentOutOfRangeException(nameof(MaxSessionFileBytes));
    }
}
