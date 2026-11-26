# PO10D candidate — Camera convergence, manual Lighting Quality Lab, targeted validation

## Apply and validate

Extract the full replacement files in this ZIP into the solution root **after PO10C**. Build and run the regression suite in Release:

```
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj -c Release
py .\Tools\po_run_benchmarks.py --po10d --tier quick
```

The Quick suite builds the game, then records five fresh-process cases (120 frames per capture, adaptive readiness for the two stationary cases, fixed 60-frame warmup for continuous camera motion). It uses the existing Performance Lab, not the manual lab. `--no-build` is safe only after a successful game build of these exact sources. `--list` previews the matrix. `--repeat 2` is optional for noisy comparisons. The previous suites remain available.

```
py .\Tools\po_visual_regression.py --temporal --no-build
```

The temporal suite compares three independent positions along the deterministic camera sweep (simulation ticks 30, 60, 150). Each group repeats the full reference, compares the full-resolution optimized renderer (strict), and compares half resolution (quality review). There are 12 captures, not a continuous video or proof against all temporal artifacts. Existing --fixtures and --extended options remain available. Upload ZIPs from Benchmarks.

## Manual quality acceptance

Launch the ordinary game (Release recommended). Choose **Level Select -> TEST / LIGHTING QUALITY LAB**. This is a distinct SceneDestination alias for the existing validated LightingPerformanceLab authored LDtk level, so no new asset copies or content build rules are required. The original ENGINE PERFORMANCE LAB remains unchanged for automation. If the existing LDtk file is missing or rejected, the manual entry is hidden as well.

Starts in optimized 100% resolution, High quality, hard shadows, normal follow camera, animated lights and bloom. The scene has colored emitters, a 55%-opacity surface, normal/edge-mapped custom-hull rocks (one animated), and multiple area-emitter shapes. Walk and jump normally. The controls are shown in-game:

| Key | Action |
|---|---|
| 1 | PO10A reference: full-resolution intermediate targets, new PO10A/PO10B flags off |
| 2 | Full-resolution optimized (PO10A flags on, no quality reduction) |
| 3 / 4 / 5 | Optimized illumination and shadow targets at 75 / 50 / 25 percent |
| 6 | 50 percent + adaptive area samples + 50 percent bloom pyramid + eligible static fields |
| 7 | Freeze/resume lighting event animation (player remains controllable) |
| 8 | Hard / soft Gaussian shadow edges |
| 9 | Cycle authored showcase / adaptive emitter fixture / shadow-free static fixture |
| 0 | Hide/show lab panel |
| Shift+1..6 | Point / line / disc / rectangle / showcase / stress authored light selection |
| Shift+7 | Toggle deterministic presentation-only camera sweep |
| Shift+8 | Toggle bloom |
| Shift+9 | Toggle VSync (manual FPS inspection, fixed simulation accumulator retained) |

All six profiles hold the original seven optimization bits at 127 and preserve the High quality budget; this is a PO10A reference, not a historical mask-0 renderer. Other process-level PO01–PO04B settings are retained. The profile's illumination and mask scales are requested values; the panel shows actual target dimensions. The experimental adaptive policy still respects authored overrides. The live profiles clear relevant renderer caches and target pools at Update boundaries. The manual-lab process override is cleared at scene exit/host unload. Existing generic lighting tool settings follow their existing scene lifecycle; the lab is not a persistent production graphics settings menu.

Suggested acceptance: freeze lighting (7) and stand still, compare 1/2 (expected equivalent); compare 1/3/4/5 for edge quality. Unfreeze, walk and jump behind the blockers, use 8 to compare soft shadows and  Shift+7 to sweep the camera. Inspect alignment, shadow boundaries, temporal flicker, material transparency, normal-map and glow appearance. Use Shift+1..6 to isolate emitter shapes. Use 9 twice to exercise the adaptive/static fixtures and compare 4/6, then return to the normal scene. Confirm leaving the lab and entering Connected Cavern works normally. No quality preset is promoted by this package.

## Changes and limits

- Camera convergence snaps to the exact dead-zone boundary once the interpolated residual is <= 0.001 world units (~0.032 pixels at 32 px/unit). No world state or input is altered. The opt-out `PS_PO_10D_CAMERA_SNAP=0` restores the prior follow calculation for A/B. The normal smoothing trajectory is retained until the threshold is reached. Tests cover stability, resumed follow, legacy opt-out and camera path bounds.
- Existing persistent geometry fan cache is already in world coordinates; no camera appears in its key. This patch removes lookup iterator allocations and pools hash buckets with a 128-list cap; exact blocker and light geometry matches remain mandatory. Tests exercise view change reuse, eviction/recycling and invalidation. Screen-space GPU masks still depend on exact viewport coordinates. No approximate mask-key reuse.
- The camera path is driven by committed world ticks and clamped to level bounds. The authored performance room is 1280x1280, so at 720p it pans vertically, not horizontally. Automated pan captures explicitly verify view movement. Timing runs with pan do not wait for static readiness.
- Remaining shader work (regional light-relative mask textures, fused passes, format probes, different-ambient field sharing), generalized shadow-casting cross-frame field caching, full persistent graphics settings and broad scalability lab are deferred. Source export still lacks .fx, .csproj and .mgcb files. Run `py .\Tools\po_export_source.py` and review the resulting ZIP before sharing.
- Local checks: all 16 Python runner tests passed; matrix generation checked; C# tests added but compilation and live rendering are not available in this environment. No FPS or visual parity claim is made for PO10D.
