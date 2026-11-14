# Milestone 24 — Phase 2A: Live first-collision arbitration

## Purpose

This is the first, deliberately narrow integration of the tested Phase 1 `UnifiedCollisionEventScheduler` into the **live** `CharacterMovementController`. It is a compatibility checkpoint before converting the controller's entire movement update into a unified event loop.

## Changed behavior

During **ordinary airborne (non-reference-jump) motion** with a moving platform present, the controller now calls `UnifiedCollisionEventScheduler.SweepParabolicFirst` to choose the earliest static or moving contact. That centralizes the stable-ID tie-break and eliminates the controller's hand-rolled comparison between `ParabolicAabbCollisionDetector` and `MovingPlatformCollisionDetector`.

When a moving-platform contact wins, the controller preserves its existing slide response in the platform's velocity frame and its existing top-surface support check. When the earliest event is static, the controller retains its proven static `AcceleratedKinematicStepper` for that segment. The goal is to verify arbitration and preserve previously working player motion, not switch every movement mode at once.

## Deliberately unchanged / remaining Phase 2 work

- Held/released analytical jumps still use `SteerableJumpMotor`.
- Riding a platform still uses `MovingPlatformRideSolver`.
- Static collision response after the first event can consume the remainder of the segment without rechecking moving collisions. Replacing that with full unified re-query and handling multi-normal contacts is **still pending**.
- The Phase 1 Strict / Diagnostic / Resilient fault policies are available as independent services, but **they are not yet wired into this live controller**. Do not claim the gym is in release-resilient mode.
- This change does not add crushing detection, simultaneous-contact manifolds, or checkpoint rollback.

## Tests and acceptance

- Four new controller integration regressions cover moving-first, static-first, no contact, and continuation after a moving-floor landing.
- All Phase 1 scheduler and fault-policy unit tests must continue to pass.
- In the gym, verify the baseline F1 and snappy F3 presets; landing, riding, corner approaches, and jumping off the platform; and no repeat of the zero-time top-contact loop.

## Installation

Extract the ZIP at:

`C:\ProgrammingProjects\MonoGame_Projects\MonoGame_Platformer\PlatformerSandbox`

Replace only:

`PlatformerSandbox.Simulation/CharacterMotor/CharacterMovementController.cs`

The new xUnit file is placed in `PlatformerSandbox.Tests/Simulation/CharacterMotor/`.

## Engineering note

No .NET build environment was available during preparation. The delivered archive was checked for integrity and source/API correspondence; final C# compilation and test execution remain with Visual Studio.
