# GPU01 integrated candidate — clean HK02 baseline

This package supersedes both earlier GPU01 candidates. Apply the changed files to the full `PlatformerSandbox(1).zip` project baseline, not over an unverified intermediate build. All nine new switches default off, the existing graphics presets and deterministic CPU simulation remain unchanged, and compatibility mode disables all nine experiments. Standard sprite rasterization still uses a GPU, including integrated GPUs. The optional shader paths need local compilation even when disabled; no new GPU-compute or CUDA dependency is introduced. The Windows-only timestamp probe is off by default and is not an authoritative GPU busy-time metric.

## Implementation and constraints

| Experiment | Scope | Quality gate |
|---|---|---|
| Buffered shadow geometry | Reusable bounded dynamic buffer | Exact RGB |
| Resident shadow geometry | Exact projected vertex data cached across draws/frames | Exact RGB |
| Indexed shadow fans | Shared index prefix, validated exact triangle reconstruction | Exact RGB |
| Local hard-point masks | Full-resolution only, max 2048 square/32 MiB/48 entries, no current-frame eviction; camera-independent eligible geometry key | Pixel differences require review, especially subpixel motion |
| Specialized production materials | Ambient/emission and front-pair shader techniques; old workbench shader remains | Exact RGB for isolated test |
| Fused layer composition | One albedo/illumination pass; point or linear sampler according to resolution | Compare RGB; review rounding differences |
| Batched direct lights | Contiguous single-energy unshadowed point lights; original route for other lights | Compare RGB; review intermediate quantization differences |
| Fast bloom | Fused downsample/tent filter, optional quality experiment, not master-enabled | Visual review |
| Instanced particles | CPU particle state preserved; stable blend/source ordering, bounded 16384 per chunk, CPU fallback | Visual review |
| Analytic stress motion | Lab-only 1k/5k/10k deterministic seed-based vertex motion; CPU reference formula | Visual review; not general GPU simulation |

The master GPU experiment switch enables eight features and does not enable fast bloom or change the graphics preset. Local masks may change pixel sampling. For actual quality acceptance compare reference and candidate at identical preset/resolution. The existing `Enable All Optimizations` remains the established PO controls, not an implicit GPU opt-in.

## Validation

From the solution root, use PowerShell and Release, no debugger, no VSync or frame cap for timing; keep the game window focused, don't move controls during automated tests. Do not launch Nsight or RenderDoc during clean timings. Run:

```powershell
py .\Tools\gpu01_validate.py --tier quick
```

The runner executes C# tests/build and both GPU suites: 22 quick timing cases (10 shadow submission, 12 expanded) and 28 720p visual captures (12 shadow submission, 16 expanded). Each pair uses fixed quality/workload. Reference repeats are mandatory. Non-exercised requested paths and unexpected fallback are failures, not successful acceleration. For new quality-sensitive features, non-exact images are `REVIEW_REQUIRED`, and the runner exits nonzero; inspect images and magnified difference maps, not just aggregate metrics. Exact reference-repeat failure is inconclusive. No measured speedup is claimed by this package. Validation output includes individual run ZIPs if created, with raw CSV, metadata, image/difference reports and logs.

For isolation:

```powershell
py .\Tools\po_run_benchmarks.py --gpu01 --tier quick --no-build
py .\Tools\po_run_benchmarks.py --gpu01-expanded --tier quick --no-build
py .\Tools\po_visual_regression.py --gpu01-expanded --gpu-quick --no-build
```

`--tier standard` adds 5k particles and soft-shadow variants (45 performance cases overall); full adds 10k and 1080p cases (58 performance cases overall). Standard/full visual capture remains 720p, 56 captures overall. `--no-wide` can be used in the individual performance runner on a small display. Repeat timing at least twice after initial smoke checks. Compare frame intervals/P95, uploads, draw and target changes, candidate/fallback counters and memory. Use the same system, display, driver and power mode. Keep cross-device results separate. Source-level Python tests: `py -m unittest discover -s Tools -p 'test_*.py'`.

## Manual quality checks

In Lighting Quality Lab: compare compatibility, individual flags, and all GPU experiments under identical Ultra and Balanced graphics settings. Try frozen light/wall and animated versions; move horizontally and vertically across every section, including off-screen light entry and the HK02 pink light. Inspect front/back wall overlap, hard/soft shadows, direction/tube lights, transparency, normal maps, HDR/emission/bloom and particles. Change window size, switch levels and reset settings. Ensure no overlapping panels, diagnostic counters reflect actual execution and all-off restores reference. On integrated GPU, run Performance plus Compatibility and report baseline; unsupported optional paths should fall back instead of crashing. A device-level loss or shader compiler failure is not promised recoverable.

## GPU diagnostics

Optional Windows WGL timestamp query ring measures the GPU timestamp span from queued frame-start to frame-end commands (excluding Present), including possible idle periods; it does not block for a result. `-1` means unavailable, not zero time. Latest resolved result may be repeated across CSV rows, and sample/skipped counters identify coverage. Other backends and per-pass GPU timing use external tools. NVIDIA telemetry is device-wide, not process-exclusive.

```powershell
py .\Tools\po_run_benchmarks.py --gpu01-expanded --tier quick --gpu-timing --gpu-telemetry --no-build
```

Use Nsight Systems for CPU/GPU timing and submission stalls, Nsight Graphics for pass profiling where supported, RenderDoc for frame and texture inspection. Capture the same reference and accelerated scenes separately from clean benchmarks. Do not attach multiple profilers simultaneously. The game's default path must not depend on NVIDIA tooling.

## Backend experiment

Existing DesktopGL project and solution remain production defaults. Three separate projects share the original C# and code-first content builder: WindowsDX (DX11), WindowsDX12, and DesktopVK (Windows Vulkan runtime). They are intentionally not added to the solution, so an experimental restore/build failure cannot prevent DesktopGL from compiling. WindowsDX uses Windows Forms and net10.0-windows; DX12/Vulkan use native packages pinned to the existing MonoGame 3.8.6.1. Native shader profiles, packaging, runtime and any effect differences need testing. DesktopGL4 integration is still outstanding, because a matching package and content-builder target were not verified.

```powershell
py .\Tools\gpu01_backend_compare.py --build-only
py .\Tools\gpu01_backend_compare.py --backends DesktopGL WindowsDX WindowsDX12 DesktopVK
```

Backend runner records each build separately and does not proceed with a failed backend. Compare captured outputs across backend runs manually; per-backend A/B comparison is not proof of cross-backend equivalence. Do not replace the production project based on a successful build alone.

## Remaining work / scope

See `GPU01_Traceability.csv` for all 76 tasks with implementation status and evidence. Pending work includes general sprite/shadow instancing, atlas and single-channel masks, tiled lighting, GPU geometry generation, static/dynamic light separation, production light budget transitions, multiple render targets, general GPU particle lifecycle and compute, native per-pass timing, and hardware acceptance. These are deliberately not marked complete. The current renderer preserves compatibility by default, and experiments require the user to choose them. This environment does not have the .NET SDK or a graphics device, so C# compilation, HLSL compilation, runtime validation and actual timings have not been executed here. All 41 Python tests passed against the extracted project.
