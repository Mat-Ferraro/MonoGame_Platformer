# Phase 21G — Independent optimization experiments

## Baseline and installation

Extract into PlatformerSandbox after Phase 21 C–H Combined Lighting. This supersedes the previously proposed FPS Profiler Optimization ZIP; that earlier ZIP is not needed. All five experimental optimizations are disabled in ordinary levels. No shader, simulation timestep, VSync setting, physics, material definitions or existing visual lab geometry is replaced. The dedicated performance scene copies the Phase 21 visual lab with unique instance identifiers and its own scene preset registration. The initial case is combined showcase with light motion frozen and optimization mask zero. The player can still move.

Build and test locally; no .NET runtime or GPU was available in the package generation environment.

## Controls in LIGHTING / PERFORMANCE LAB

- 1 point, 2 line, 3 disc, 4 rectangle, 5 showcase (default), 6 stress. 7 hulls; 8 quality; 9 capture current case (120 warmup / 300 samples).
- Shift+1: frame-local visibility polygon reuse (bit 1).
- Shift+2: reuse latest compatible layer illumination field (bit 2).
- Shift+3: prune out-of-range point samples in ordinary materials (bit 4). Receiver projections and owner walls excluded; weights unchanged.
- Shift+4: bounded persistent radial geometry cache (bit 8). Exact source position/radius, radial resolution, ordered blocker vertices and entry/exit rule must match. Defensive vertex copies. Ignores color and intensity; no camera dependence. Previous-frame hits only. Bounded 128 entries; no GPU static light baking. Mixed moving casters can reduce hits.
- Shift+5: reuse exact hard material visibility mask in an existing slot (bit 16). Same view, effective source, blocker list, projection and entry/exit policy. Hard masks only. No extra GPU targets; not a complete shadow atlas. Most benefit if successive receivers use matching slots.
- Shift+0: toggle all five on/off; restore full visual quality diagnostic.
- 0: cycle full visual quality, lighting off, shadows off, bloom off, world particle drawing off, one area sample. These are diagnostics and may intentionally alter visuals. The one-sample diagnostic affects GPU area paths, not every CPU approximation.
- Shift+7: toggle freeze of the lighting presenter's clock (includes authored light/occluder motion and transient flash lifetime). Default on for static comparison; turn off for dynamic comparison. Not a simulation pause; avoid triggering gameplay light flashes or changing player position while sampling.
- Shift+9: automatic 12-case sweep (30 warmup + 120 frames each): all off, five single-feature cases, all on, and five feature-off/quality diagnostics with all optimizations on. Restores all-on full visual quality. Repeat under mode 2, 5, 6 and with light motion enabled. Do not move player, resize window, open tools/menu or change settings while running. Modes 1–6 and manual toggles cancel ongoing capture. CTRL+SHIFT+F toggles global FPS overlay.

The global FPS display uses elapsed wall-clock intervals between draw starts, not simulation tick rate. It displays rolling mean frame time, P95, updates and CPU draw wall time. Fixed-step scheduling and VSync still cap observed FPS. Benchmark reports CPU submission costs and counts, not GPU timer queries. Frame intervals and previous draw duration refer to preceding frames; renderer counters refer to current draw, comparable in steady-state after warmup. Automatic sweeps do not freeze gameplay or all particle dynamics; repeat for noise estimates. Use Release without debugger. Keep workload fixed and use same machine/settings. Capture GPU timings separately using a suitable external graphics profiler.

Reports: `%LOCALAPPDATA%\PlatformerSandbox\LightingBenchmarks`. Run `py Tools\phase21_compare_performance.py` (latest complete reference/all-on run) or supply folder and `--all-runs`. Groups by run ID, mode, quality, resolution and frozen-light state. Independent optimization deltas use all-off baseline; diagnostic deltas use all-on. No additive speedup assumptions.

## Validation and scope

Run `py Tools\phase21_optimization_preflight.py` plus previous closeout/spatial/emission preflights. New C# tests cover persistent geometry identity, depth/motion invalidation, prior-frame-only behavior and fixture parity. Existing menu test updated. C# compilation, GPU visual equivalence and actual speedup remain unverified. If any switch causes artifacts, turn it off and report which toggle. No performance gains are claimed until measured.

Deferred architecture experiments: GPU edge extrusion / penumbra geometry; polar shadow atlas (including wall depth and self exclusion semantics); full GPU mask sharing across arbitrary receiver groups; persistent static/dynamic shadow-texture separation; spatial indexes; multiresolution lighting and edge-aware upsampling; GPU timing; alternative area-light algorithms; material batching; Radiance Cascades GI. The optimization lab is a controlled baseline for implementing these later, not an implementation of them. Low quality and diagnostic area sample reduction are not quality-preserving optimizations.
