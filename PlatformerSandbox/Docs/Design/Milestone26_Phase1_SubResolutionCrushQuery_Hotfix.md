# Milestone 26 — Phase 1: sub-resolution crush-query ordering hotfix

## Reproduced failure

The existing Milestone 24 10,000-step Responsive-preset replay fails at frame 338, absolute time ~3.9 s, with `ArgumentOutOfRangeException(durationSeconds)` from `OpposingSurfaceCrushDetector.Validate`.

The controller had already calculated an extremely short local phase, but called `AboveStaticGround` and `AcrossClosingSideSurfaces` before its existing test for nonadvancing world time. The public crush classifier correctly requires a representable time interval; its caller violated that precondition.

## Change

- Move **both** prospective static-ground crush checks behind the existing `elapsed + advance` / `worldNow + advance` progress guard in `CharacterMovementController.StepFreeOrGround`.
- Keep that guard's exact speed-cap, reversal, ground-support departure and terminal-remainder semantics unchanged.
- Keep the crush classifier's strict input validation unchanged. Do **not** increase any tolerances, change movement equations, or hide exceptions via gameplay fallback.
- Add a focused deterministic regression replay through the previously failing frame and a test retaining the public detector's invalid-duration rejection.

Only the controller source file is replaced. The test file is new; no existing tests are edited.

## Verification

Extract at the project root. Run all tests; in particular, verify `Milestone24StressAndFaultModeTests.TenThousandFixedStepsAreReplayDeterministic(presetIndex: 1)` and `Milestone26SubResolutionCrushQueryTests`. Afterwards test a genuine elevator crush in Strict mode to ensure it still triggers a classified crush event and gameplay respawn.

ZIP and source text were checked, but .NET compilation/tests are not available in this container.
