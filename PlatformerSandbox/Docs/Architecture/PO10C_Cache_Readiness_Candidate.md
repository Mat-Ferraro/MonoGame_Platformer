# PO10C — Cache readiness and targeted fixture validation

## Scope
Incremental replacement files for PO10B Partial Candidate (including the earlier Fast Tiers update). No shader source changes. New functionality is opt-in through the existing automated capture environment and static-field option. Normal camera following and exact persistent shadow-mask equality are unchanged. Mask hash bucket pooling applies only when hashed visibility lookup is in use.

## Observations and hypotheses
The previous quick suite showed persistent mask hits starting late in captures. `SharedVisibilityKey` includes the exact `LightingViewport`; `Camera2D.Follow` interpolates toward the dead-zone boundary. Frozen light motion does not imply a frozen camera or player. This is a plausible cause, not a proven diagnosis. Receiver projections, blockers, resource resets and eviction also affect reuse. Compare `cache_follow` and `cache_locked`, and inspect warmup traces. Do not quantize cache keys or use approximate matches without proving raster equivalence. The locked camera is a benchmark-only intervention; the simulation still advances normally.

## Changes
- Adaptive readiness: minimum 60 frames (Quick), 12 consecutive ready observations, maximum 240 by default. Checks exact viewport equality, selected light geometry diagnostic fingerprint, resource invalidation serial and persistent shared-mask misses. Does not select by FPS. No-shadow scenes may have no masks. Dynamic light cases use fixed warmup. A maximum-reached capture is retained with warning and is not validated steady state. All recorded frames are retained. `steady_*` summaries are descriptive partitions based on counters, not alternative acceptance metrics.
- `warmup_trace.tsv`: frame, view changes, light geometry hash changes, reset/invalidation, persistent hits, misses, evictions, capacity fallbacks, static hits, ready, player-bounds changes and full-precision camera focus. Hash changes are diagnostic only. Player bounds sampled at the capture boundary can include intervening simulation updates; not a precise attribution of individual cache misses.
- Shadow-free static fields: preserve compatible completed fields across frames in existing bounded pool (maximum three surfaces, 16 MiB extra budget). Match exact viewport, layer, budget, sample count, ambient, art light scalars and ordered value-equal lights; restrict to point/tube lights without explicit routing lists and no shadow work on that layer. Keep exact per-frame compatibility rules. Reset on existing renderer invalidation and non-layered entry points. The cache is still experimental. General shadow-bearing static field caching is not implemented.
- Hash collision bucket pooling bounded to 96 lists; no weakened cache comparisons. Performance benefit unmeasured.
- Dedicated fixture emitters injected only in automated captures. Adaptive fixture contains five unshadowed tube emitters with projected widths 8/20/48/96/160 px at the existing 32 px/world-unit camera, expected sample counts 1/3/5/7/9 versus 9/9/9/9/9. Static fixture uses five shadow-free point lights with stable value-equal parameters. These replace selected authored lights for the fixture only, and intentionally bypass planner selection; not representative gameplay or a scalability test.
- Visual fixture mode captures each reference twice and compares candidate. Static case is strict and requires a recorded static hit; zero hits yields inconclusive. Adaptive is a quality change, requiring review. Existing visual suite unchanged.

## Commands
From solution root, after extracting:
```
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj -c Release
py .\Tools\po_run_benchmarks.py --po10c --tier quick
py .\Tools\po_visual_regression.py --fixtures --no-build
py .\Tools\po_export_source.py
```
Quick = 6 cases, 120 recorded frames per case. Standard/full add two moving-light comparisons. Optional `--fixed-warmup`, `--max-warmup-frames 300`, `--repeat 2`. Use `--list` for plan. Do not use `--no-build` until new C# code compiled. Upload performance ZIP, visual ZIP and reviewed source export. Keep window focused and input neutral.

## Validation and limitations
14 Python tests passed including synthetic warmup metadata/trace validation and fixture sample counts; four new headless C# tests included but not executed. No .NET SDK / MonoGame runtime or `.csproj`, `.fx`, `.mgcb` source in available export. C# compilation, actual cache hit behavior, visual parity and Windows performance are pending. The new diagnostics introduce small extra capture overhead; compare paired cases on the same build, not absolute timings against old runs. The late reuse cause has not been conclusively resolved. Regional mask texture allocation, fused shader passes, alternate GPU formats, differing-ambient field sharing, complete graphics menu and comprehensive scalability lab remain outstanding. Do not promote experimental static caching before visual validation.
