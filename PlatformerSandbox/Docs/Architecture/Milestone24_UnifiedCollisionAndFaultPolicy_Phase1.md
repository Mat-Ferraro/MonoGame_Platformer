# Milestone 24 — Unified Collision Events and Fault Sensitivity (Phase 1)

**Status:** Source package prepared; compilation and xUnit verification pending in Visual Studio.  
**Target:** PlatformerSandbox / MonoGame, .NET 10, C#.  
**Scope:** Additive simulation foundation. Does **not** replace the currently playable controller.

## Why this phase exists

Milestone 23 exposed unrelated branches for static vs. moving collisions, repeated zero-time contact events, and numerical time remainders. This phase introduces a shared detection event shape, a stateless event-aware stepper for constant-acceleration motion, and configurable numerical-recovery diagnostics. The existing movement-gym path is deliberately retained as a known-working baseline.

## New production components

| Module | Purpose |
| --- | --- |
| `Collision/Contacts/UnifiedCollisionHit.cs` | One contact representation for static and moving geometry: ID, world impact time, normal, body position/velocity, surface velocity, relative velocity. |
| `Collision/Detection/UnifiedCollisionEventScheduler.cs` | Compare earliest static vs. moving AABB contacts, with stable ID tie-breaking and initial-penetration validation. Contains parabolic and monotone analytical queries. |
| `CharacterMotor/UnifiedAcceleratedKinematicStepper.cs` | Stateless stepper: query unified world, advance to impact, apply stop/slide response in surface-relative coordinates, and query remaining time again. Return moving-floor support handoff. |
| `CharacterMotor/UnifiedAcceleratedStepResult.cs` | Immutable committed result with contact events and optional moving-floor ID. |
| `Diagnostics/SimulationFaultSensitivity.cs` | `Strict`, `Diagnostic`, `Resilient` (independent of build type). |
| `Diagnostics/SimulationFaultPolicy.cs` | Immutable configurable policy, bounded discarded displacement and per-step recovery budget. |
| `Diagnostics/SimulationFaultSession.cs` and recovery types | Per-step deterministic event records and counters; strict faults on a recovery, diagnostic/release record and proceed if within limits. |
| `Diagnostics/SimulationTimeProgress.cs` | Consume **only a known-event** time remainder that cannot advance binary64 absolute time and has provably bounded displacement. |

## Important boundaries

1. **No live controller replacement yet.** The existing `CharacterMovementController`, `SteerableJumpMotor`, and MonoGame gym remain unchanged in Phase 1. Next phase integrates the scheduler into those paths behind regression tests.
2. **Safety remains strict.** Invalid input, initial penetrating geometry, unresolved zero-time contacts, excessive contact counts, and unsafe numerical corrections remain failures in *every* fault mode. `Resilient` does not mean catch-all exception suppression.
3. **No gameplay physics changes by fault mode.** Physical response equations remain the same; only the policy reaction to an explained, bounded recovery differs.
4. **Landing transfers ownership.** A moving-floor contact returns the impact and platform ID. The future higher-level rider coordinator must complete the remaining update. The stepper never applies platform displacement twice.
5. **Analytical custom curves require intervals monotone in both world space and platform-relative space.** Split at jump apex, early release, input reversal, and each relative-velocity turning point. The query checks visible relative-direction reversals and rejects them instead of silently missing contact; it is not a general root-finder for arbitrary curves.
6. **Multi-normal simultaneous constraints, crushing, and multiple moving platform interaction policies** are not implemented here. Exact-time ties use smallest stable collider ID; resolving all simultaneous normals is later work.
7. **Last-known-valid checkpoint fallback** is not hooked into the gameplay host in this phase. An unrecoverable update throws before committing the stateless stepper result. Release-mode checkpoint restoration belongs to controller/host integration.
8. **Keep diagnostics separate from on-screen visibility.** Even resilient recoveries are counted. The host may aggregate and log them without showing a user-facing error.

## Example usage

```csharp
// Both static and moving collision sources are considered in the SAME query.
UnifiedCollisionHit? hit = UnifiedCollisionEventScheduler.SweepParabolicFirst(
    bounds, velocity, acceleration, simulationTime, fixedDt,
    staticColliders, movingPlatforms);

// The Stepper runs repeated earliest-event queries after applying a response.
SimulationFaultSession session = SimulationFaultPolicy.QualityAssurance.StartStep();
UnifiedAcceleratedStepResult result = UnifiedAcceleratedKinematicStepper.Step(
    bounds, velocity, acceleration, simulationTime, fixedDt,
    staticColliders, movingPlatforms,
    CollisionResponseMode.Slide, maxContactEvents: 8, diagnostics: session);
// session.Recoveries reports deterministic numerical recoveries.
```

## Test coverage

The package adds **38 xUnit cases** across three new test classes:

- `UnifiedCollisionEventSchedulerTests` (13): static-first, moving-first, tie-by-ID, duplicate-ID/penetration validation, curved and moving relative collisions, unsplit relative reversal rejection.
- `UnifiedAcceleratedKinematicStepperTests` (11): exact free motion, two contacts in one update, side push, moving-floor handoff, stop vs slide, event budget, transactional invalid-state failure, strict/release time remainder handling.
- `SimulationFaultPolicyTests` (14): strict/diagnostic/resilient profiles, recoverability limits, recovery budget, immutable diagnostics, invalid arguments, representable vs nonrepresentable time.

**Acceptance criteria:** entire six-project solution compiles; all existing tests remain green; 38 new cases pass. After that, integrate the new scheduler into the live controller incrementally and run the gameplay stress scenarios in F1/F2/F3.

## Next integration sequence (Milestone 24 Phase 2)

1. Introduce the same contact query into ground/freefall and steerable jump, while retaining the existing motor's single authoritative state.
2. Move support handoff and response policies to a shared event scheduler; avoid repeated zero-time contacts.
3. Define strict/diagnostic/resilient configuration at application-context level; default automated tests to Strict; explicitly select Diagnostic/Resilient in recovery tests.
4. Make authoritative motor updates transactional and add a host-owned last-known-valid-state fallback for release where safe (not an arbitrary teleport).
5. Add deterministic long-running 60 Hz tests with logged seed, tick, input, collider and movement state; stress platform turnarounds and switching F1/F2/F3 profiles.
6. Confirm gameplay: stationary ride, jump, corner contact, ceiling and wall contact, platform reversal, no state corruption and no unexplained event-limit faults.

## Install

Extract the ZIP's contents into:

`C:\ProgrammingProjects\MonoGame_Projects\MonoGame_Platformer\PlatformerSandbox`

All files are **new**. Do not delete or move existing project files. Rebuild the solution and run all tests in Visual Studio.
