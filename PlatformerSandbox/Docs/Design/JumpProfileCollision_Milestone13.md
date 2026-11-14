# Milestone 13 — Piecewise analytical jump collision queries

## Intent

Milestone 13 connects the *existing* vertical jump-flight profiles (held, early-release,
optional apex-hang power curves) and the *existing* horizontal reference-input schedules
to static AABB collision detection. No existing source files or project files change.

The input is a **designer reference trajectory**, not an authoritative gameplay path.
The detector returns the **first contact**. `JumpReferenceInterruption` applies an
explicit stop/slide velocity response at that contact and **stops**. It does not
silently resume the original reference, which would be invalid after an interruption.

## API

1. `JumpReferenceCollisionPlan.Create(verticalFlight, horizontalProfile, inputSchedule, initialHorizontalVelocity)` creates the same reference as `JumpTravelSolver.Create` but adds analytically known monotonic phase boundaries.
2. `JumpProfileCollisionDetector.SweepFirst(initialBounds, plan, obstacles, contactAlignmentTolerance)` searches for the first collision event, or returns `null`.
3. `JumpReferenceInterruption.AdvanceToFirstContact(...)` returns either the unobstructed terminal state or a collision interruption state, including elapsed and remaining reference time.

Coordinates use the existing world convention: positive X to the right, positive Y up. `initialBounds` is the world-space AABB at takeoff. The reference's horizontal displacement starts at zero; vertical displacement starts at zero.

## Continuous-path collision search

The vertical reference is monotone over held ascent, released ascent, and descent.
The horizontal reference is monotone between input changes, speed-target transitions,
and braking/reversal turning points. The plan creates these event boundaries without
sampling the simulation at fixed increments.

On each monotonic interval the detector solves for intersections with the four
AABB contact planes. For general power-curve motion, a unique root is located by
bounded binary64 bisection (maximum 128 iterations). It then partitions time at
all roots and tests only the interior of each root-delimited interval. This is a
continuous collision search **within floating-point precision**, not a numerical
physics integrator or a fixed-interval linear sweep.

Contact events report collider ID, event time, world-space bounds, outward surface
normal, analytical incoming velocity and acceleration. A small, explicit
face-alignment tolerance (default `1e-8` world units) guards against returning a
contact position too far from the face. Exact-time ties prefer lower collider IDs;
X-axis contact takes precedence over Y-axis contact at simultaneous corner entry.

## Explicit restrictions / future work

- Static **solid AABB** obstacles only (no slopes, one-way platforms, moving platforms, dynamic bodies).
- Initial penetration is rejected, not resolved implicitly.
- Tangential contact alone does not count as collision; a terminal face hit with inward velocity does.
- Numerical behavior is bounded by binary64 coordinate resolution. If roots or face alignment cannot be represented accurately, the query may fail explicitly; it does not promise arbitrary-precision geometry.
- On an interruption, the original reference becomes **invalid** for subsequent motion. Its remaining time is reported to the caller. The next milestone will define the authoritative runtime motor that starts a *new* analytical segment with contact constraints, correctly managing gravity, wall contact, further events, and state transitions.
- `Stop` and `Slide` apply velocity policy at first contact only; acceleration is reported but not yet projected or used to continue motion.
- Movement event scheduling, collisions, and rendering remain separate; no MonoGame dependencies are introduced.

## Verification

Build the existing solution and run all xUnit tests. New test cases cover reference profile collisions (including non-parabolic apex hang), short/early-released jumps, scheduled input changes, reversal turning points, thin obstacles, floor and ceiling contacts, start contacts, deterministic tie handling, and first-contact interruption response.
