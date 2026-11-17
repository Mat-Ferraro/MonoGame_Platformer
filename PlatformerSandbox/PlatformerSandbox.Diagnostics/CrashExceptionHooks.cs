using System.Threading;

namespace PlatformerSandbox.Diagnostics;

/// <summary>
/// Host-owned crash hooks. Does not swallow/recover an exception. The top-level
/// catch runs before process unwind; AppDomain is last resort for worker faults.
/// </summary>
public sealed class CrashExceptionHooks : IDisposable
{
    private readonly FileDiagnosticSession _session;
    private readonly DiagnosticBuildInfo _build;
    private readonly Func<DiagnosticContextSnapshot>? _contextSnapshot;
    private int _fatalCaptured;
    private bool _disposed;

    public CrashExceptionHooks(FileDiagnosticSession session, DiagnosticBuildInfo build,
        Func<DiagnosticContextSnapshot>? contextSnapshot = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _build = build ?? throw new ArgumentNullException(nameof(build));
        _contextSnapshot = contextSnapshot;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobservedTask;
    }

    /// <summary>Return the saved report path or null if storage was unavailable.</summary>
    public string? CaptureFatal(Exception exception, string source)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (Interlocked.Exchange(ref _fatalCaptured, 1) != 0) return null;
        try
        {
            _session.LogException(DiagnosticSeverity.Fatal, "app.unhandled-exception", exception,
                new Dictionary<string, string> { ["source"] = source });
            DiagnosticContextSnapshot? context = null;
            if (_contextSnapshot is not null)
            {
                try { context = _contextSnapshot(); }
                catch (Exception providerFailure) when (providerFailure is not OutOfMemoryException)
                {
                    _session.Log(DiagnosticSeverity.Warning, "context.capture-failed",
                        providerFailure.GetType().Name);
                }
            }
            return _session.WriteCrashReport(exception, source, _build, context);
        }
        catch (Exception loggingFailure) when (loggingFailure is not OutOfMemoryException)
        {
            // A secondary diagnostic failure must never replace the real crash.
            try { Console.Error.WriteLine("[Diagnostics] Crash-report failure: " + loggingFailure); }
            catch { /* stderr may not exist */ }
            return null;
        }
    }

    private void OnUnhandled(object sender, UnhandledExceptionEventArgs args)
    {
        if (args.ExceptionObject is Exception exception)
            CaptureFatal(exception, "AppDomain.UnhandledException");
        else
            CaptureFatal(new InvalidOperationException("Unhandled non-Exception object: " +
                (args.ExceptionObject?.GetType().FullName ?? "null")), "AppDomain.UnhandledException");
    }

    private void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        // Notification does not necessarily mean the process will terminate.
        // Do not mark observed: this diagnostic hook must not change runtime behavior.
        try { _session.LogException(DiagnosticSeverity.Error, "app.unobserved-task", args.Exception); }
        catch (Exception loggingFailure) when (loggingFailure is not OutOfMemoryException)
        {
            try { Console.Error.WriteLine("[Diagnostics] Task exception could not be logged: " + loggingFailure); }
            catch { /* stderr may not exist */ }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTask;
    }
}
