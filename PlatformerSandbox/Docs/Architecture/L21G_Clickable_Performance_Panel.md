# L21G — Clickable performance panel

Apply over the independent optimization lab package, not the earlier profiler ZIP. Only Game1.cs changes at runtime. Renderer, light/occluder data, five optimization bits, CSV schema and sweep sequence are unchanged.

## Controls

Open LIGHTING / PERFORMANCE LAB. The panel is expanded on entry. Click its header to collapse or expand; the separate FPS HUD stays in the bottom right (unless explicitly hidden using the existing global FPS shortcut). The compact header shows workload, quality, diagnostic mode, optimization count and individual abbreviated ON/OFF states. The expanded panel includes all six workloads, three qualities, six diagnostic modes, five independent optimization toggles, All On/Off, motion freeze, hull overlay, current capture, and automated sweep. Selected controls have a teal accent; diagnostic quality-reducing selections are amber. Optimization buttons explicitly include ON/OFF. Counters reflect last-frame work, not merely whether a feature is enabled. At shorter window heights the controls are split into two clickable pages.

Mouse and keyboard invoke the same methods. Clicking the panel consumes the click before DEV TOOLS or scene navigation; no panel input is accepted over menus or scene transitions. Workload changes preserve selected quality in the performance lab but reset diagnostic mode as before. All On/Off changes only the five optimization flags (does not reset diagnostics).

## Capture and sweep

Capture Current retains the original 120 warmup + 300 measured frames. Run Benchmark Sweep retains twelve cases, each with 30 warmup + 120 measured frames. Both automatically collapse the performance panel, preserving a small status display and progress bar. Cancel stops the current incomplete capture; completed CSV files remain. Expanding the panel during recording or changing measured settings cancels the incomplete capture, preventing mixed configurations. Keyboard shortcuts remain supported. The sweep still finishes with all optimizations enabled and the full visual quality diagnostic. For comparable results keep player position, viewport, motion freeze and other settings fixed; test in Release outside debugger. UI draw overhead remains included in wall-clock frame timing.

Reports: `%LOCALAPPDATA%\PlatformerSandbox\LightingBenchmarks`. Existing `py Tools\phase21_compare_performance.py` works unchanged.

## Acceptance

- Enter the performance lab, verify all five toggles OFF, motion frozen, Showcase/High/Full Quality.
- Collapse and expand; FPS remains visible, no settings change, no gameplay pause.
- Click each workload, quality and diagnostic mode; exactly one in each group is selected.
- Click every optimization separately, verify displayed state and counters; All On and All Off change all five without changing diagnostic mode.
- Verify keyboard shortcuts update buttons and vice versa.
- Toggle motion freeze and geometry overlay; test clicking while DEV TOOLS is open, and after resize. At short height select both pages.
- Start current capture and sweep by mouse; verify warmup/recording display, progress, cancel, and output CSV. During capture expand panel to verify cancellation; run another complete sweep.
- Verify previous Phase 21 visual lab and other scenes are unchanged except shared capture actions.

## Verification limits

The Python preflight checks source integration, not C# compilation, live mouse hitboxes, actual FPS or graphics output. Those require local MonoGame testing. Normal 1280x720 layout is the intended test viewport; very small windows can truncate labels. No external UI dependencies or new textures.
