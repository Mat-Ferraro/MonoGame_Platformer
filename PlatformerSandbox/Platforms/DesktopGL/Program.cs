using System;
using System.Collections.Generic;
using System.Reflection;
using PlatformerSandbox;
using PlatformerSandbox.Diagnostics;

// D01: the logger is created before Game1 and kept alive through its disposal.
// Only the desktop host depends on the logging library; authoritative simulation
// remains independent of file IO, console output, and MonoGame presentation.
var settings = DiagnosticSettings.ForLocalApplication("PlatformerSandbox");
var diagnostics = new FileDiagnosticSession(settings);
var contextRecorder = new DiagnosticContextRecorder();
using (diagnostics)
{
    var build = DiagnosticBuildInfo.ForAssembly(
        Assembly.GetEntryAssembly() ?? typeof(Game1).Assembly, "PlatformerSandbox");
    using var crashHooks = new CrashExceptionHooks(diagnostics, build, contextRecorder.Snapshot);
    using var runTracker = new DiagnosticRunTracker(settings.LogDirectory);
    if (runTracker.HadPreviousUncleanExit)
        diagnostics.Log(DiagnosticSeverity.Warning, "app.previous-unclean-exit",
            "A previous game session ended without a clean shutdown (may be a crash or forced termination).",
            new Dictionary<string, string> { ["count"] = runTracker.PreviousUncleanExitCount.ToString() });
    var exporter = new DiagnosticReportExporter(diagnostics, build, contextRecorder.Snapshot);
    bool devStall = false;
#if DEBUG
    devStall = Array.Exists(Environment.GetCommandLineArgs(), arg =>
        string.Equals(arg, "--dev-stall-after-world-step", StringComparison.OrdinalIgnoreCase));
#endif
    using var watchdog = new DiagnosticHangWatchdog(diagnostics, build, contextRecorder.Snapshot,
        stallThreshold: devStall ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(8));
    diagnostics.Log(DiagnosticSeverity.Information, "app.start",
        "DesktopGL startup", new Dictionary<string, string>
        {
            ["version"] = build.Version,
            ["buildId"] = build.BuildId,
            ["runtime"] = build.Runtime
        });
    try
    {
#if DEBUG
        // Deliberate smoke-test fault. Never enabled in a Release build.
        if (Array.Exists(Environment.GetCommandLineArgs(),
                arg => string.Equals(arg, "--dev-crash-test", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("D01 intentional crash-capture smoke test.");
#endif
        using (var game = new Game1(new GameDiagnosticBridge(contextRecorder, watchdog,
                   exporter.Export, runTracker.HadPreviousUncleanExit)))
        {
            game.Run();
        }
        diagnostics.Log(DiagnosticSeverity.Information, "app.exit", "Normal exit");
        runTracker.MarkCleanExit();
    }
    catch (Exception exception)
    {
        // Preserve original exception + stack for debugger and Windows crash handling.
        // AppDomain hook is last resort for exceptions on other threads.
        crashHooks.CaptureFatal(exception, "DesktopGL.Program.Main");
        throw;
    }
}
