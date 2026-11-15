# Milestone 24 — Full Integration: Test Corrections

These corrections address the **three failing assertions** in the uploaded 761-test run (758 passed, 3 failed). **No production code is modified.**

## Corrected tests

- `Milestone24RideArbitrationTests.RideEndsAtStaticWallRatherThanMovingThroughIt`: `MovingPlatformTrajectory` takes a *travel offset*, not a velocity. The intended test speed is 1 unit/s, so the platform now travels 2 units over 2 seconds. The existing wall-contact time and position assertions remain unchanged.
- `Milestone24RideArbitrationTests.UnobstructedRidingContinuesWithPlatformCarry`: same scenario correction. The player should move 0.25 units in 0.25 seconds at 1 unit/s. The position assertion remains unchanged.
- `UnifiedAcceleratedKinematicStepperTests.CollisionEventBudgetIsNeverSilentlyExceeded`: the production code deliberately throws `SimulationEventLimitException`, a subclass of `InvalidOperationException`. xUnit `Assert.Throws<T>()` requires the *exact* type. The test now expects the classified exception and verifies its diagnostic trace contains the first wall collision at t=0.2, normal (-1,0).

## Installation

Merge these files into:
`C:\ProgrammingProjects\MonoGame_Projects\MonoGame_Platformer\PlatformerSandbox`

Replace only the two `.cs` test files. No `.csproj` edits required.

Rebuild, then run the full test suite. **Expected: 761 passed / 0 failed**, pending local C# verification. If gameplay still exhibits any unusual wall or platform interactions, treat those separately as potential runtime regressions; passing tests don't prove everything is resolved.
