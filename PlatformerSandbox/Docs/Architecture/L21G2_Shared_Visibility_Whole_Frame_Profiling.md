# L21G2 — Shared visibility and whole-frame profiling

Incremental patch for the installed L21G Independent Optimization Lab + Clickable Performance Panel. No content or simulation changes in ordinary gameplay; new cache flags default off and are lab-only. Existing lab geometry unchanged.

## Why

Original user stress sweep (1280x720, High, frozen lights): baseline 154.29 ms, all five 129.79 ms, no lighting 23.07 ms (Update 0.47 ms, Draw 1.34 ms). Unattributed time in no-lighting test ~21.26 ms; pacing/present, driver/GPU or uninstrumented work, not a measurement of GPU time. Extended-emitter and shadow diagnostics gave the greatest savings. Existing hard-slot mask optimization gave no hits.

## New switches

- Shared soft masks (32): a bounded pool of full-size preserved RGBA8 masks replaces repeated identical generation within one frame. Cache final filtered masks for material receivers; hard layer masks can share these; uniform soft layers cache the horizontal intermediate and retain the original direct vertical multiply (no extra quantization). Contact-hardening layers retain reference path. Material contact-hardening masks can be cached after complete filtering. No extra sample reduction.
- Persistent GPU masks (64): identical mask inputs may reuse results across frames. Camera/projection/resolution, source position/radius/direction/shape/virtual depth, caster vertices and entry/exit routing, filter width/contact emitter radius and sample count all participate. Compare exact values; no hash-only match. Color and intensity are not visibility inputs. Cache keys copy geometry. Owner exclusion occurs before the key is built. Invalidation on device reset, target rebuild and renderer abort. At most 48 textures and 128 MiB nominal color data; report actual allocated targets and estimate. No in-frame eviction; overflow uses original scratch targets. Cache increases VRAM; check hit/miss/fallback counts. Not a compact packed atlas or new shadow algorithm.
- Existing 1/2/4/8/16 switches retained; all seven mask 127. Off by default; user must visually validate parity before enabling outside lab. No change to existing shaders.

## Performance lab

Open LIGHTING / PERFORMANCE LAB. At 720p select OPTIMIZATIONS / BENCHMARK page. Choose STRESS + HIGH, Full quality, motion frozen. Start FIXED / VSYNC to compare against old captures. All OFF / ON and individual toggles remain clickable. QUICK SWEEP = 17 cases; REPEATED SWEEP = 41 cases (two samples per individual/diagnostic, six references and six all-on brackets; 30 warmup + 120 recorded intervals each). Capture current = 120 warmup + 300 recorded. Capture auto-collapses panel; cancel on size/focus loss or setting changes via panel. Keep player stationary and window focused; frozen light motion is not a frozen simulation or fixed particle count. Record counts and repeat dynamic-motion sweeps separately. Reports use unique case IDs, run metadata, backwards-compatible parser, median of capture means, P95/P99 per capture, drift ranges, nearest case-order baseline interpolation. Original CSV files supported.

Optional FIXED / VSYNC button requests uncapped draw; switches off requested VSync and uses a fixed-tick accumulator in variable framework loop. Restore original fixed framework pacing when leaving lab. Existing target timestep retained; framework elapsed clamp still applies; display/driver may override VSync. Validate this mode separately (including scene transitions and no simulation acceleration). Record requested pacing in CSV. Use fixed/uncapped as separate runs.

Additional diagnostics: DEV HUD OFF (most developer text/tools; core performance UI stays); NO LIGHT/VFX (lighting and world VFX draw disabled, VFX update still runs); NO WORLD DRAW (world render skipped, simulation and HUD continue; camera/light prep can still run). Diagnostic settings are not quality-preserving optimizations.

New CSV fields: per-interval cumulative simulation/audio/particle/light/diagnostic/event update CPU times, camera+animation, inclusive scene draw, nested particle draw, light preparation, developer HUD time, main-thread allocations, process GC collection counts, graphics device draw/primitive/sprite/target counts, particle/effect counts, shared-mask hits/misses/fallbacks/pool and actual lighting target binds/filter pixels. Capture previous completed draw and following updates at start of next draw. CPU scopes are wall time, some nested; not GPU timestamps. Report residual interval is not GPU elapsed time. Metadata has build mode, attached debugger, GPU adapter, OS/runtime, target timestep and requested VSync.

## Acceptance

1. Build/test. Run Python preflights. Test default fixed pacing first; open reference scene, no visual differences with shared or persistent masks alone and together. Validate hard/soft shadows, dark/lit wall overlap, contact hardening, normal/rim, directional and area sources, particles/transparency. Move camera, rotate/move walls, vary source color and position, resize; no stale masks. Check high/low quality and toggle back off. Confirm pool under budget. Test a repeated frozen-light sweep and dynamic sweep.
2. Test uncapped option in no-lighting and lighting cases. Ensure movement speed, jump and simulation ticks unchanged; exit lab and ensure VSync/fixed framework restored. Compare same pacing groups only.
3. Check CSV row counts/metadata and run `py Tools\phase21_compare_performance.py`. Repeatability and actual performance are not yet measured. GPU captures using RenderDoc or a backend-specific timer query are still needed.

## Remaining architecture

Packed polar atlas/GPU extrusion, fewer material light passes for area emitters, lower-resolution visibility/illumination, spatial index and broader material batching remain separate backend changes. Existing shaders not present in original repository text export; do not pretend these changes are implemented. GPU timer query integration remains pending. Other systems measured before modification: fixed-step physics staged transactions and snapshots, particle batches, audio event dispatch, durable diagnostics, LDtk culling/batching and bitmap HUD. Preserve crash reporting, deterministic physics and artistic opacity; no blind feature removal.
