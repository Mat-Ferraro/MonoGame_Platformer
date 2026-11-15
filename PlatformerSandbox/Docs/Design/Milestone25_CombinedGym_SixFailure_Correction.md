# Milestone 25 — Six-failure combined gym correction

This patch applies over `PlatformerSandbox_Milestone25_DescendingElevatorSideCollision_CombinedGym.zip`.

## What failed

- Two side-contact regression cases encountered an invalid **static-floor penetration** during a grounded step after moving-platform collision arbitration. The moving collision detector returned a platform-relative hit converted into world coordinates by subtracting and re-adding platform displacement. On the tangential (world Y) axis that calculation could introduce an error even when the authored body remains at world Y=0. The new conversion evaluates the body in the original world trajectory and aligns only the actual contact axis to the platform's face.
- Two old reflective test fixtures attempted to simulate an unsupported body by clearing `_onMovingPlatform` and `_grounded`, but omitted the newly authoritative `_supportPlatform`; the inconsistency caused a different code path to run. The fixtures now clear all three fields, retaining the original expectations and exact contact requirements.
- One gym test assumed the *default* platform mode was `Horizontal` instead of `Both`. It now verifies that invalid input preserves `Both` and both platform instances.
- One Strict-mode combined-gym replay reached an already processed collision boundary so close to the authoritative end timestamp that the remaining local interval had no distinct absolute clock tick. For an already resolved contact **only**, the stepper integrates this bounded final tail in local analytical time, validates endpoint penetration in the full world, and closes the update. Fresh unrepresentable input intervals and significant displacements still fault according to policy.

## Invariants retained

- No general tolerance increase for penetrating moving platforms or static floors.
- No arbitrary exceptions are caught or suppressed.
- Strict mode still raises faults for actual recoveries, invalid initial states, unsafe travel and nonprogressing interior events.
- Moving contacts remain deterministic and exactly face-aligned.
- Multiple-platform gym remains the default without F8 switching.

## Validation

The package source and archive were reviewed, but the .NET SDK is not installed in the packaging environment; run the full Visual Studio test suite and manually check side-contact and dual-platform gameplay before closing Milestone 25.
