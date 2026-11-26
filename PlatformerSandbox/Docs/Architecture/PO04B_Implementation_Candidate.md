# PO04B – Targeted Lighting and Rendering Optimization

Candidate based on the November 25 source export + PO01–PO04 Candidate + Automated Benchmarks ZIP, in that order. Install only after both preceding packages. Changed/new files only. No shaders, project files, content fixtures or collision simulation changed.

## Stage 1 – Light preparation
- Global sample count: PS_PO_B_AREA_SAMPLES = 0 (existing automatic policy), 1/3/5/7/9. The current quality presets are Low, Balanced, High, not Ultra. At 0 the existing CPU 9-sample behavior and GPU quality-based policy are retained. An explicit override is applied to CPU and production GPU paths; single-sample isolation takes precedence.
- Optional authored LDtk LightSource numeric field AreaSampleCount = 0/1/3/5/7/9 (0 automatic). Add the field in the editor if desired; existing LDtk files unchanged. Explicit global override takes precedence.
- Bounded shared area light cache (256 source/count entries; clears on capacity or renderer abort). Full source record equality preserves intensity, routing, color and motion. Weighted sample energy conserved; lower sample counts change appearance, not a free optimization.
- CPU receiver sampling uses the same prepared cache where applicable and conservative segment bounds from validated hull snapshots. Reference sampler accepts the same count. No new shaders.

## Stage 2 – Caches and allocations
- Validated hull snapshot identity is the revision token, rather than a public monotonic version counter; the original mutable vertices are still compared against the private validated copy. Weakly keyed fingerprint/bounds metadata. Content hash indexes persistent geometry and shared GPU masks; exact input comparison on collision. Geometry cache LRU limit 128; GPU pool stays at 128 MiB and 48 targets; no VRAM growth policy change. Existing frame cache unchanged.
- Shared mask free-slot selection uses bounded loops rather than LINQ sort. Distinguish capacity fallback and persistent-only same-frame fallback, record both in CSV; a fallback is not automatically a defect. No attempt to increase the pool or evict a mask still referenced during a frame.
- Prepared area samples and static material records reduce repeated allocations. Per-material candidate selection and temporary render callback allocations still exist. Further allocation profiling is needed; 100 KiB/frame is not demonstrated.

## Stage 3 – Rendering and UI
- Conservative viewport culling skips empty non-gameplay, non-backdrop layers; retains existing gameplay/backdrop and particle ordering. Shadow casters are independent and not removed by artwork visibility. Static material records cached by authored instance, immutable options and center; dynamic callbacks stay frame-local.
- Production lighting profile Reference (mask 0), OptimizedCandidate (127), Custom (explicit mask). Keep production default 0 pending dynamic visual parity. PO B process switches default on and can be disabled individually.
- Primary menu: Connected Cavern, Engine Performance Lab, Movement Gym, Material Showcase, Light Types Showcase, Legacy Lighting Labs submenu. Older level IDs unchanged and still directly launchable. Menu tests adjusted for new intended navigation.

## Stage 4 – Validation
1. Run `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj -c Release`.
2. Run `dotnet build .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj -c Release`.
3. Run `py .\Tools\po_run_benchmarks.py --po04b --smoke` (one candidate capture).
4. Run `py .\Tools\po_run_benchmarks.py --po04b --repeat 3` (16 cases x 3). Use `--no-wide` if 1920x1080 cannot fit the display. Runner collects 300 measured frames/case, 120 warm-up frames, alternating case order. Upload the generated Benchmarks ZIP.
5. Visual A/B: dot-source `Tools/po_profile.ps1 -Profile Reference -LightingMask 0 -AreaSamples 9` then launch. Compare same pose/quality with `-Profile Candidate -LightingProfile OptimizedCandidate -AreaSamples 9` in a fresh process. Inspect Performance Lab with motion both frozen and running, Material Showcase (glass/ghost opacity, emissive, receiver orientation, front/back walls), Light Types Showcase (directional, spots, extended lights), Connected Cavern (move camera, cross rooms, dash/flash, moving/rotating blockers), Movement Gym. Verify legacy submenu and back navigation using keyboard/mouse/controller. Verify no missing artwork at viewport edges, no stale shadows, no changes at equivalent quality settings. Lower sample counts: compare separately, choose only with user approval.
6. Benchmark summary: compare `candidate_uncapped` vs `po04b_all_off` and the four single-switch ablations, then sample curve and motion cached/reference, quality and resolution. Compare 9-sample baseline to historical 24.33ms and 371KiB only under identical settings; repeated runs required. Do not infer GPU execution times from submission/present wall time.

## Switches
PS_PO_B_SHARED_SAMPLES, PS_PO_B_HASHED_VISIBILITY, PS_PO_B_VISIBLE_LAYERS, PS_PO_B_PREPARED_MATERIALS: 0 disables, default 1. PS_PO_B_AREA_SAMPLES: 0 automatic. PS_PO_B_LIGHT_PROFILE: Reference default or OptimizedCandidate. PS_PO_LIGHT_MASK overrides profile. Automated runner sets all settings explicitly and checks capture metadata. The original --isolations suite remains available.

## Remaining work / limitations
- C# build/test and GPU visual validation require the user's Windows environment. No .NET SDK or project files in the available source export. Python plan/parser replay checked locally, not runtime.
- Camera movement and Connected Cavern benchmarking remain manual; automated suite currently uses fixed camera in Performance Lab, static/dynamic lighting and 720p/1080p. No automated image comparison or direct GPU timestamps.
- Static material cache does not cache frame-dependent closures or GPU texture uploads. No full allocation profiler trace. No claim of 60 FPS or allocation target achieved.
- Warning review in PO04B_Compiler_Warnings.md. No project setting changed without the actual project file. New warning count unknown.
- GPU architecture, reduced resolution render targets, shadow mask combination, and spatial indexing deferred. Production mask 127 is explicitly a candidate, not validated default.
