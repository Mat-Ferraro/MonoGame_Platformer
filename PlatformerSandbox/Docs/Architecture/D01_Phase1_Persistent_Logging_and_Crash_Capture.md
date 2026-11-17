# D01 Phase 1 — Persistent Logging and Crash Capture

**Scope:** reusable diagnostics module and DesktopGL startup integration. This is an implementation candidate until built and validated locally.

## Architecture

- `PlatformerSandbox.Diagnostics` is a new pure .NET class library; it has no MonoGame, Gameplay or Simulation references.
- DesktopGL owns the per-process `FileDiagnosticSession` and `CrashExceptionHooks`. `Game1` and the movement motor remain unchanged.
- `Program.cs` catches exceptions escaping `Game1` creation, running or disposal, records the original exception and rethrows it. `AppDomain.UnhandledException` is a last-resort handler for other threads. `TaskScheduler.UnobservedTaskException` is logged as nonfatal, without changing observed status.
- `SimulationNoProgressException`, `SimulationFaultException`, and similar existing exceptions are recorded through the same top-level catch if they escape gameplay. Current Strict/Diagnostic/Resilient semantics are unchanged.
- Logs are JSONL for tooling; crash reports are readable text containing original exception/inner exceptions/stack, build identity, runtime, OS and bounded recent event history. No network upload or personal information collection is implemented.

## Files / installation

The ZIP is a source overlay rooted at `PlatformerSandbox/`. Extract into the existing solution root, **not** its parent directory. This package replaces only `Platforms/DesktopGL/Program.cs`; the other code is additive. It does not include `Game1.cs`, the movement motor, jump motor, or the recently validated freeze fixes.

The source-text snapshot omitted `.csproj` and `.sln` files, so this package includes `INSTALL_D01_Phase1.ps1` to add the new project and project references safely with the .NET CLI. It does not overwrite any existing project files and is safe to rerun. See `INSTALL_D01_Phase1.txt`.

## Defaults / limitations

- Log folder: `%LOCALAPPDATA%\PlatformerSandbox\Logs` on Windows, equivalent per-user LocalApplicationData elsewhere.
- JSONL session logs rotate at 8 MiB; retain eight session-log files. Crash reports retain the latest eight reports. The last 128 diagnostic events are kept in memory.
- All file I/O is best-effort. File/permission failures must not mask the original exception. Logs may contain local source paths; review before sharing.
- Main loop and log writer run on the same process. Power loss, forced termination, native faults, severe out-of-memory failures and indefinite hangs without watchdog exceptions are not guaranteed to produce a crash report. An external hang watchdog is deferred.
- Structured per-tick game-state capture, replay input history, crash-bundle export and UI reporting belong to D01 Phases 2–3.
- Diagnostics are synchronous and low-volume. Do not log physics contacts or per-frame metrics through this file logger.

## Acceptance

1. Build solution and run xUnit including `D01FileDiagnosticSessionTests`.
2. Launch DesktopGL Debug build with `--dev-crash-test`. This deliberately throws before constructing the game. Verify a new `crash_*.txt` and a `session_*.jsonl` appear in `%LOCALAPPDATA%\PlatformerSandbox\Logs`.
3. Verify the report contains exception type, message, stack, .NET/OS details, version, build identifier and `app.start` event.
4. Launch normally and exercise the wind/moving-platform scenario. Gameplay must be unchanged; no fatal report is expected.
5. Confirm normal exit writes `app.exit`. Simulate an unwritable log directory in the automated test: the app should not crash just because logging failed.
6. Validate a Strict-mode physics exception is still thrown and logged, never converted to a recovery by the diagnostics module.
