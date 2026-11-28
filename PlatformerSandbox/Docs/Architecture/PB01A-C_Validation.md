# Validation procedure

## Extract and build

Extract this overlay at the PlatformerSandbox solution root after PO05 candidate + compile/visual fixes and GPU refinement. It includes the latest crush recovery overlay. Rebuild solution. New SDK-style C# files are picked up automatically. Do not delete old levels. Run from root:

```
py .\Tools\test_pb01.py
py .\Tools\pb01_validate.py --tier quick
```

Quick runs all existing and new C# tests in Reference, Indexed and Verify, 600 replay ticks per lab per mode, a Release build, 3 dedicated-lab visual captures (reference, repeat, indexed), and 2 integrated graphical cases (uncapped reference and indexed). Archive `Benchmarks/PB01_validation_<timestamp>.zip` is emitted on failure too. Upload it. Keep game focused and hands off during captures, plug in AC power; do not change resolution/settings mid-capture. Native adapter guard does not require an RTX GPU; an integrated renderer is supported.

`--headless` skips graphics; `--no-bench` still runs visual comparisons; `--bench-only` runs the graphical benchmark independently when needed. `--tier full` expands replay to 3600 ticks, 3 visual tick positions and reference/indexed with and without VSync (3 repeats by default; override `--repeat`). `--list` prints actual planned commands. Output includes fixed-tick CSVs, test TRX, graphical archive, summary and logs. Graphical FPS is a baseline, not a pass/fail target. Compare identical device, AC status, renderer/driver, settings and scene version before drawing performance conclusions. Reference/Indexed rendering may cover different simulated durations due to frame-count sampling; use tick counts and workload metrics. No optimization is accepted based solely on FPS.

## Manual acceptance

- Physics Interaction Lab appears in menu, loads without rejected authored project, returns to title and restarts correctly.
- Player movement/jump unchanged; horizontal pusher and descending platform can crush, current PO05 respawn policy recovers without restarting world. Carrier transports player. Test Reference, Indexed, Verify.
- Visible spatial controls and body counters work. Character is counted as kinematic. No dynamic bodies are claimed. Preview Wood/Ice, switch both materials, check static/sliding friction and restitution; edit Materials.json then reload and verify preview. Invalid material file is reported without replacing character motion.
- Engine Integration Lab appears independently, loads, moving platform and wind/checkpoint function, visual layers, animation, particles and lighting remain visible, audio cues play. Use existing quality panel, verify pause/restart and camera. Record which features are actually exercised; do not infer coverage from presence in content alone.
- Regression suite includes bundled PO05 crush tests; if they fail, stop before new physics work. Confirm the previously reported manual crush crash is resolved before closing PO05.

## Authoring-side evidence

Seven PB01 Python tests and nine existing PO05/GPU01 Python tooling regression tests passed. The PB01 tests cover plan generation, matched configurations, original visual plan compatibility, material data, dedicated swept platform geometry, integration authored entity coverage and summary formatting. C# regression tests and actual game/graphics captures NOT run here because .NET SDK and DesktopGL execution are unavailable. Imported level acceptance and gameplay behavior remain pending local validation.
