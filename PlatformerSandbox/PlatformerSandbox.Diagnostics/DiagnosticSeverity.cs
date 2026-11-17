namespace PlatformerSandbox.Diagnostics;

/// <summary>Severity is descriptive only; it never controls gameplay fault policy.</summary>
public enum DiagnosticSeverity
{
    Debug,
    Information,
    Warning,
    Error,
    Fatal
}
