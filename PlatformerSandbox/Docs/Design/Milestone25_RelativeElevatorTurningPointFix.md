# Milestone 25 — Rising Elevator Analytical Collision Turning Point

## Reproduction

While jumping toward the rising F8 elevator in the playable gym, Visual Studio
stopped inside `UnifiedCollisionEventScheduler.SweepAnalyticalFirst` with:

> Relative analytical motion turns within a moving-platform interval.
> Split the input at the relative velocity-zero event. (Parameter 'evaluate')

The jump controller already splits world-space apex / law-change events and
attempts to split when its Y speed equals the platform Y speed. Binary64
rounding at the isolated crossing can nevertheless place endpoint samples
on opposite sides of that equality. Rejecting the entire sweep turns an
ordinary jump into an unhandled exception even when the lift is not touched.

## Fix

The shared scheduler now isolates any *interior* relative X/Y velocity-zero
event on each continuous-velocity platform leg, and sweeps each resulting
monotone interval with the existing analytical AABB face-crossing detector.
The analytical collision solver and response physics have **not** been
replaced by discrete sampling. Platform direction reversals remain separate
named events. The earliest hit and stable collider-ID tie policy are retained.

The supplied world motion still needs to be monotone between the calling
motor's own apex / acceleration / release / reversal events. As before,
generic arbitrary functions with undisclosed multiple hidden velocity
reversals are not supported by the analytical sweep's contract.

## Regression coverage

- World-monotone but moving-platform-relative X reversal, with and without a hit.
- Rising elevator underside contact before a relative Y reversal.
- Rising elevator landing after a relative Y reversal.
- Deterministic earliest-contact ID when several platforms share a face.
- Real `SteerableJumpMotor` executing a jump next to a rising elevator over
  fixed-step updates and in a larger analytical update.

## Installation

Copy the files from the ZIP into the `PlatformerSandbox` root, preserving paths.
Existing tests are updated to reflect the improved scheduler contract, not
removed or disabled. Run the full xUnit suite, then reproduce the F8 jump in
Strict mode (F5). No fault policy or checkpoint fallback changes are included.
