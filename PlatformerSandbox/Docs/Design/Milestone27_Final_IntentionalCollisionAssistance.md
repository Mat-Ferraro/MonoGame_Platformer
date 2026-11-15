# Milestone 27 — Intentional Collision Assistance (Completion Candidate)

## Status
Built against the validated Milestone 26 Phase 2C baseline plus the working
Milestone 27 Phase 1 ceiling-corner assistance. Source/ZIP checks performed.
**The supplied C# project must still be compiled, xUnit-tested, and manually
validated before claiming that Milestone 27 is complete.**

## Features and exact scope

1. **Ceiling corner (static and moving)**: a single underside impact during
   upward player motion *relative to the contacted surface* may shift the player
   sideways to the contacted edge. The moving surface is sampled at the exact
   authoritative hit timestamp. The jump's vertical reference is preserved.
   No assistance on a simultaneous contact manifold or in the absence of a
   complete, unobstructed horizontal correction path.
2. **Ground micro-step / anti-snag (static only)**: on a grounded static floor,
   while pressing into a touching or precision-near short static step, allow an upward
   lift and small forward toe placement onto the step top. The vertical and
   horizontal legs are checked separately against *all* colliders. No climbing
   full walls, unsupported step-ups, or moving-support reparenting.
3. **Ledge catch (static and moving)**: while ordinarily falling (outside an
   active analytical jump), steering toward a narrowly missed floor edge may
   shift the player laterally while keeping the same Y position and velocity.
   Only a downward *relative* approach to a moving ledge qualifies. No neutral
   magnetism. At most one catch per airborne episode, reset on landing/takeoff.
   The normal collision solver handles any later landing.
4. **Observability and control**: F9 toggles the full assistance profile with a
   clean reset. Each successful correction publishes `CollisionAssistEvent` with
   `Kind`, collider ID, timestamp, horizontal and vertical displacement. The
   existing HUD and window title show enabled state and total assists.

## Default test-gym policy (only when F9 enabled)

- Ceiling corner maximum lateral correction: 0.18 world units, at most two per jump.
- Ground step maximum height: 0.12 units; forward toe: 0.04 units.
- Ledge catch maximum lateral correction: 0.16 units; maximum height above
  the target floor: 0.15 units.
- Simulation library default: **all assistance disabled**.

Every correction must have finite, positive, explicitly bounded travel. Candidate
paths are checked for positive-area overlap with **every** static and moving AABB
at the adjustment timestamp. Ties in choosing candidate floors/steps are
resolved deterministically by correction size, then collider ID. Assistance
is distinct from strict floating-point face alignment/recovery.

## Preserved invariants

- No broad detector tolerance increase or automatic recovery for real penetration.
- A new crush is detected by the existing resolver; assistance does not bypass
  simultaneous manifolds or classified crush exceptions.
- No change to the 8-contact collision-event budget.
- Motor updates are transactional: failed steps publish neither adjusted bounds
  nor assistance events, and the one-catch limit is committed only on success.
- No changes to authored moving platform trajectories or obstruction policy.
- Coyote time/jump buffering remain gameplay input policy; ledge catch is
  separately configured and is **not** an extension of either timing window.

## Deliberate limits

- The micro-step works from a static floor into a small **static** step;
  it does not dynamically lift a character riding an elevator into a new
  moving body. That would require a dedicated moving-support handoff.
- Ledge catch is applied to ordinary freefall at the end of a completed motor
  update. An actively controlled analytical jump keeps its own trajectory and
  only receives ceiling-corner assistance, so catch does not rewrite it.
- These are fixed-world AABB gameplay assists, not slopes, capsules, animation
  systems, wall jumps, step-climbing of tall obstacles, or general pathfinding.
- Platform motion remains deterministic and authored; no stop-on-obstruction
  platform motor was added.

## Acceptance checklist

1. Build the existing .NET 10 solution, then run **all** xUnit tests. New tests:
   `Milestone27FinalAssistanceTests`, plus retained Phase 1 tests.
2. Repeat the four Milestone 26 horizontal/vertical crush scenarios in F5
   Strict, both assistance OFF and ON. Crashes and swallowed crushes are failures.
3. Jump just beneath the edge of a horizontal platform and a moving elevator;
   verify optional correction at the actual moving edge and no correction
   if another blocker closes the exit.
4. Test a short grounded step, a tall wall, a ceiling immediately over a step,
   and a falling near-miss of static and moving ledges. Verify one ledge catch
   maximum per airborne episode.
5. Replay long 60 Hz scenarios, corner/turnaround tests and deterministic
   stress tests in all F1/F2/F3 presets and F5/F6/F7 fault policies. Keep
   existing strict event budgets and deep-penetration assertions intact.
6. Compare disabled behavior with the Milestone 26/Phase 1 reference.
