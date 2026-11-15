# Milestone 26 — Phase 2A: Exact-time contact constraints

## What this implements

- The shared parabolic collision scheduler retains `SweepParabolicFirst` for existing callers and adds `SweepParabolicCoincident` for finding all hits **at exactly the first impact timestamp**. Candidates remain sorted by stable collider ID, irrespective of input enumeration.
- When two or more hits are exactly coincident, `UnifiedAcceleratedKinematicStepper` resolves them together rather than projecting velocity against one normal and rediscovering another at the same instant.
- `SimultaneousContactResolver` treats cardinal contact normals as world-velocity bounds: outward normal +X requires `vx >= surfaceVx`; -X requires `vx <= surfaceVx`, analogously for Y. When the bounds intersect, it clamps velocity into their intersection and removes acceleration pushing into an active bound.
- Opposing moving/static or moving/moving velocity constraints with no feasible velocity yield an existing `CharacterCrushException` with a deterministic mover/opponent pair and the actual event time. Impossible static-only constraints remain a `SimulationNoProgressException`.
- Single-contact resolution, moving-platform support handoff, fault sensitivity, collision event budgets, zero-duration validation, existing corner-graze precision policy, and stage geometry retain their previous behavior.

## Important limits

This is a *first integrated slice* of Milestone 26 Phase 2, not a general contact manifold or rigid-body physics system. Only **exact same-time** contact candidates in the shared constant-acceleration stepper are grouped; jump/reference analytical and platform-ride schedulers still use their established event paths. No time epsilon coalesces physically distinct impacts. The platform trajectories are immutable analytical scripts; they do not stop when they hit static scenery or each other. A configurable obstruction policy and broader cross-path manifold unification remain for Phase 2B.

This patch adds no numerical penetration tolerance and no automatic displacement/depenetration. It does not change the existing crush preflight detector's predictions for already supported characters, or the strict initial-overlap validator. Any real depth overlap still faults.

## Verification

1. Apply the ZIP at the root containing `PlatformerSandbox.Simulation` and `PlatformerSandbox.Tests`.
2. Build the solution, run the entire xUnit suite, and investigate any failures; do not weaken old assertions.
3. Run F5 Strict with all four crush gym scenarios and repeated respawns. Verify ordinary jumping, landing, supported rides, exact elevator turns, and wall contact.
4. Exercise long-running fixed-step replay and state-fingerprint tests, ideally across Strict, Diagnostic, and Resilient.
5. If this introduces a behavioral regression, restore the three changed/new simulation files from the previous Git commit and preserve the regression evidence.

The authoring environment cannot run the .NET build or xUnit; installation into the user's validated tree is the final verification step.
