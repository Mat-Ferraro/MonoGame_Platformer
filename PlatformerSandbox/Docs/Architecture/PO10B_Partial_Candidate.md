# PO10B first implementation candidate (partial)

Base: PO10A candidate + Benchmark Fast Tiers update. New experiments opt-in. This is not the full PO10B milestone.

## Implemented
- Separate visual capture mode (`PS_PO_VISUAL_OUTPUT`, used by `Tools/po_visual_regression.py`). Runs the Performance Lab with fixed update increments, waits for ready scene, runs 60 update ticks, then renders two frames at zero presentation delta; saves RGB comparisons before developer HUD/UI. Backbuffer readback is never included in performance runs. Exact reference-repeat control; compare world tick, player bounds, camera, scene, lights, adapter. Pixel diff images amplified 8x. Quality changes always require human review. No claim of deterministic live execution until verified on Windows. Do not touch input during capture. No automated material/Light Types/Connected Cavern/camera motion cases yet.
- Adaptive area sample policy, 1/3/5/7/9, projected emitter size at thresholds 12/32/64/128 logical output pixels, 15% hysteresis, quality budget cap. Explicit authored overrides and existing global override have precedence; same selected light list used for CPU and GPU. No adaptive sampling by default.
- Existing three-level bloom pyramid (already present in PO10A) can now start at 100/75/50/25% of its *original half-size base*, retaining shader passes and HDR/SDR route. A 50% setting starts at quarter-output dimensions. Smaller bloom texture changes filter footprint/threshold sampling; quality review required. No changes to shader source.
- Experimental cross-frame reuse of the most recent layer field, only if it is shadow-free, point/tube, no mutable explicit routing lists, camera/ambient/quality/layer/light sequence and field scalar art inputs unchanged. No new texture budget. Field cleared on existing invalidation/target rebuild/abort; does not cache shadowed static illumination or separate static/dynamic contributions. It may yield zero hits in the current lab.
- PO10B performance suite: 6 quick / 12 standard / 17 full cases. Original suite behavior preserved. Uses existing mode 6 stress fixture (120 offered debug lights with existing High budget of 12 visible/5 shadow logical lights); not a 50-active-light/entity stress lab. Added actual bloom pixel and static field hit counters and metadata checks. Existing CSV columns kept and two appended.
- PowerShell profile supports BloomScale, AdaptiveSamples, StaticFields; settings not yet persisted in-game. Source export tool includes missing shader/project/MGCB/build files if present locally.

## Not implemented / blockers
The earlier text export explicitly included only .cs/.json/.ldtk/.md/.py/.txt. There are zero actual .fx, .csproj or .mgcb files in the reconstructed tree, despite the source header reporting four discovered FX and seven projects. No dotnet SDK in the analysis environment. Do not guess shader interface or project configuration. Deferred: cropped regional texture allocation and UV transforms, single-channel RT capability and blend tests, fused passes, different-ambient shared direct fields, general shadowed static cache/dynamic mask policy, dynamic allocation profiling, persistent in-game graphics menu, full multi-scene scalability fixture, and remaining visual validation. Do not promote production defaults until tested. No major graphics rewrite.

## Commands from solution root
```
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj -c Release
py .\Tools\po_visual_regression.py
py .\Tools\po_run_benchmarks.py --po10b --tier quick --no-build
py .\Tools\po_export_source.py
```
Visual runner builds Release by default; use --no-build only with already updated binary. Extended visual suite: --extended (hard/soft/moving; 13 captures). Full performance run optional --po10b --repeat 3 (51 cases); prefer quick. The visual runner returns nonzero for inconclusive reference controls, exact mismatches, or execution errors but still archives available evidence. Reduced-resolution comparisons labeled REVIEW_REQUIRED; visual captures from first validated reference must be inspected manually.

## Validation
Python unit tests, CLI plan, source export and archive verification performed locally. C# compile, regression tests and live graphics capture not performed here. Candidate must compile and run on user's system. The shader/project source export is the prerequisite for implementing the remaining shader work and verifying build configuration.
