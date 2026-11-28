# PB01-R4 — integration and acceptance candidate

## Baseline and scope

Apply over the manually accepted Landing Handoff and Horizontal Pinch Recovery patch. The original project archive plus the seven chronological PB01 fixes was used to reconstruct the baseline. This package contains full replacement files only, not a new solution. No physics response equation, character motor, crush policy, friction coefficient, or lighting quality setting is changed. The only solver changes count successful coupled event-loop passes; preview passes, rejected attempts and independent terrain-only solves are deliberately excluded. Whole-world step time includes support previews and retries.

The existing `PHYSICS_INTERACTION_LAB` remains the isolated feature fixture, with platforms 8001 (horizontal wall), 8002 (elevator), and 8003 (horizontal carrier). Existing visible body/material/collision/push/crush/playback/timeout controls remain the authoring and validation surface. The separate `ENGINE_INTEGRATION_LAB` retains terrain art, parallax, wind, checkpoint, lights, particles and existing character presentation; it adds elevator 8002 (x=52–56, starting bottom=23 world units; 4.5-unit downward travel in 2 s), upper carrier crate 13 and lower obstruction crate 14. Both use StopPlatform. The existing horizontal carrier and stack remain intact. No new runtime GPU dependency.

## R4 visual clarity follow-up

The integration lab now uses 25 opaque, axis-aligned textured terrain rectangles matching the 136 solid IntGrid cells exactly; exposed top edges are bright and decorative scenery is dimmed and non-shadow-casting. The independent `Physics Interaction Lab` is unchanged. Eleven world-fixed point lights (six authored shadow casters and five fill lights) cover the play route, with matching non-solid round markers. All terrain rectangles cast corresponding rectangular shadows. This is an art/lighting change only: collision grid, gameplay entities, dynamic bodies and physics are unchanged. There are no one-way platforms in this level. Moving platforms and props retain their existing visuals and lighting treatment; this patch does not introduce dynamic shadow casting for them. Regenerate the terrain assets after editing the collision grid with `py Tools\pb01_showcase_art.py` and check them using `py -m unittest discover -s Tools -p test_pb01.py`. The collision-alignment preflight checks asset bytes, coverage, nonoverlap, transforms, light placement and geometric radius coverage, not actual rendered illumination.

## Execution

Run from the solution root (Visual Studio closed or game not running):

```powershell
# Optional quick check (all C# tests, 3 spatial modes, no graphical captures)
py Tools\pb01_validate.py --tier quick --headless
# Full acceptance candidate: full suite in each spatial mode, visual and graphical benchmark
py Tools\pb01_validate.py --tier full
```

Keep the game window in focus during automated captures. Use AC power and the same machine/configuration for repeat measurements. For a separate headless run use `--tier full --headless`; it is not full graphical acceptance. `--list` prints the plan without executing. Existing quick/standard/full tiers now run 600/3000/10000 ticks per authored mixed-world replay, with a Reference oracle and the requested mode. The normal full run covers 60,000 compared ticks across six level/mode combinations, i.e. 120,000 world steps in those tests alone. It also performs the original regression suite, the dedicated elevator obstruction test, scaling (8, 32, 64 bodies), visual differential captures and 3 repeated integrated graphical measurements for each Reference/Indexed × uncapped/VSync configuration. `--repeat 1` is suitable for a smoke run, not the preferred performance baseline. No benchmark pass requires an arbitrary machine-specific FPS threshold.

## Evidence and gates

- `Benchmarks/PB01_validation_*.zip`: TRX, logs, mode-specific CSVs, visual/benchmark ZIPs, `summary.json`, `PB01_closeout_comparison.csv`.
- `*_closeout_replay.csv`: committed tick counts, world step mean/P95, thread allocations per tick, successful dynamic solver CPU time, sweep/contact counts, recovery attempts, event iterations, peak contact island, landing/jump/death/respawn events. Replay timing excludes the reference world and invariant assertions, includes cold start and excludes scripted command setup.
- `PB01_closeout_scaling.csv`: 8/32/64 body stacks over a shared floor; isolated physics measurements after separate-world JIT warmup. Pair contacts required. Timing excludes invariant checks and input setup.
- `PB01_closeout_comparison.csv`: same-workload mode comparison; Verify is diagnostic overhead, not a shipping performance preset.
- Graphical workload version **7** (collision-aligned terrain and expanded lighting) is a fresh baseline; v6 and older reports are not apples-to-apples. Re-run v7 on the same hardware for later comparisons. Successful coupled event iterations are not total computational work: previews and unsuccessful retries are represented only in whole-world timing. Counts are not FPS targets.
- Any failed regression, unhandled exception, overlap of enabled pairs, replay divergence, missing/malformed evidence, or visual/benchmark failure blocks automated acceptance. `automated_closeout_candidate` in summary.json is only a candidate, not manual approval. Inspect logs and report any unexpected recovery or performance regression. Strict mode is retained.

## Manual sign-off (both levels)

1. Physics Interaction Lab: free crates and multi-crate chains push and settle; ice slides farther than wood/rubber. Check all visible collision and material controls.
2. Horizontal wall: free crates push, a trapped StopPlatform crate survives and wall retreats; repeat opposite direction, player pinched on either side and jumping into gap. Destroy removes obstructing crates. HOLD/manual pause remain stationary; manual reverse works.
3. Vertical elevator: direct ride, crate beside player, player on crate, jump/land while descending and ascending, no repeated landing particles; crate under platform blocks it, retreat occurs, subsequent traversal resumes.
4. Horizontal carrier: ride and jump off the prop; no penetration or false crush.
5. Engine Integration Lab: travel toward x=52–56 (right of the existing horizontal carrier) to the new elevator. Observe lower crate stop/reverse, ride upper crate, move away and return. Check combined lights, shadows, particles, sound, animation, camera/parallax, checkpoint/wind and respawn, with CPU-compatible settings. Existing lighting/scene features remain separate validated baselines; this is interoperability, not new lighting functionality.
6. Complete full runner and inspect results. Record manual pass/fail and any remaining limitations; save report ZIP and commit validated baseline. PB02 rotation stays deferred; next planned major feature is gameplay interactions.

## Limitations

The new tests are acceptance candidates, not proof of correctness until executed on the target .NET 10/MonoGame environment. The solver is still translational AABB physics, with conservative character/prop coupling and predicted support endpoints; no rotational dynamics, general joint solver, or fully coupled character impulse response is introduced. Existing first-chance classified compression exceptions may appear in the debugger during normal transactional recovery. A new unhandled fault is a failure, not an acceptable recovery. No C# compilation, graphical capture, or measured performance result was obtained in the preparation environment.
