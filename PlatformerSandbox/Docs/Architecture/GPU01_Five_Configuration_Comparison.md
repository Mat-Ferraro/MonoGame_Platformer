# GPU01 five-configuration comparison

Apply over GPU01_AH_Candidate (not the pre-GPU project). Only Python test tooling changes; existing Release binary is sufficient. No changes to renderer or settings.

From the solution root, with the game closed, laptop plugged in, Windows Release executable configured for the desired GPU, and other GPU-heavy applications closed:

```powershell
py .\Tools\po_run_benchmarks.py --gpu01-compare --tier standard --repeat 3 --warmup-frames 180 --recorded-frames 600 --expect-adapter "RTX 5070" --no-build
py .\Tools\po_visual_regression.py --gpu01-compare --expect-adapter "RTX 5070" --no-build
```

Optional separate instrumented run:

```powershell
py .\Tools\po_run_benchmarks.py --gpu01-compare --tier standard --warmup-frames 180 --recorded-frames 600 --expect-adapter "RTX 5070" --gpu-timing --gpu-telemetry --no-build
```

Use `--expect-adapter "Intel"` on the integrated GPU when appropriate, or omit the argument for unrestricted testing. The guard verifies the application's reported adapter and native OpenGL renderer when a timing probe provides one. It is not a GPU-selection mechanism. Device-wide telemetry can include unrelated GPU work. A timing query measures an asynchronous draw span, not per-pass GPU times.

All cases use the existing authored Engine Performance Lab, 1280x720 Ultra-equivalent settings, automatic samples, all established optimization flags, hard shadows, animation enabled, uncapped rendering, no GPU particle stress or fast bloom. This is not the widened manual Quality Lab. Animated benchmark runs use matching workload settings, not identical animation ticks per rendered frame. Visual tests use a fixed tick capture and repeat the reference. No extra UI input is needed; keep the game window focused, do not resize or move the player. Repetitions alternate forward and reverse order. Timing reports include median run FPS, P95/P99 frame time, CPU submission, draw calls, target changes, and individual activity counters. An optional feature may have no eligible work in this workload (e.g. light-local masks on dynamic geometry); such features are identified in gpu_comparison.csv and should still be tested separately with --gpu01-expanded. The existing suite and individual isolation tests are unchanged.

The output is under Benchmarks in timestamped ZIP files. Upload the clean performance ZIP, visual ZIP, and separately the diagnostic ZIP if collected. Visual REVIEW_REQUIRED (exit 2) means differences need inspection, not a crash; no automatic acceptance threshold was added. Missing GPU execution evidence or wrong adapter fails the relevant performance capture. Runtime on Windows is untested by the author; Python tests and plan generation are verified.
