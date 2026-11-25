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
    private readonly ActivityEntry?[] _ring;
    private readonly record struct ActivityEntry(DateTimeOffset Timestamp, string Kind, object Detail, long? Tick);
    private sealed record PendingEntry(DateTimeOffset Timestamp, string Kind, object Detail, long? Tick);
    private interface IDeferredState { IReadOnlyDictionary<string, string> Format(); }
    private sealed record DeferredState<T>(T Value, Func<T, IReadOnlyDictionary<string, string>> Formatter) : IDeferredState
    { public IReadOnlyDictionary<string, string> Format() => Formatter(Value); }
    private interface IDeferredDetail { string Format(); }
    private sealed record DeferredDetail<T>(T Value, Func<T, string> Formatter) : IDeferredDetail
    { public string Format() => Formatter(Value); }
    private IDeferredState? _deferredState;
    private Dictionary<string, string> _state = new(StringComparer.Ordinal);
    private PendingEntry? _pending;
    private int _head;
    private int _count;

    public DiagnosticContextRecorder(int recentActivityCapacity = 96)
    {
        if (recentActivityCapacity is < 1 or > 1024)
            throw new ArgumentOutOfRangeException(nameof(recentActivityCapacity));
        _ring = new ActivityEntry?[recentActivityCapacity];
    }

    /// <summary>Atomically replace context, preventing data from a previous level leaking forward.</summary>
    public void UpdateCommittedState(IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var next = CopyBounded(values);
        lock (_gate) { _state = next; _deferredState = null; }
    }

    /// <summary>Value and formatter must be immutable, detached from live game state,
    /// and bounded. Formatting happens only when a crash/export reader requests context.</summary>
    public void UpdateCommittedState<T>(T value, Func<T, IReadOnlyDictionary<string, string>> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        lock (_gate) _deferredState = new DeferredState<T>(value, formatter);
    }
    private static Dictionary<string, string> CopyBounded(IReadOnlyDictionary<string, string> values)
    {
        var next = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            if (next.Count >= MaxStateProperties) break;
            if (string.IsNullOrWhiteSpace(pair.Key)) continue;
            next[Bound(pair.Key, MaxKeyLength)] = Bound(pair.Value ?? "", MaxValueLength);
        }
        return next;
    }

    /// <summary>
    /// Capture an in-flight tick before entering gameplay. If simulation throws,
    /// this remains explicitly UNCOMMITTED in the crash report.
    /// </summary>
    public void BeginOperation(string kind, string detail, long? worldTick = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        lock (_gate) _pending = new PendingEntry(DateTimeOffset.UtcNow,
            Bound(kind, 64), Bound(detail ?? "", MaxDetailLength), worldTick);
    }

    public void BeginOperation<T>(string kind, T value, Func<T, string> formatter, long? worldTick = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(formatter);
        lock (_gate) _pending = new PendingEntry(DateTimeOffset.UtcNow, Bound(kind, 64),
            new DeferredDetail<T>(value, formatter), worldTick);
    }

    /// <summary>Call only after a successful simulation commit.</summary>
    public void CommitOperation(string detail)
    {
        lock (_gate)
        {
            var finished = _pending;
            _pending = null;
            if (finished is not null)
                AppendLocked("tick.committed", Bound(detail, MaxDetailLength), finished.Tick);
        }
    }

    public void CommitOperation<T>(T value, Func<T, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        lock (_gate)
        {
            var finished = _pending;
            _pending = null;
            if (finished is not null) AppendLocked("tick.committed", new DeferredDetail<T>(value, formatter), finished.Tick);
        }
    }
    public void RecordActivity<T>(string kind, T value, Func<T, string> formatter, long? worldTick = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        ArgumentNullException.ThrowIfNull(formatter);
        lock (_gate) AppendLocked(Bound(kind, 64), new DeferredDetail<T>(value, formatter), worldTick);
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
            {
                var entry = _ring[(_head - _count + _ring.Length + i) % _ring.Length]!.Value;
                activity[i] = new DiagnosticBreadcrumb(entry.Timestamp, entry.Kind, FormatDetail(entry.Detail), entry.Tick);
            }
            var pending = _pending is null ? null : new DiagnosticPendingOperation(
                _pending.Timestamp, _pending.Kind, FormatDetail(_pending.Detail), _pending.Tick);
            return new DiagnosticContextSnapshot(DateTimeOffset.UtcNow,
                _deferredState is null ? new Dictionary<string, string>(_state, StringComparer.Ordinal) : CopyBounded(_deferredState.Format()),
                pending, Array.AsReadOnly(activity));
        }
    }

    private static string FormatDetail(object detail) => detail is string text ? text :
        Bound(((IDeferredDetail)detail).Format() ?? "", MaxDetailLength);

    private void AppendLocked(string kind, object detail, long? worldTick)
    {
        _ring[_head] = new ActivityEntry(DateTimeOffset.UtcNow, kind, detail, worldTick);
        _head = (_head + 1) % _ring.Length;
        _count = Math.Min(_count + 1, _ring.Length);
    }

    private static string Bound(string value, int limit) =>
        value.Length <= limit ? value : value[..limit] + " [truncated]";
}
