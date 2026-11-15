# Milestone 24 — Unified Collision/Event Integration and Fault Sensitivity

**Status:** Integrated implementation candidate. Verify with the full Visual Studio build, xUnit suite, and live gym before marking complete.

This update is based on the validated Milestone 24 Phase 2C + sub-resolution jump-duration hotfix. It is intended to be installed **after** those packages, not onto an earlier Milestone 23 source tree.

## Scope completed in this package

1. **Ordinary airborne motion** continues using the unified static/moving accelerated stepper, including re-querying both collider types after each interruption (from Phase 2B).
2. **Analytical jumping** continues selecting contacts through the unified scheduler, with phase transitions and transactional jump copies (from Phase 2C).
3. **Static ground motion** now uses `UnifiedAcceleratedKinematicStepper` with an empty moving-collider set, rather than a separate static-only accelerated stepper. Precise static support-loss time remains an analytical event that terminates the ground phase.
4. **Supported moving-platform motion** now uses `MovingPlatformRideEventScheduler`. The platform's next turnaround and the player's horizontal phase boundary limit the segment. Support departure competes against collisions with static scenery. The supporting platform is excluded from its own collision query to avoid treating resting support as repeated impact.
5. **Fault sensitivity** retains the independent Strict, Diagnostic and Resilient profiles. Known, bounded numerical recoveries can be accounted through `SimulationFaultSession`; unsafe conditions still fault.
6. **Event budgets and no-progress faults** have named exception types. Event-limit exceptions can carry the ordered partial contact trace (collider ID, time, normal). The controller captures a pre-step fault snapshot without committing partial gameplay state.
7. **Release-oriented checkpoint fallback** in the movement gym is explicitly limited to those named faults; it does *not* suppress arbitrary `Exception`/`InvalidOperationException` failures. An eligible failure causes one clean reset of the player and buffered input; three consecutive fallback attempts are the limit. The number and last cause are exposed for diagnostics.
8. **Reproducible headless stress testing** includes a pure integer-driven input schedule, state fingerprinting, first-failure frame and state capture, 10,000-frame runs across all three tuning presets, and a separate long moving-platform ride test.

## Numerical/recovery policy

- Known analytical phase boundaries (speed cap, velocity-zero reversal, support departure, apex) are resolved by the owning solver, not discarded as arbitrary tiny durations.
- The unified collision service remains strict about initial overlaps and nonadvancing time. A completed step never commits invalid geometry because of an unhandled exception.
- **Strict:** `SimulationFaultSession` throws when a classified numerical recovery is requested. Use for debugging.
- **Diagnostic:** the same bounded recovery is applied and recorded. Use for QA and exhaustive stress schedules.
- **Resilient:** bounded recoveries are applied; a **classified** terminal failure can cause a checkpoint restart instead of crashing the gym. The new fallback is a *gameplay* policy; the simulation library remains independent of spawn locations.
- Unclassified exceptions, unsafe deep penetrations, and arbitrary programming bugs are **not** silently ignored. `Resilient` is not a universal guarantee against crashes.
- Existing deliberate face/contact alignment at machine precision remains a geometric normalization rule; it is not a general-purpose penetration waiver.

## Deterministic arbitration

- The scheduler chooses minimum time of impact and breaks exact-time ties using stable collider IDs.
- A static or moving impact interrupts the analytical segment. The stepper applies the selected explicit collision response and re-queries for the remaining time.
- On support, riding is solved relative to the platform. A static collision or support departure ends the current ride leg, then the controller re-plans the remaining update.
- A zero-time collision that does not change velocity or constrained acceleration is an explicit `SimulationNoProgressException`, not an unbounded event loop.
- The controller publishes next bounds, velocity, support and jump state only after the full update is successfully calculated.

## Validation checklist

1. **Build** the six-project solution in Visual Studio. No new project references or NuGet packages are required.
2. **Run All Tests.** New tests are located in `Milestone24RideArbitrationTests` and `Milestone24StressAndFaultModeTests`; the older Phase 1, 2A, 2B and 2C tests must remain green. Long-running failures include frame/time context; do not disable those tests to obtain a passing suite.
3. In the gym, test F1/F2/F3 independently of F5/F6/F7. The in-game fault-mode HUD from Phase 2C remains active; window title also reports cumulative checkpoint fallbacks.
4. Walk into a stationary wall, jump through apex and descend, walk off a static ledge, land on the moving platform, ride across turnarounds, jump at platform corners, and press into a static wall while riding.
5. Verify **Strict** surfaces recognized anomalies; **Diagnostic** records permitted recoveries; **Resilient** handles classified errors by checkpoint restart without propagating invalid motor state. Inject a classified failure through `MovementGymCheckpointFallback.Execute` in tests to verify the policy independently from accidental playtime timing.
6. Run the 10,000-frame headless tests (166.67 simulated seconds each) and investigate any failing frame rather than adding a wide tolerance or raising event budgets.

## Explicit limits / later milestones

- There is **one** horizontally translating support platform in the current gameplay motor. Multiple independent moving platforms, vertical elevators, simultaneous multi-normal crush constraints, and gameplay-specific crush damage/death policies are outside Milestone 24 and remain Milestones 25–26 work.
- All previous contact assistance is still deliberately bounded. Corner forgiveness and designer-configurable assistance belong to Milestone 27.
- The checkpoint fallback is intentionally implemented in `MovementGymSession`; other games will need to select their own safe spawn/checkpoint policy.
- A C# build and xUnit execution must be performed locally before treating this package as a verified milestone. The ZIP and static source checks alone are insufficient.
