# PO01–PO04 — initial implementation candidate

Based on the November 25, 2026 `PlatformerSandbox(2).txt` source export and Performance Optimization Roadmap rev 1.0. Copy the changed files over the corresponding locations in the solution root. No shaders, project files, binary assets, collision algorithms, or GPU pass topology are replaced. This is a candidate, not a phase acceptance record. The source-only export cannot be built here; all C# compilation, unit tests, visual parity and hardware timings remain pending.

## What is included

- **PO01:** Both VSync settings in the performance lab use the same 60 Hz simulation accumulator; VSync is independent of the render cap. Normal gameplay defaults to the existing framework fixed loop unless a custom cap, disabled VSync, or force-variable setting is requested. An approximate sleep-based render limiter and inactive cap are configurable. Ctrl+Shift+B starts/cancels a 120-frame warmup + 300-frame capture in gameplay; existing lab capture and sweep controls remain. CSV adds EndDraw CPU wall time (can include presentation/driver waiting, not GPU execution or physical display timestamps). Metadata captures settings and scene. `po_summarize.py` reports mean, P50/P95/P99/max, deadline exceedances, allocations and subsystem timings. Opt-in headless two-player benchmark added.
- **PO02:** Immutable committed snapshots and static/slot/platform views cached by appropriate lifecycle/tick. Step events remain distinct from Snapshot() events. A generated 5x7 glyph atlas retains the original font and fallback renderer. Performance overlay statistics refresh at 4 Hz; percentile calculations cache by sample revision. Artwork color and flip flags prepared during scene loading; empty material regions skip batch interruption. Off-screen material counters may decrease because rejection occurs earlier.
- **PO03:** Committed numeric state, attempted commands, and recent event detail stored as detached bounded typed payloads, formatted only when a report requests context. Existing string API remains. Crash and hang hooks, file flush/retention policy and simulation fault handling are unchanged. The ring is bounded; deferred payload objects and command arrays are still allocated, so this is not an allocation-free recorder.
- **PO04:** Hull validation caches successful exact vertex snapshots (not identity-only); mutations revalidate. Full validation available via `ValidateExhaustive()` or process switch. Light routing validation removes temporary array and LINQ. Shadow vertex arrays and directional list reused; exact triangle count submitted. Viewport validation hoisted for shadow batches, shader parameter handles/names cached. CPU surface/hero area samples prepared once per drawing frame when used; original shadow radius and scaled attenuation remain separate. Shared/persistent visibility keys reuse private defensive vertex copies while preserving exact equality. Existing seven lighting switches can be selected in normal gameplay using a production mask; default remains zero until parity and speed are measured.

## Process-lifetime switches

Set before launching the game. Restart after changing. `0` disables each optimization, default `1` unless noted. `Tools/po_profile.ps1` sets the main candidate or reference flags and identical variable-loop pacing for both. It does not recreate the original binary; independent changes such as updated telemetry and HUD refresh are still present.

| Variable | Meaning |
|---|---|
| PS_PO_SNAPSHOTS | cached committed and platform/slot/static views |
| PS_PO_HUD_ATLAS | generated glyph atlas |
| PS_PO_ARTWORK | cached artwork tint/flip and early empty material rejection |
| PS_PO_DIAGNOSTICS | deferred structured diagnostic records |
| PS_PO_LIGHT_VALIDATION | cached hull validation and allocation-light routing/occlusion |
| PS_PO_LIGHT_BUFFERS | reuse shadow vertex and parameter scratch buffers |
| PS_PO_LIGHT_PARAMETERS | cache shader parameter handles |
| PS_PO_LIGHT_SAMPLES | prepare CPU forward lighting samples once per frame |
| PS_PO_LIGHT_MASK | production cache mask 0–127; default 0; lab panel still overrides |
| PS_PO_VSYNC | initial VSync, default 1 |
| PS_PO_VARIABLE_LOOP | force same variable host loop with fixed simulation; default 0 outside lab |
| PS_PO_RENDER_CAP | approximate maximum draw rate, default 0 (no software cap) |
| PS_PO_INACTIVE_CAP | approximate inactive draw cap, default 30 |
| PS_PO_HUD | initial FPS overlay, default 1 (other developer HUD controlled separately) |

## Build and test (PowerShell in solution root)

```powershell
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj -c Release
dotnet build .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj -c Release
```

The existing project should include new .cs files through SDK default globbing. If the project explicitly lists files, add the new files to the corresponding projects. Do not copy any incomplete project reconstruction from the text bundle.

## First validation — VSync on vs off, same loop

```powershell
. .\Tools\po_profile.ps1 -Profile Reference -VSync On
 dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj -c Release --no-build -- --dev-level=LIGHTING_PERFORMANCE_LAB
```

1. Keep High quality, same resolution and preset, frozen lights, all seven lab optimization flags OFF, same HUD/particle settings. Click Capture or Ctrl+Shift+B. Repeat at least three times. Exit.
2. Repeat with `-Profile Reference -VSync Off`; repeat with `-VSync Off -RenderCap 60`. Both lab VSync settings use the fixed simulation accumulator. Record monitor refresh, adapter/driver and power mode manually.
3. Repeat with `-Profile Candidate` and the same VSync/cap. Change one PO flag at a time for attribution if the combined candidate differs. Repeat the lighting-off isolation in the lab, and capture an ordinary level or shared-world gym using Ctrl+Shift+B. Keep settings stationary throughout each capture. No real display/present GPU timestamps are available, and CPU timings overlap in some subsystems.
4. For actual comparison to the pre-change code, capture the original revision separately using its existing lab tools; the new Reference profile is a feature-toggle reference, not a bit-identical old binary. Do not compare different scene/quality/resolution/pacing configurations as if they were identical.
5. CSVs and metadata are saved under `%LOCALAPPDATA%\PlatformerSandbox\LightingBenchmarks`.

```powershell
py .\Tools\po_summarize.py 'C:\path\to\reference.csv' 'C:\path\to\candidate.csv'
```

Headless benchmark (run twice with different profiles; change output filename):

```powershell
$env:PS_PO_HEADLESS_CSV = "$PWD\po-headless-candidate.csv"
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj -c Release --filter FullyQualifiedName~PO01HeadlessBenchmarkTests
Remove-Item Env:PS_PO_HEADLESS_CSV
py .\Tools\po_summarize.py .\po-headless-candidate.csv
```

## Acceptance checklist

- Run full test suite in Reference and Candidate profiles; exercise moving platforms, jump, crush, respawn, joins and scene navigation; confirm fixed-step behavior and event order. New snapshot tests verify retained snapshots and invalidation.
- Check 5x7 HUD legibility at normal and small resolutions, VSync toggle/cap, focus loss, pause/menu transitions, and rapid keyboard/gamepad input. Confirm no Pulse activation from Ctrl+Shift+B.
- Run diagnostic failed-step, crash, watchdog, and retention tests; export report from both profiles and confirm equivalent last committed state, pending operation, and event history.
- Run lighting parity tests, especially moving/rotating blockers, concave hulls, embedded lights front/behind walls, glass, normal/rim materials, HDR, directional and tube lights. Compare Reference/Candidate screenshots. Change normal-game lighting mask only after independently validating each lab flag (1,2,4,8,16,32,64) and combinations.
- Measure warmed repeated Release captures without debugger. Record median of capture means, P95/P99, GC collection counts, bytes, draw/present wall time and geometry timings. Do not claim a win until demonstrated.

## Still open before PO01–PO04 closeout

- PO01: run baseline and VSync tests; actual GPU timestamps/display present feedback, exclusive nested timing, GC pause duration, interpolation and host-frequency input edge latching are not implemented. Cap is approximate. The existing per-frame instrumentation is not fully switchable; measure its cost separately.
- PO02: broader render-command preparation, static bounds/material classification and full debug-HUD scheduling remain; only the primary FPS statistics are throttled.
- PO03: dedicated allocation-free typed ring/pending storage, atomic combined publication of committed context + breadcrumb, ordinary log flush policy evaluation and actual injected-failure verification remain. Keep the existing durability policy until verified.
- PO04: hashed/revision-indexed lookup (existing exact linear scans remain), static/dynamic geometry separation, safe hull merging/internal-edge elimination, angular pruning, broader LINQ reduction, prepared blocker sets, particle CPU-sampling integration and validated production default flags remain. Preserve current exact geometry and quality until benchmark evidence supports further changes.
- PO05 and later not started. PO10 GPU pipeline redesign remains deferred.
