# Milestone 24 Phase 2C — analytical jump phase-duration hotfix

## Failure

`MovementGymTuningTests.SamePresetAndInputScheduleAreDeterministic()` did not reach its equality assertions. In `SteerableJumpMotor.Step`, the unified scheduler rejected a positive `segmentDuration` for which `worldStart + segmentDuration == worldStart` in binary64. Phase 2C routed analytical jumps through a scheduler with an explicit representable-time precondition; the jump motor had not normalized these tiny boundaries.

## Correction

The jump motor now recognizes a nonadvancing scheduled interval *before* calling the unified collision scheduler. For a recognized analytical phase boundary, it establishes the exact boundary velocity/state and rebuilds the subsequent segment without pretending any measurable time has passed:

- Horizontal acceleration or stopping ends at the profile's exact cruise velocity.
- A reversal's braking phase ends at zero horizontal velocity.
- Relative horizontal velocity extremum matches the platform velocity.
- The apex, early-release boundary, and end of the reference flight use the exact analytical reference state.
- An otherwise uneventful, final sub-resolution remainder closes the step.

Unknown nonadvancing internal events still fault; the collision detector's input validation is **not** weakened. This is deterministic phase normalization, not a general catch-and-ignore strategy. No change to designer motion constraints or fault sensitivity modes.

## Files

- Replacement: `PlatformerSandbox.Simulation/CharacterMotor/SteerableJumpMotor.cs`
- New: `PlatformerSandbox.Tests/Simulation/CharacterMotor/Milestone24Phase2CSubResolutionJumpTests.cs`

## Verify

Rebuild all projects; run all tests, especially `MovementGymTuningTests.SamePresetAndInputScheduleAreDeterministic`. Then exercise F1 and F3 running, direction reversals, full and short jumps, and moving platform landings.

The ZIP was inspected but not compiled in the authoring environment; Visual Studio's test runner is the authoritative verification.
