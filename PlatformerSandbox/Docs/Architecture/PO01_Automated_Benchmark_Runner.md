# PO01–PO04 automated benchmark runner

Apply after the PO01–PO04 Candidate ZIP. This package replaces only Source/Game1.cs from that candidate and adds a Python runner and this note. No rendering optimization or level menu changes are included. If you have edited Game1.cs since the candidate, merge the benchmark-only diff instead of overwriting your edits.

## Run

From the solution root in PowerShell, close any already-running game and Visual Studio debugger:

```powershell
py .\Tools\po_run_benchmarks.py
```

The script builds Release once, boots the Performance Lab directly, runs each case in a fresh process, waits for completion and writes the final results ZIP under `Benchmarks`. No clicks or key presses are required. Keep the game in the foreground, keep keyboard/controller neutral, do not resize, and avoid other GPU-intensive applications. The runner stops on the first failed case and packages partial data. For a two-case startup check use `--smoke`; for two passes (second in reverse order) use `--repeat 2`. Optional `--isolations` adds seven workload diagnostics; `--single-on` adds each original flag enabled in isolation. `--list` prints the matrix without building. Ctrl+C stops the run; the active game process is terminated and completed files retained.

## Default matrix (24 cases)

- Six pacing cases: reference and candidate each at VSync on, VSync off uncapped, and VSync off with a 60 FPS software cap. All seven original lighting flags on for each of these, so the eight PO switches are isolated as a group.
- Eight ablations: each new process-lifetime PO switch off individually, everything else on, uncapped and VSync off.
- Seven ablations: each original lighting flag off individually with all other flags on, same pacing.
- Original lighting flags all off with the new PO flags on.
- Lighting-off diagnostic with VSync on and off, all optimization flags on. This is a different workload, not an equivalent-quality optimization.

Each case uses 1280 x 720, Showcase mode 5, High quality, frozen light motion, collapsed performance panel, FPS HUD enabled, 120 warm-up intervals and 300 recorded draw intervals. A fresh process and normal level boot reset world state. Motion freeze is the existing light-motion control; particles and simulation still advance. Frame-based sampling means the elapsed simulation duration can differ; repeated runs and headless/world-specific benchmarks are needed for stronger attribution. Leave controllers neutral. Focus/resize and certain settings changes cancel captures; the runner rejects missing, incomplete or inconsistent CSVs and metadata. The runner verifies resolution, scene, preset, quality, masks, pacing, process flags and frame indices. It does not verify physical display timing or pixel parity. The actual game must receive foreground focus on each launch; if not, the runner times out or stops with a partial ZIP rather than mixing inactive measurements. The default per-case timeout is 240 seconds, configurable via `--timeout`.

## Output

`plan.json`, `summary.csv`, `ablation_summary.csv`, individual raw capture CSVs, metadata, build and process logs. The summary's positive delta against `candidate_uncapped` for a `without_*` case means disabling that switch was slower (the optimization helped in the all-on configuration). Negative delta means it was faster; repeat before drawing conclusions. Flags can interact: a zero marginal effect does not prove an optimization is useless by itself. The reference is the candidate executable with eight switches disabled, not the original executable. CPU submission/EndDraw timings may include driver waits, and draw intervals are not true display timestamps. Frame allocation includes capture/instrumentation overhead. A stationary lighting lab is not sufficient to assess all optimizations (notably artwork, multi-player snapshots, moving occluders and gameplay diagnostics under load); follow with dedicated scenarios.

## Verification status

Python syntax, matrix generation, synthetic CSV/metadata validation and archive creation can be checked without MonoGame. C# compile and Windows runtime validation are pending; no claims of measured performance or acceptance are made.
