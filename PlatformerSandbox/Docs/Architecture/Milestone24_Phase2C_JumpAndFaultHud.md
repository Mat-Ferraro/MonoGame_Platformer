# Milestone 24 — Phase 2C checkpoint: analytical jump arbitration, transactional jump state, and visible fault HUD

## Install

Extract the archive into `C:\ProgrammingProjects\MonoGame_Projects\MonoGame_Platformer\PlatformerSandbox` and merge the matching paths. Rebuild the solution, run all xUnit tests, and launch DesktopGL.

## Included changes

* `SteerableJumpMotor` uses `UnifiedCollisionEventScheduler.SweepAnalyticalFirst()` to choose the earliest static **or** moving-platform collision for each already-split analytical jump phase. Wall collisions still leave designer vertical reference intact, while ceiling/floor collision interrupts it. The motor continues to own collision response.
* The controller now forks the active jump motor before calling `Step`; it commits the fork only after the parent update succeeds. This avoids partial child progress if the remainder of the update fails. Dependencies shared by the fork are read-only.
* A persistent, clearly labeled fault-mode HUD is drawn **inside** the playable gym, just below the four movement-state lights. It is independent of the application window title, updates immediately after F5/F6/F7, and shows the number of recoveries on the last successful update. A tiny built-in bitmap font avoids new assets or MonoGame Content registrations. Text descriptions are testable through a gameplay-only helper.
* New headless xUnit regression tests cover status labels, mode preservation on reset/tuning, jump partitioning, wall-contact jump height, and invalid step transactional behavior.

## Manual verification

1. F5 → in-window label `FAULT: STRICT` (red). F6 → `FAULT: DIAGNOSTIC` (amber). F7 → `FAULT: RESILIENT` (teal).
2. F1/F2/F3 still change movement *presets*, not fault modes; the fault HUD must not change.
3. R resets the player but preserves the selected fault mode.
4. Jump into the static wall and the moving platform side; normal jump height and air steering must be preserved.
5. Land on, ride, and jump off the moving platform around direction reversals.
6. Run all existing xUnit tests plus the added regression tests.

## Scope still pending within Milestone 24

This is an **integration checkpoint**, not universal release-safe exception containment. Ordinary airborne motion uses the unified accelerated stepper and the active jump now uses unified analytical *contact selection*. Ground locomotion and platform riding retain their existing event loops. Neither arbitrary exception swallowing nor checkpoint respawn is included. The next phase is to unify the remaining event loops, add deterministic long-running stress traces and controlled fault injection, then implement rollback/fallback only for explicitly classified recoverable failures. Unrecoverable penetration/collision-budget errors still throw in every sensitivity mode.

No SDK is installed in the authoring environment, so source and archive checks do not substitute for the real .NET 10 compile and xUnit run in Visual Studio.
