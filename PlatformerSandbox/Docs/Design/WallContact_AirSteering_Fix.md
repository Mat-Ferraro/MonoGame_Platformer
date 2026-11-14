# Milestone 17 — Air Steering and Wall-Contact Jump Fix

## Observed gameplay issues

1. The Milestone 15 controller creates a fixed horizontal input schedule when the jump starts; subsequent air-input changes do not affect it.
2. A wall collision during the initial ascent interrupts the **entire** reference jump. The old `JumpKinematicMotor` then advances under a separate constant gravity, which may be much weaker than the deceleration implied by the configured power-curve jump. In the Milestone 16 gym, this causes a large unwanted vertical boost.

## Fix / ownership

- `SteerableJumpMotor` owns one active jump. It combines independently authoritative vertical and horizontal motion, with a single final bounds and velocity state.
- Y follows the exact `JumpFlightTrajectory` during unobstructed ascent/descent. X is evaluated from `HorizontalMotionProfile` anew whenever input changes. Both functions are analytical.
- The new detector accepts a pure mathematical curve on an interval where each coordinate is monotone. Intervals split at horizontal speed changes/reversals and vertical apex/end events. It locates collisions by continuous face-root isolation, **not** a frame-to-frame straight sweep.
- X-normal wall contact zeroes inward X velocity, constrains continued inward input while the wall is still alongside the body, and leaves the vertical jump reference unchanged. There is no wall-jump ability or vertical impulse.
- Y-normal ceiling contact stops upward velocity and begins a fresh gravity segment. Y-normal floor contact ends the jump and returns grounded state.
- The pre-existing `JumpKinematicMotor` and reference collision planning APIs remain intact for independent tests and other uses. `CharacterMovementController` delegates active jumps to the new steerable motor.

## Intentionally outside scope

- Jump-button early release / minimum height remains a separate gameplay-input milestone.
- Collision assistance, wall jumps, coyote time, variable air control curves and moving platforms remain separate policies.
- Ground locomotion resumes on the next fixed update after a mid-step landing. Exact ledge-departure timing remains pending.
- Static AABBs, slide-only responses, and independently testable MonoGame-free simulation remain the current constraints.

## Verification

Rebuild the solution and run all existing xUnit tests plus the new `SteerableJumpMotorTests`. In the movement gym, hold right and jump while pressed against the wall; the highest Y should match the normal jump. Reverse direction in midair and release the direction key to test air braking.
