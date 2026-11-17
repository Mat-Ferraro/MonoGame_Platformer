namespace PlatformerSandbox.Diagnostics;

/// <summary>
/// Monitors operations already in progress on the game thread. Never calls the
/// game thread or touches gameplay objects. Reports a suspected stall ONCE per
/// operation; it cannot recover a deadlock or retrieve the blocked thread stack.
/// </summary>
public sealed class DiagnosticHangWatchdog : IDisposable
{
    private readonly object _gate = new();
    private readonly FileDiagnosticSession _session;
    private readonly DiagnosticBuildInfo _build;
    private readonly Func<DiagnosticContextSnapshot>? _snapshot;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _stallThreshold;
    private readonly TimeSpan _slowThreshold;
    private readonly Timer? _timer;
    private long _sequence;
    private long _activeId;
    private long _activeStarted;
    private string? _activeOperation;
    private bool _stallEmitted;
    private bool _disposed;
    private long _lastSlowTimestamp;
    private bool _hasSlowTimestamp;

    public DiagnosticHangWatchdog(FileDiagnosticSession session, DiagnosticBuildInfo build,
        Func<DiagnosticContextSnapshot>? contextSnapshot = null,
        TimeSpan? stallThreshold = null, TimeSpan? slowThreshold = null,
        TimeProvider? clock = null, bool startTimer = true)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _build = build ?? throw new ArgumentNullException(nameof(build));
        _snapshot = contextSnapshot;
        _clock = clock ?? TimeProvider.System;
        _stallThreshold = stallThreshold ?? TimeSpan.FromSeconds(8);
        _slowThreshold = slowThreshold ?? TimeSpan.FromMilliseconds(250);
        if (_stallThreshold < TimeSpan.FromMilliseconds(100))
            throw new ArgumentOutOfRangeException(nameof(stallThreshold));
        if (_slowThreshold < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(slowThreshold));
        if (startTimer)
            _timer = new Timer(_ => CheckOnce(), null, TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMilliseconds(250));
    }

    /// <summary>Call on the game's own thread at the beginning of Update/Draw.</summary>
    public IDisposable BeginOperation(string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        lock (_gate)
        {
            if (_disposed) return NoopScope.Instance;
            long id = ++_sequence;
            _activeId = id;
            _activeOperation = operation.Length > 80 ? operation[..80] : operation;
            _activeStarted = _clock.GetTimestamp();
            _stallEmitted = false;
            return new Scope(this, id);
        }
    }

    /// <summary>
    /// Safe to invoke from the watchdog timer thread. Also callable directly in
    /// deterministic tests (with an injected TimeProvider).
    /// </summary>
    public void CheckOnce()
    {
        string? operation;
        TimeSpan elapsed;
        // Pausing on a debugger breakpoint is not a gameplay hang.
        if (System.Diagnostics.Debugger.IsAttached) return;
        lock (_gate)
        {
            if (_disposed || _activeOperation is null || _stallEmitted) return;
            elapsed = _clock.GetElapsedTime(_activeStarted);
            if (elapsed < _stallThreshold) return;
            _stallEmitted = true;
            operation = _activeOperation;
        }
        try
        {
            _session.Log(DiagnosticSeverity.Warning, "performance.possible-stall",
                "A game-thread operation exceeded the stall threshold.",
                new Dictionary<string, string> {
                    ["operation"] = operation!, ["elapsedMs"] = elapsed.TotalMilliseconds.ToString("F0") });
            DiagnosticContextSnapshot? context = null;
            if (_snapshot is not null)
            {
                try { context = _snapshot(); }
                catch (Exception e) when (e is not OutOfMemoryException)
                { _session.Log(DiagnosticSeverity.Warning, "diagnostics.stall-context-failed", e.GetType().Name); }
            }
            string? report = _session.WriteStallReport(operation!, elapsed, _build, context);
            LastStallReportPath = report;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            // Watchdog I/O is observational, never a reason to crash the game.
            LastError = e.GetType().Name + ": " + e.Message;
        }
    }

    public string? LastStallReportPath { get; private set; }
    public string? LastError { get; private set; }

    private void EndOperation(long id)
    {
        string? operation;
        TimeSpan elapsed;
        bool reportSlow;
        lock (_gate)
        {
            if (_disposed || id != _activeId || _activeOperation is null) return;
            operation = _activeOperation;
            elapsed = _clock.GetElapsedTime(_activeStarted);
            _activeOperation = null;
            _activeId = 0;
            long now = _clock.GetTimestamp();
            reportSlow = !System.Diagnostics.Debugger.IsAttached && elapsed >= _slowThreshold &&
                (!_hasSlowTimestamp || _clock.GetElapsedTime(_lastSlowTimestamp, now) >= TimeSpan.FromSeconds(10));
            if (reportSlow)
            {
                _lastSlowTimestamp = now;
                _hasSlowTimestamp = true;
            }
        }
        if (reportSlow)
        {
            try { _session.Log(DiagnosticSeverity.Warning, "performance.slow-operation",
                "A game-thread operation completed slowly.",
                new Dictionary<string, string> {
                    ["operation"] = operation!, ["elapsedMs"] = elapsed.TotalMilliseconds.ToString("F0") }); }
            catch (Exception e) when (e is not OutOfMemoryException)
            { LastError = e.GetType().Name + ": " + e.Message; }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _activeOperation = null;
        }
        _timer?.Dispose();
    }

    private sealed class Scope : IDisposable
    {
        private DiagnosticHangWatchdog? _owner;
        private readonly long _id;
        public Scope(DiagnosticHangWatchdog owner, long id) { _owner = owner; _id = id; }
        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.EndOperation(_id);
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly NoopScope Instance = new();
        public void Dispose() { }
    }
}
