namespace PlatformerSandbox.Diagnostics;

/// <summary>
/// Diagnostic contract. Keep this out of authoritative physics hot paths.
/// A log call must not be used to decide whether a simulation step commits.
/// </summary>
public interface IDiagnosticLog
{
    void Log(DiagnosticSeverity severity, string eventId, string message,
        IReadOnlyDictionary<string, string>? properties = null);

    void LogException(DiagnosticSeverity severity, string eventId, Exception exception,
        IReadOnlyDictionary<string, string>? properties = null);
}
