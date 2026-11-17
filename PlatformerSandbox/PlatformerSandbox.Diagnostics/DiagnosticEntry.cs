namespace PlatformerSandbox.Diagnostics;

/// <summary>A bounded, serializable, non-authoritative diagnostic record.</summary>
public sealed record DiagnosticEntry(
    DateTimeOffset TimestampUtc,
    DiagnosticSeverity Severity,
    string EventId,
    string Message,
    string? Exception,
    IReadOnlyDictionary<string, string>? Properties);
