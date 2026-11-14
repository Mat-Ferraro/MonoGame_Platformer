# Milestone 17 — Variable-height jump input in the playable gym

## Goal
Connect the already-implemented A06 early-release and complete-flight solvers to the existing steerable kinematic motor. **Tap Space** for a shorter jump; **hold Space** for the full designed reference jump. This is a fixed-simulation-step input event, not a new numerical physics model.

## Inputs and authority

- DesktopGL `Game1` polls the keyboard and emits a **Space pressed edge** and **Space released edge** into `MovementGymSession`.
- Gameplay forwards those events to `CharacterMovementController`, which remains the authoritative owner of character position, velocity and grounded state.
- `SteerableJumpMotor` can accept a release while its held reference is rising. It constructs `JumpFlightSolver.ForEarlyRelease(...)` with the original held `JumpTrajectory`, the designer's `JumpReleaseConstraints`, and the *exact elapsed reference time at the start of that simulation step*.
- Cut profile position and velocity are continuous at the event. The new analytical acceleration applies immediately after release.
- The release is **one-shot**: another release during the same jump has no effect. Releasing during descent, after ceiling contact, or while grounded has no effect.
- X air steering and X wall collision constraints stay independent of the Y reference trajectory.
- A collision normal with nonzero Y still ends reference vertical movement; the motor resumes ordinary gravity or lands.

## Configuration

The first gym uses an illustrative `MinimumHeight = 1.10` world units against a held jump height of `3.7` units, without changing its ground/air acceleration profiles or the existing gravity. This is explicitly a *reference minimum*: it is reached on an immediate release at takeoff in an unobstructed jump. Later releases can reach a taller peak. The default release descent policy preserves the reference fall law.

## Installation

Merge ZIP contents into the solution root `C:\ProgrammingProjects\MonoGame_Projects\PlatformerSandbox`. Replace the five existing source files and add the two new test files and this design note. No project-reference or package changes.

Build, run all xUnit tests, then run DesktopGL. Compare a short tap versus a long hold. Verify midair steering, wall contacts, ceiling cancellation, landing, and ability to jump again.

## Deliberate boundaries

- Input edges are observed only at the 60 Hz simulation update boundary; sub-frame event timestamps/input buffering are not yet implemented.
- Press and release can occur between consecutive updates; if both happen entirely between polls, keyboard polling cannot observe them. Input buffering is a later layer.
- The original reference apex-hang curve and early-release cut curve are mathematically distinct but meet continuously in position and velocity at the release event.
- Contact assistance, coyote time, jump buffering, wall jumps, moving-platform carry, and exact ledge-leave subframe timing remain future work.
- Ground/air acceleration and stopping times remain unchanged; the "too much momentum" feel is intentionally deferred to a later tuning pass.
