# PB01 R2/R3 platform–prop interaction candidate

## Baseline and install

Apply to the PB01 character–prop candidate plus Compile Fix 01 and Pair Response Fix 02. Full replacement files; extract into solution root. The existing analytical character motor is not modified.

## Implemented scope (candidate, not runtime-validated)

- Per-body Platform collision mask, independently selectable from Terrain, Prop and Character masks. Existing authored bodies in both labs enable it. The three stack props in the Physics Interaction Lab move one world unit right to clear the initial pusher geometry.
- Prescribed moving platform boundaries in the chronological translational prop solver; relative linear sweeps split at analytical turn events. Friction uses relative tangential velocity and the Default platform material. Platform impact restitution is inelastic for this first pass.
- Use active face constraints and incompatible prescribed anchor velocities to classify prop compression. A normal push is not a crush. StopPlatform (default) or Destroy per prop. If a compression includes a StopPlatform prop, stop wins; otherwise remove the first eligible ID and re-evaluate. Removal and exclusion cleanup occur only after a successful staged prop update. Last destroyed IDs and counters are observable.
- Stop rolls back the proposed tick and retries with the offending path paused at the last committed tick boundary, not at the exact fractional contact instant. If retry fails, restore playback changes and propagate the fault. Manual Resume retries forward motion; Reverse releases an obstruction pause and moves away when legal. No automatic recovery in this candidate.
- Secondary platform contact friction is evaluated even if a different body's terrain collision wins the initial event tie. Bounded co-moving roundoff snap is allowed only for microscopic overlap and matching normal velocity; genuine missed impacts are still errors.
- Physics Interaction Lab: body 9 rests on horizontal carrier 8003; body 10 rests on floor under vertical crusher 8002; the existing three-crate chain encounters horizontal pusher 8001. Engine Integration Lab: body 12 rests on platform 8001. Restart to restore fixtures and playback together (Reset Bodies can reject placements intersecting a currently moved platform).
- Visible PLATFORM COLLISION, PROP CRUSH RESPONSE, PROP OBSTRUCTION, PLATFORM CONTACTS / STOPS, PROPS CRUSH DESTROYED controls/counters.
- Benchmark workload v5 retains absolute tick alignment, adds platform-prop contact/stop/destroy counters, requires real platform contact evidence, and adds carrier-focused visual comparisons. Existing test suites retain reference/indexed/verify comparisons. New PB01PlatformPropTests cover relative sweeps, horizontal secondary friction, rising stack, turn split, stop rollback, reversal, destruction, vertical crush, mask restoration and repeated 60 Hz motion.

## Limits and validation

The player still uses the staged kinematic character–prop interaction: character motion is computed before prop motion. Full mixed player/NPC/prop compression, character carrying on moving props, exact intra-tick destruction timing, automatic obstruction recovery, full lifecycle events, per-platform material authoring and arbitrary contact-island resolution remain outstanding. Horizontal transport of tall stacks is not yet guaranteed. Do not treat the new solver as a final general multi-body solver. Numerical faults are not swallowed. Existing snapshot/rendering registration uses the committed prop list. The user's newest screenshot was not available as a readable attachment, so its reported issue is not diagnosed or fixed by this candidate.

Python tooling/fixture tests: 21 passed. No .NET SDK/C# compiler available in authoring environment; C# compilation, unit tests, graphical tests and benchmark validation are pending. Run targeted tests first, then `py .\Tools\pb01_validate.py --tier quick`. Keep a baseline copy for rollback.
