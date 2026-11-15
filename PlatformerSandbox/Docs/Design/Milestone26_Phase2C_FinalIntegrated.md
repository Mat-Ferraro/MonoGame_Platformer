# Milestone 26 — Phase 2C: Supported Contact Manifolds and Obstruction Policy

## Baseline and purpose

Apply this integration **after** the confirmed working Milestone 26 Phase 2B analytical-jump manifold package. It is a continuation of the existing immutable, analytical moving-platform model; it is NOT a new rigidbody physics engine. No movement tuning, collider placement, fault sensitivity default, simulation event budget, or gym crush/respawn policy is altered.

The earlier M26 phases already provide:

- M26 Phase 1: physical crush event + gameplay policy (respawn/throw), vertical and horizontal closing-gap preflight checks;
- M26 Phase 2A: simultaneous contacts in the shared parabolic accelerated stepper;
- M26 Phase 2B: simultaneous contacts in analytical jumps, with ID-safe moving-floor landing handoff.

This package addresses the remaining supported ride and independent platform/world-obstruction cases.

## Integrated behavior

1. **Supported riding:** `MovingPlatformRideEventScheduler.Step` queries all external static and moving candidates with `SweepParabolicCoincident`, restricted to the analytically supported leg. The current floor is an **implicit upward contact constraint**, not a new impact event. Every exact-time external normal is sent to `SimultaneousContactResolver` as a single manifold. No epsilon-based merging of non-equal timestamps.
2. **Crushing:** When an upward moving support requires `vy >= support.vy` but a stationary ceiling requires `vy <= 0`, the conflict becomes a classified `CharacterCrushException` at the collision timestamp. Same-time moving pusher vs static wall also classifies as horizontal crush. Ordinary wall slides remain slides, not crushes. No position or timestamp is committed on failure.
3. **Independent moving bodies:** Riding now considers other moving platforms, not only static walls or the prior preflight vertical squeeze. The current support stays authoritative while the resolved vertical speed matches that support; otherwise a new geometrically valid top contact can take over, in deterministic ID order. At most one moving floor owns the rider at any time.
4. **Same-frame handoff:** The jump-landing remainder and ordinary supported riding both call the updated ride scheduler. All external contacts are included in the existing global controller budget and public contact trace. The controller passes the correct current support instead of assuming the initial landing platform owns every later ride leg.
5. **Corner/no-progress safety:** A supported rider already flush with a second moving side does not repeatedly accelerate into it at t=0. The guard only suppresses inward acceleration while the rider is not separating; existing exact contact, numerical-correction, and event-budget safety rules are retained.
6. **Independent platform obstructions:** `MovingPlatformObstructionDetector` analytically checks moving/static and moving/moving AABB overlap, splitting at each relevant platform turnaround. Strict positive-area overlaps count; an endpoint face touch or isolated corner graze does not. An event has a deterministic timestamp and collider IDs.

## Explicit authoring policy

`PlatformObstructionPolicy` is independent of player crush outcome and independent of `SimulationFaultSensitivity`:

- `FollowAuthoredTrajectories` (default) — **preserves prior behavior**: platforms follow their authored immutable timeline even if world geometry intersects. This is the game's existing autonomous/kinematic platform model. Player collisions and crushing still apply.
- `RejectWorldObstructions` — **opt-in world validation**: before a player frame, reject any analytically proven platform/static or platform/platform positive-area overlap in that interval via `PlatformObstructionException`; preserve the entire pre-update player state, and record `LastFault`. This mode is useful for levels where platforms must never intersect other geometry. It does not silently stop or teleport a platform.

A dynamic *stop-on-contact / push-other-platform* actuator is **not** implemented: immutable `MovingPlatformTrajectory.Evaluate(worldTime)` cannot stop while retaining the same authoritative trajectory for rendering, sweeping, jump evaluation, replay, and rider support. Supporting that behavior would require a separate stateful platform motor, transactional platform state and a revised world-time scheduler; inventing an ad hoc "stop" in the character controller would reintroduce independent physics clocks. This is a deliberate model boundary, not an unhandled exception path.

## Tests and verification

New test classes:

- `Milestone26CompleteRidingManifoldTests`: coincident static walls, deterministic order, event-budget atomicity, rising lift + ceiling crush, secondary moving side contact, moving pusher + static wall crush, and compatibility with the old single-wall riding behavior.
- `Milestone26WorldObstructionPolicyTests`: analytical moving/static entry, moving/moving relative approach, tangent corner + reversal exclusion, initial overlap, transactional strict reject and default authored-movement compatibility.

**Acceptance on Windows / Visual Studio**:

1. Rebuild all projects and run the entire xUnit suite, including all previous M23–M26 tests.
2. Exercise all four default gym crush scenarios repeatedly in F5 Strict, plus F6 Diagnostic and F7 Resilient. Confirm crash-free respawns, stable timestamps and support/collision traces.
3. Jump into corners, land on a platform near a wall, ride into an immovable wall, then jump away; ride the elevator through turnarounds, ceiling and floor proximity.
4. Run the existing 10,000-frame deterministic stress tests. New exceptions must be investigated with contact traces—never raise event budgets or broadly relax penetration tolerances.
5. Test `RejectWorldObstructions` in separate headless fixtures; do **not** enable it in a level containing intentionally overlapping moving scenery.

**Verification status in authoring environment:** this package can be inspected and ZIP-integrity-checked, but the .NET 10 SDK is unavailable here. Source consistency is not a substitute for compiling and running your xUnit suite.
