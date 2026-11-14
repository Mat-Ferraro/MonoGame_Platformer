# Milestone 23 — Sub-resolution motion-event hotfix

## Observed failure

A moving-platform collision query raised `ArgumentOutOfRangeException` for `durationSeconds` after approximately 20 seconds of simulation. The captured query had `startTimeSeconds = 20.071069360564575` and `durationSeconds ≈ 5.24e-19`. The duration is mathematically positive, but adding it to the world clock returns the same `double`: there is no representable *absolute* time advancement.

## Root cause and boundary

In `CharacterMovementController.StepFreeOrGround`, a horizontal movement phase can terminate an extremely small time after the current state (e.g., the velocity is already within a few ULPs of its target, or nearly zero just before a direction reversal). The controller previously passed that time fragment to `MovingPlatformCollisionDetector.SweepFirst`, which correctly rejects a nonadvancing absolute time range.

This can occur while near a moving platform, but does not require a moving collision. It is a scheduler/event-resolution issue, not proof of an overlap or a physics impulse.

## Correction

Before making a sweep query, the controller checks whether the proposed phase duration advances both the local and absolute clocks. If not, it consumes the *identified* analytical boundary at the current representable instant:

- velocity reaches an exact cruise target; or
- reversal braking reaches zero velocity; or
- static ground support ends at its solved edge.

It then rebuilds the next motion segment, rather than querying an unrepresentable interval. An unrepresentable terminal remainder is consumed without an artificial movement step. Unexplained, nonterminal zero-progress cases still throw. The low-level moving-platform detector retains its strict validation. This patch does not change the configured acceleration, jump, or platform speed values.

## Regression coverage

Three tests exercise (1) a near-cruise phase boundary with the world clock greater than 20 seconds, (2) an equally small reversal-stop boundary, and (3) preservation of strict invalid-range rejection in the collision detector.

## Scope

This is a narrow Milestone 23 stability hotfix. It does not implement the planned Milestone 24 unified collision event scheduler, and it does not address arbitrary initial penetrations, simultaneous multiple-platform collisions, or crushing behavior.

## Verification

Rebuild all projects, run the full test suite, then play the gym for at least 30 seconds while jumping into/onto the moving platform and riding through multiple reversals. Check that the prior corner-contact, landing, and air-steering behaviors remain intact.
