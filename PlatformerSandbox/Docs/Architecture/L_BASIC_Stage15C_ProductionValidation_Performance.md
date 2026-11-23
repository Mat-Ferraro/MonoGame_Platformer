# Stage 15C — Production lighting validation and CPU performance baseline

**Baseline:** working Stage 15B2 plus ShadowHullInsetPx alignment fix. This phase **does not alter lighting decisions, polygons, shaders, or the validation scene**. It exposes the costs of existing paths and defines a repeatable acceptance procedure before optimization.

## What the instrumentation measures

All timings are **CPU elapsed time spent submitting/constructing lighting passes**. They include CPU-side MonoGame driver-call overhead and may include GPU stalls. They are **not GPU execution time or total frame time**. No FPS/GPU-cost claim can be made solely from these counters.

- `CPU-MS LAST/AVG/P95/N`: last, arithmetic mean, nearest-rank 95th percentile and number of samples over a bounded 120-frame rolling window. One sample is recorded at the end of each layered lighting frame.
- `LAYER`: current-frame cumulative `ComposeLayer` CPU milliseconds.
- `MAT`: current-frame cumulative per-material pass CPU milliseconds (includes material mask work).
- `MAT-GEO` / `LEG-GEO`: CPU milliseconds computing `ShadowVisibility.BuildPolygon` for material masks vs legacy layer masks (not the GPU draws). `MAT-GEO` is a subset of MAT, and `LEG-GEO` is a subset of LAYER.
- `MAT-VIS`: number of production mask fans actually submitted; `LEG`: conventional layer visibility fans submitted. White bypass masks are not counted.
- `OCC`: sum of eligible *other* occluders considered for material mask submissions. **Not** a count of individual ray intersection tests.
- `RAYS`: sum of requested radial fan samples for submitted material masks. **Not** a count of GPU triangles or true rays against all blockers.
- `RT-MB`: theoretical 11 full-size RGBA8 targets only; excludes backbuffer, graphics-driver padding, and small textures.
- `RT-RESIZE`: total target allocation batches since renderer construction; this should remain constant at fixed resolution, rising only for actual size changes.
- `MAT-LIMIT`: count of eligible material lights not selected under the two-light-per-material contract (a visible **quality** limitation, not a GPU timing measure).

No new adaptive degradation is introduced. If performance is poor, data guides the next change instead of silently switching off wall/rim shadows.

## Repeatable manual matrix (DesktopGL, same viewport, same location)

Press **F11** for the full HUD. In `LIGHTING_INTEGRATION_TEST`, walk next to the green pillar so `MAT 2`, `MAT-BACK >= 1` and at least one material mask are visible. Keep window dimensions stable. The light and moving platform are animated by presentation time, so compare longer steady runs rather than single snapshots. After changing a setting, allow 120 complete rendered frames to refill the timing window.

| Run | Controls | What to record | Visual checks |
| --- | --- | --- | --- |
| A | `Ctrl+Alt+3` to **Low**; `Ctrl+Alt+5` **Off** | CPU AVG/P95, MAT-VIS, LEG, OCC, RAYS, RT-MB | Low quality deliberately disables projected shadows; face/rim layer rules remain meaningful |
| B | **Balanced**, stress **Off** | Same metrics | Normal material panel receives shadows; green wall self-excludes but other casters block its rim |
| C | **High**, stress **Off** | Same metrics | No new rim leaks, shadow fan silhouette stable |
| D | **Balanced**, `Ctrl+Alt+5` stress **On** | Same metrics and MAT-LIMIT | Authored lights should retain priority; extra candidates may be omitted under budgeting |
| E | **High**, stress **On** | Same metrics and MAT-LIMIT | Shadows and material selector remain stable; quality may change with two-light selection |
| F | Return **Balanced**, stress **Off** | CPU AVG/P95, MAT-VIS, RT-RESIZE | Baseline returns after 120 frames; RT-RESIZE unchanged |

The stress switch injects presentation-only candidate lights and **does not** modify the authoritative simulation. `Ctrl+Alt+3` cycles Low → Balanced → High. Record resolution, GPU driver, build configuration, and VSync for meaningful comparisons. The supplied suite cannot measure true GPU time without an external graphics profiler.

## Acceptance and regression

1. `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj` succeeds, including the new bounded-window tests.
2. No unexpected target rebuilds during ordinary camera movement or moving/rotating occluders.
3. `MAT-VIS` is zero when quality Low disables all shadow lights, and becomes positive when Balanced/High actually route casters to an opted-in material.
4. Block another wall's green rim and verify that it disappears; the back wall must never be face-lit by a lower-depth light.
5. Maintain correct, stable silhouettes in `LIGHTING_FLAT_TEST` and `LAB / LIGHTING BASICS`.
6. No mandatory performance pass/fail threshold is asserted until a machine-specific baseline exists; CPU timing variance is expected.

## Next optimization decisions (not part of 15C)

- If MAT-GEO dominates MAT CPU: cache static light/hull pairs or reduce repeated visibility geometry only where stable IDs/positions guarantee parity.
- If MAT CPU dominates but MAT-GEO is small: investigate full-frame surface-map passes, target switches, and batching.
- If material light overflow appears (`MAT-LIMIT > 0`): design multi-pass or clustered selection; **do not** quietly drop shadows to hide overflow.
- If GPU time dominates CPU timing: use a native GPU profiler before adjusting fan quality or adding soft shadows.
- Keep existing fault/crash diagnostics separate from per-frame performance HUD; avoid logging per-frame spam.
