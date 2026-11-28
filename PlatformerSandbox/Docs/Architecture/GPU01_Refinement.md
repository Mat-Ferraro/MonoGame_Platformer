# GPU01 refinement 01 — implementation candidate

Apply over the GPU01 A–H candidate and Automated Comparison patch (source baseline: PlatformerSandbox(4).txt). Includes complete replacement source files only. Existing shaders, content and experimental project files are required, unchanged. No mandatory advanced GPU path; compatibility is preserved. Back up or commit first.

## Validation order (Windows PowerShell at solution root)

Plug in AC and set the Windows GPU preference for the Release executable. Close other game instances and profilers; keep focus and window size unchanged. Build and test with:

```
py .\Tools\gpu01_validate.py --refine --tier quick --expect-adapter "RTX 5070"
```

13 performance configurations, 32 visual captures; includes C# tests and Release build. Reference repeat is required. Local-mask GPU visual captures render 14 times at the same simulation tick (12 consecutive observations for admission, then reuse). Capture only final image; no continuous playback comparison. On success or review/failure, upload GPU01_validation ZIP. Then:

```
py .\Tools\gpu01_validate.py --refine --tier full --repeat 2 --expect-adapter "RTX 5070"
```

39 performance configurations per repetition; same visual set. Optional separate instrumented quick run (do not mix FPS with clean runs):

```
py .\Tools\po_run_benchmarks.py --gpu01-refine --tier quick --gpu-timing --gpu-telemetry --expect-adapter "RTX 5070" --no-build
```

For low-end laptop, substitute its verified renderer name, or omit `--expect-adapter` (Windows DesktopGL native verification still required). Controlled Windows performance benchmarks require AC by default; `--allow-battery` deliberately opts into a separate power-limited baseline. Power source changes invalidate captures; active power plan is recorded before each launch, not enforced. Telemetry is device-wide. Windows launches exact Release apphost; other OS uses dotnet run, and WGL native check unavailable. Never change Windows power settings or GPU preference automatically.

## R01–R15

| ID | Delivered / status |
|---|---|
| R01 | Cached native WGL renderer identity independent of timing, adapter guard, executable/PID metadata and Windows launch matching. Other backends still use reported adapter; native verification outstanding there. |
| R02 | Windows AC guard, periodic and final power source checks, power-plan capture, intentional battery override. |
| R03 | Both authored labs, five matched variants; normal Quality Lab preserves scene light coverage. Different scene loads are reported separately. |
| R04 | Existing established master toggle, separate recommended shader candidate, all GPU experiments, compatibility. Recommended is not a production acceptance label. |
| R05 | Dedicated GPU pages in the same lab UI, visible return/navigation and counters. |
| R06 | 12 consecutive render-frame stability observations before allocating a local mask, bounded history and cache, exact key on reuse. Dynamic lights may still be eligible if geometry stays unchanged long enough; heuristic must be benchmarked. |
| R07 | Direct compact fan generation in both fan producers, lazy expansion for compatibility/fallback, shared indices. |
| R08 | Opt-in shadow upload/preparation and draw/bind wall-time split; these can include driver waits. External Nsight investigation pending. |
| R09 | Scoped redundant bind bypasses only where target ownership is known, counter and ablation. |
| R10 | Skip full material target clear before opaque ambient overwrite of the clipped region; final read only that region. Counter and strict visual check. Clear counter baseline changes as this previously uncounted clear is now tracked. |
| R11 | Isolated rendering workloads of 5k, 10k, 25k, 50k particles (CPU, instanced, analytic). Full tier includes all; standard 5k/10k. |
| R12 | Matched DesktopGL / WindowsDX benchmark runner; build and actual performance/visual acceptance pending. |
| R13 | Existing WindowsDX12 / DesktopVK projects remain isolated. Build/run feasibility pending; DesktopGL4 not implemented. |
| R14 | Explicit recommended shader candidate works independent of quality preset, compatibility remains available/default for fresh settings. No automatic vendor-specific strategy until both hardware classes validated. |
| R15 | Strict and one-channel-value tolerance classification (shader combination only), fixed-tick dynamic and static camera comparisons, world-fixed light position check, reference repeat, new target/clear tests. Continuous temporal shimmer and quality-sensitive differences still require manual review. |

## Backend experiments

```
py .\Tools\gpu01_backend_compare.py --build-only
py .\Tools\gpu01_backend_compare.py --backends DesktopGL WindowsDX --expect-adapter "RTX 5070"
py .\Tools\gpu01_backend_compare.py --backends WindowsDX12 DesktopVK --expect-adapter "RTX 5070"
```

Do not promote backends from successful compilation alone. The runner captures within-backend visual comparisons; compare reference images between backends manually. Existing project definitions are intentionally unchanged. DesktopGL remains production baseline.

## Manual acceptance

In both lighting labs use the GPU controls page. Compare compatibility, recommended shader candidate, all experiments and individually toggled new features. Turn off the new clear/bind optimizations if any visual difference occurs. Freeze lights/walls and move camera horizontally/vertically, check stable shadows; then unfreeze and repeat. Check transparency, normal maps, emission and bloom; resize, reload level, repeat. Test 50k particle stress and compatibility path on both hardware classes. No optimization accepted solely because requested flag is on. A zero skipped-bind/clear counter means inconclusive, not a measured improvement. Fast bloom remains a separate quality experiment. Existing graphics presets unchanged.

Local validation: Python unit tests and plan generation only. C# and graphics compilation/runtime not available in preparation environment. Driver bottlenecks, GPU timings, FPS, visual equivalence and all alternate backends need Windows testing. No speedup claimed.
