# Milestone 26 — Phase 1: Classified crushing and gameplay response

## Baseline

Apply this package on top of the **passing Milestone 25 installation** including:

- Combined horizontal platform + vertical elevator gym.
- Consolidated six-failure correction.
- Near-endpoint collision hotfix.

This package contains **complete replacement C# files**, new C# files, and this design note. No scripts, package changes, project/solution-file changes, or external dependencies are required.

## Design separation

A confirmed crush is a collision *outcome*, **not** a numerical simulator fault. The headless Simulation layer reports a `CharacterCrushEvent` (IDs of the closing and opposing surfaces, axis, timestamp, and snapshot). If detected during a transactional controller update, Simulation throws a dedicated `CharacterCrushException` without committing intersecting bounds or advancing the motor state. This is deliberately excluded from `SimulationFaultException` and Resilient-mode numeric recovery.

Gameplay decides the consequence through `MovementGymCrushPolicy`:

- `Respawn` (default) — the gym catches **only** `CharacterCrushException`, preserves its tuning and fault mode, increments `CrushRespawns`, exposes `LastCrush`, and restarts at the scenario's known-safe spawn.
- `Throw` — preserves the dedicated exception for debugging/testing.

The HUD and window title display the cumulative crush respawn count. Strict/Diagnostic/Resilient (F5/F6/F7) remain unchanged; **a legitimate crush respawns even in Strict**.

## Initial classified scenarios

1. Rider is carried into an immovable ceiling by an ascending supported elevator (detected at an exact static collision event).
2. A descending overhead moving collider closes the height available over an immovable floor while the player is grounded.
3. Two moving vertical platforms close around a supported player.
4. A moving side surface closes a horizontal gap against a static wall.
5. Two moving side surfaces close a horizontal gap around a grounded player.

Horizontal and vertical closing-gap checks require positively overlapping orthogonal spans, a gap that starts large enough to contain the player, and an actually decreasing gap that reaches the player dimensions. A player moving completely out of an overhead platform's horizontal footprint is **not** prematurely classified as crushed. An ordinary descending-platform side collision without an opposing surface remains a standard blocking collision.

This is **Phase 1** rather than the whole Milestone 26. Multi-contact manifolds, obstacle constraints for platform trajectories themselves, sliding/forced displacement across multiple moving bodies, and true contact-time calculation for every multi-platform configuration still require further integration. The closing-gap classifiers check one analytical interval's endpoints; motion containing a moving-platform reversal may need to be split into monotone legs in the next pass. This work must not be described as a complete multi-platform crushing/obstruction solver.

## Install

Extract the ZIP over `C:\ProgrammingProjects\MonoGame_Projects\MonoGame_Platformer\PlatformerSandbox` and allow replacements. Build the solution, run all xUnit tests, and test a ceiling crush in Strict mode. The window should not terminate from the classified crush; it should respawn and increment `CRUSH RESPAWNS`. Other unexpected exceptions must still propagate. For a test that intentionally preserves the typed exception, construct `MovementGymSession(crushPolicy: MovementGymCrushPolicy.Throw)` or change it with `SetCrushPolicy`.

## Regression suite

`Milestone26CrushOutcomeTests` checks the above independent squeeze configurations, stable collider IDs, absence of false crush on a harmless side contact, transactional rollback, and basic input validation. `Milestone26CrushPolicyTests` verifies that gameplay catches only the typed outcome, respects a debug throw policy, and keeps crush policies separate from fault modes and movement tuning.

**Validation limitation:** The package source was inspected for structure and archive integrity here. There is no .NET SDK in the build environment, so compilation and tests must be run in the user's Visual Studio installation before this phase is declared complete.
