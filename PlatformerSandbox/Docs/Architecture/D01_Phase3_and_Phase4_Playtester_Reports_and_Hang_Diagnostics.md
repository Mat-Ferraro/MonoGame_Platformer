# D01 combined Phase 3 + Phase 4 — Playtester Reports and Hang Diagnostics

Status: copy-over implementation candidate; local .NET 10 compilation, xUnit, DesktopGL interactive acceptance required.

## Architectural boundaries

- `PlatformerSandbox.Diagnostics` remains reusable and **independent of MonoGame, simulation, gameplay, and LDtk**.
- `Platforms/DesktopGL/Program.cs` owns startup hooks, marker lifetime, exporter, and watchdog lifecycle.
- `Platforms/DesktopGL/Diagnostics/GameDiagnosticBridge.cs` is the project-specific adapter and presentation status provider.
- `Source/Game1.cs` only owns in-game input/display and Update/Draw operation scopes. No simulation solver changes.
- Existing Phase 1 and 2 crash handling, last committed snapshots, uncommitted tick records, and strict/resilient fault policy are preserved.

## Phase 3 — Playtester reporting

- **Ctrl+Shift+F12** exports a local ZIP under `%LOCALAPPDATA%\PlatformerSandbox\Logs\Reports` while the game is running (including title and pause screens). Plain F12 remains the movement request-order test.
- ZIP includes up to two latest crash reports, two latest stall reports, three session logs, current gameplay snapshot, `manifest.json` and a prominent review-before-sharing README.
- Exports are manually initiated and **never uploaded**. Paths under the executing user's home directory are replaced where recognizable; other sensitive material is not guaranteed removed, so manually inspect the ZIP before sharing.
- Export is limited to 8 MiB per diagnostic file and 24 MiB total input, and retains the latest six report archives. File selection is by fixed non-recursive patterns, excluding reparse points. Files too large or unavailable are listed as skipped in manifest.
- Each process writes its own `run_*.running` marker. A normal successful exit explicitly removes the marker. On next launch, markers from terminated processes are classified as **unclean termination** (may be a game crash, OS shutdown, kill, etc.), a warning is logged, and a persistent on-screen notice appears. Concurrent live instances are not mistaken for terminated processes when process state can be inspected.
- The run marker is intentionally not removed by `Dispose` during exception unwind.

## Phase 4 — Advanced observation

- Game1 Update and Draw are measured with `DiagnosticHangWatchdog` scopes. Completed operations exceeding 250 ms generate throttled slow-operation warnings (no crash).
- An **in-progress** Update or Draw exceeding 8 seconds is reported once as a *suspected stall* in `stall_*.txt`, including last committed world state, any in-flight operation, recent events and timing. In Debug builds the deliberate `--dev-stall-after-world-step` flag uses a 2s timeout and sleeps for 4s after committing a world step; it must continue running normally afterwards.
- Monitoring happens on a separate timer thread; it **does not** access MonoGame, touch simulation state, change fault policy, terminate, reset or recover the stuck game thread. No main-thread stack unwinding is attempted; reports explicitly explain that the blocked thread stack is unavailable.
- The watchdog deliberately ignores pauses imposed by a debugger; idle waiting with no active Update/Draw scope is not counted as a hang. This avoids mislabeling menu pauses, minimized windows, sleep and breakpoints.
- The recent input/activity history from Phase 2 acts as bounded **diagnostic breadcrumbs**, not a deterministic full replay recording. Full replay capture and out-of-process hang monitoring remain separate future enhancements if measured need justifies them.

## Debug testing (PowerShell from project root)

```powershell
dotnet build --configuration Debug
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj

# Watchdog test: creates stall_*.txt after a 2-second in-progress Update.
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=MOTION_LAB --dev-stall-after-world-step

# Run separately: creates crash_*.txt with a failed tick; relaunch normally afterward.
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=MOTION_LAB --dev-context-fail-inside-world-step

# Open reports.
explorer "$env:LOCALAPPDATA\PlatformerSandbox\Logs"
```

During normal gameplay press **Ctrl+Shift+F12**, then inspect `%LOCALAPPDATA%\PlatformerSandbox\Logs\Reports`. Close the game normally and verify there is no false unclean-exit warning on the next launch.

## Acceptance conditions

1. Full solution compiles, existing regression suite and new D01 tests pass.
2. Normal launch and clean exit do not generate crash or stall reports and remove the run marker.
3. Manual in-game export creates a reviewable ZIP while the session file is active, with bounded size/retention and local redaction.
4. Intentional fatal fault persists crash context and is reported as previous unclean exit on next launch, without interfering with crash reporting.
5. Intentional stall reports last committed world state, does not claim to be a fatal exception, and leaves gameplay running after the operation completes.
6. Existing movement-gym F12 behavior, simulation fault policies, and Phase 2 tick semantics are unchanged.
