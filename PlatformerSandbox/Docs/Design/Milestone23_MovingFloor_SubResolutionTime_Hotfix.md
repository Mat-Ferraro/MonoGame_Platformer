# Milestone 23 — Moving-floor sub-resolution time boundary hotfix

## Failure reproduced in the movement gym

The moving-floor path inside `CharacterMovementController.StepFreeOrGround` subdivides a fixed update at horizontal speed and reversal events. As the rider approaches its target speed or the end of braking, the remaining analytical phase can be positive but **smaller than one representable increment of the absolute simulation time**. Passing that duration to `MovingPlatformRideSolver.Step` raises `ArgumentOutOfRangeException(nameof(durationSeconds))` as designed. A reported example had `startTimeSeconds ≈ 3.6111111111111032` and `durationSeconds ≈ 1.60365548e-18`.

The previous sub-resolution fix covered the static/free-fall path, but not the riding path. F1 and F3 use different rates; that changes *when* the floating-point boundary occurs, not whether one preset is valid.

## Fix

- Keep the pure moving-floor solver strict: invalid/unrepresentable durations remain errors when passed directly.
- In the **controller**, when a known cruise or reversal transition is sub-resolution, set the exact boundary *relative* velocity and re-plan without moving the clock.
- If only an unrepresentable terminal remainder remains, end the update without claiming additional physical displacement.
- On a jump landing at the very end of an update, do not invoke the ride solver for an unrepresentable residual. The landing contact already established support.
- Leave all movement-tuning values, the analytical platform trajectory and the collision detector unchanged.

## Verification

1. Build the full six-project solution and run all xUnit tests.
2. Run the playable gym in F1 and F3 for several minutes, riding and reversing direction, accelerating/braking/reversing while riding, landing near update boundaries, and jumping repeatedly.
3. Ensure the character remains supported without penetrating the platform or unexpectedly changing horizontal velocity.
4. The upcoming unified scheduler (Milestone 24) should consolidate these clock-progression rules to prevent duplicated fixes in separate motor branches.

These four new tests check the precise micro-phase boundary, near-stop reversal, retained low-level validation, and repeated fixed updates in two tuning profiles. This package is limited to Milestone 23 stabilization, not Milestone 24 collision-event unification.
