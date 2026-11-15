# Milestone 25 — Vertical Moving Platforms and Elevators

**Status:** implementation package; build and tests require local validation.  
**Target:** `PlatformerSandbox` (.NET 10 / MonoGame, framework-independent simulation).  
**Depends on:** verified Milestone 24 unified static/moving collision scheduling.

## Scope and ownership

The existing `MovingPlatformTrajectory` already supports Y travel, turnarounds and exact platform velocities. The same `MovingPlatformRideSolver` uses platform-relative horizontal displacement and exact vertical carry. Milestone 25 enables that path in the authoritative `CharacterMovementController`, adds vertical takeoff velocity to the analytical jump, and exposes a runnable vertical-elevator fixture in the gym. No transform parenting is introduced.

The character has **one** current moving support. Press **F8** to switch between the existing horizontal platform and an elevator test scene. Both use stable collider ID 7, *but only one exists in a given scene*. Multiple simultaneous platforms, crushing, obstruction policies and ceiling-induced platform stoppage remain Milestone 26. The demo elevator is unobstructed and sits in the existing pit.

## Runtime rules

1. **Ride / turnaround.** A supported character's bottom is the platform's current top. The solver recomputes the exact platform position and world velocity, including at each reversal; left/right movement is platform-relative.
2. **Ordinary departure.** Walking past the supported horizontal extent interrupts the ride at the support boundary. Existing world-space velocity (including platform Y) carries into free flight. Gravity then applies in the remainder of the update.
3. **Jump takeoff.** The designer's vertical reference remains relative to the launch frame. On takeoff the current vertical platform velocity is inherited **once**: `worldY(t) = takeoffY + referenceDisplacement(t) + platformTakeoffVelocityY * t`. The corresponding velocity is `referenceVelocityY(t) + platformTakeoffVelocityY`. Platform reversals after takeoff **do not** move or re-parent the airborne character.
4. **Early release.** A release is evaluated against the *relative* jump's ascent, not its world vertical speed. It retains the additive launch-velocity contribution.
5. **Collision detection.** The jump sweep is split at platform turnarounds and analytically found vertical-direction changes, including world and platform-relative reversals. Landing uses the existing unified moving-collider collision query; a valid top-surface landing transfers ownership to the ride solver.
6. **Faults.** Configurable Strict / Diagnostic / Resilient settings remain as in Milestone 24. A moving platform pushing a supported character into an immovable ceiling is **not** silently resolved by this milestone; crushing and safe obstruction handling are deferred to Milestone 26. Keep the strict no-overlap and event-limit guards.

## Playable gym

- **F8:** switch platform fixture: `Horizontal` ↔ `Elevator`. This resets the player and active platform clock; the selected movement preset and fault sensitivity remain unchanged.
- **Elevator:** character starts standing on a lift at the existing pit (`x = 6.2..7.7`, top `y = 0`). The lift travels **3 world units upward** in **2 seconds**, then returns, repeatedly (1.5 units/s). It is gold, like the horizontal platform. Platform mode is labeled in the HUD and window title.
- Other controls are unchanged: A/D, Space, R, F1–F3, F5–F7, P.
- The gym uses one platform at a time to keep the Milestone 24 regression baseline and Milestone 26 multi-platform scope separate.

## Verification

Rebuild the solution and run all xUnit tests. The package updates the old test that intentionally expected vertical supports to be rejected, because that rule is now obsolete. New tests cover upward/downward carrying, takeoff speed inheritance, falling onto a rising lift, turnaround at takeoff, reset/mode changes, deterministic replay, and repeated support over 60 Hz updates.

Recommended interactive checks: choose F8, ride several full periods, jump while ascending and descending, reverse horizontal input in midair, tap and hold Space, walk off the lift, and switch back to F8 horizontal to check the existing fixture. Repeat with F1 and F3 and each fault mode. Do not treat an obstructed elevator/crushing test as supported yet.

## Installation

Extract into `C:\ProgrammingProjects\MonoGame_Projects\MonoGame_Platformer\PlatformerSandbox` and replace the included source/test files. No `.csproj`, solution, NuGet or content-pipeline change is needed.
