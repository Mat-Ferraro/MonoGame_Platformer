namespace PlatformerSandbox.Diagnostics;

/// <summary>Immutable, bounded diagnostic breadcrumb. No input text or device secrets.</summary>
public sealed record DiagnosticBreadcrumb(DateTimeOffset TimestampUtc, string Kind,
    string Detail, long? WorldTick);

/// <summary>An attempted operation that has NOT been confirmed as committed.</summary>
public sealed record DiagnosticPendingOperation(DateTimeOffset BeganUtc, string Kind,
    string Detail, long? WorldTick);

/// <summary>
/// Safe snapshot for attaching host-provided context to a crash report. State
/// is the LAST KNOWN COMMITTED state; PendingOperation describes an operation
/// started afterward whose outcome has not been confirmed.
/// </summary>
public sealed record DiagnosticContextSnapshot(
    DateTimeOffset CapturedUtc,
    IReadOnlyDictionary<string, string> LastCommittedState,
    DiagnosticPendingOperation? PendingOperation,
    IReadOnlyList<DiagnosticBreadcrumb> RecentActivity);

/// <summary>
/// Reusable, MonoGame-independent crash context recorder. No disk writes and
/// no interaction with simulation state. Intended for one-way host adapters.
/// </summary>
public sealed class DiagnosticContextRecorder
{
    private const int MaxStateProperties = 96;
    private const int MaxKeyLength = 96;
    private const int MaxValueLength = 320;
    private const int MaxDetailLength = 768;
    private readonly object _gate = new();
    private readonly DiagnosticBreadcrumb?[] _ring;
    private Dictionary<string, string> _state = new(StringComparer.Ordinal);
    private DiagnosticPendingOperation? _pending;
    private int _head;
    private int _count;

    public DiagnosticContextRecorder(int recentActivityCapacity = 96)
    {
        if (recentActivityCapacity is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(recentActivityCapacity));
        _ring = new DiagnosticBreadcrumb[recentActivityCapacity];
    }

    /// <summary>Atomically replace context, preventing data from a previous level leaking forward.</summary>
    public void UpdateCommittedState(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var next = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (next.Count >= MaxStateProperties) break;
            if (string.IsNullOrWhiteSpace(pair.Key)) continue;
            next[Bound(pair.Key, MaxKeyLength)] = Bound(pair.Value ?? "", MaxValueLength);
        }
        lock (_gate) _state = next;
    }

    /// <summary>
    /// Capture an in-flight tick before entering gameplay. If simulation throws,
    /// this remains explicitly UNCOMMITTED in the crash report.
    /// </summary>
    public void BeginOperation(string kind, string detail, long? worldTick = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        lock (_gate) _pending = new DiagnosticPendingOperation(DateTimeOffset.UtcNow,
            Bound(kind, 64), Bound(detail ?? "", MaxDetailLength), worldTick);
    }

    /// <summary>Call only after a successful simulation commit.</summary>
    public void CommitOperation(string detail)
    {
        lock (_gate)
        {
            var finished = _pending;
            _pending = null;
            if (finished is not null)
                AppendLocked("tick.committed", Bound(detail, MaxDetailLength), finished.WorldTick);
        }
    }

    public void RecordActivity(string kind, string detail, long? worldTick = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        lock (_gate) AppendLocked(Bound(kind, 64), Bound(detail ?? "", MaxDetailLength), worldTick);
    }

    /// <summary>Use when a prepared scene supersedes an earlier in-flight context.</summary>
    public void ClearPendingOperation()
    {
        lock (_gate) _pending = null;
    }

    public DiagnosticContextSnapshot Snapshot()
    {
        lock (_gate)
        {
            var activity = new DiagnosticBreadcrumb[_count];
            for (int i = 0; i < _count; i++)
                activity[i] = _ring[(_head - _count + _ring.Length + i) % _ring.Length]!;
            return new DiagnosticContextSnapshot(DateTimeOffset.UtcNow,
                new Dictionary<string, string>(_state, StringComparer.Ordinal),
                _pending, Array.AsReadOnly(activity));
        }
    }

    private void AppendLocked(string kind, string detail, long? worldTick)
    {
        _ring[_head] = new DiagnosticBreadcrumb(DateTimeOffset.UtcNow, kind, detail, worldTick);
        _head = (_head + 1) % _ring.Length;
        _count = Math.Min(_count + 1, _ring.Length);
    }

    private static string Bound(string value, int limit) =>
        value.Length <= limit ? value : value[..limit] + " [truncated]";
}
