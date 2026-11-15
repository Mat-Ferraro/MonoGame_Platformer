# Milestone 26 — Phase 2B: Analytical jump coincident-contact constraints

## Starting point

This package is an incremental patch on **Milestone 26 Phase 2A** (`PlatformerSandbox_Milestone26_Phase2A_SimultaneousContacts.zip`) after the user's successful build/test/run. It requires the Phase 2A `SimultaneousContactResolver`; do not apply to a Milestone 26 Phase 1 tree.

## Implemented

1. `UnifiedCollisionEventScheduler.SweepAnalyticalCoincident` enumerates all impacts at the **exact timestamp of the first** analytical collision, across static AABBs and independently moving platforms. Contacts are sorted by stable collider ID. It reuses the existing monotone analytic face-crossing searches and moving-platform turnaround/relative-turn partitioning. It introduces no time epsilon or sampled movement integration.
2. `SteerableJumpMotor` processes a multi-contact analytical event as one constraint set using the Phase 2A resolver. Compatible X/Y constraints share a common projected velocity; conflicting moving/static or moving/moving velocity constraints throw `CharacterCrushException` without committing state.
3. The jump motor aligns each **verified** impact face within the already permitted 1e-8 analytical face-correction budget (no general depenetration). It handles an X wall while retaining the Y reference, or a Y ceiling/floor while interrupting it, exactly as its single-contact path does.
4. A complete same-time contact set consumes the existing 8-event budget atomically. Incompatible sets, event-budget exhaustion, invalid input, or non-advancing zero-time manifolds fail explicitly. No partial state is committed.
5. `CharacterMovementController` now identifies moving-floor landings by the most recent positive-Y moving-platform contact, not `contacts[^1]`. With ID-sorted coincident events, the last contact can be a wall despite a valid simultaneous platform landing.
6. Eight xUnit scenarios exercise exact static corner ties, ID ordering, noncoincident times, single-hit equivalence, moving/static opposition, jumping into a ceiling and wall simultaneously, transactional event budget, and strict initial penetration validation.

## Intentional limits

- The new manifold path is used only for **two or more truly equal-timestamp analytical impacts**. Existing single-contact jump behavior and platform-riding logic remain intact.
- The jump motor's horizontal blocked-input policy and Y reference ownership are retained; the projected acceleration is not a replacement for the jump's analytical motion law.
- Platform-riding arbitration still processes one static impact per ride leg; simultaneous static contacts and autonomous platform/platform obstruction policies remain Phase 2C / subsequent work.
- Existing crush preflight checks, fixed-step handling, narrow precision corrections, and fault-policy profiles are not bypassed or relaxed.

## Validation on Windows / Visual Studio

1. Commit the current working Phase 2A version.
2. Extract the ZIP at the `PlatformerSandbox` project root, preserving relative paths. It replaces three existing C# files and adds one test file plus this note.
3. Rebuild the solution and run all tests. The eight additional `Milestone26Phase2BAnalyticalManifoldTests` must pass alongside the old tests.
4. In F5 Strict, repeatedly jump into corners, jump against wall 12/14, ride/land on elevator 8, test all four crush directions, and verify respawn/replay determinism.
5. If a case fails, retain full exception details, contact trace, and matching test output. Do **not** raise collision-event limits or weaken initial-penetration rejection.

**Authoring verification:** source structure and archive integrity were checked; no .NET SDK was available in the authoring environment. Compilation, xUnit, and gameplay verification remain necessary on the user's development machine.
