# Moving platform turnaround precision hotfix

## Symptom

The playable moving platform can throw `InvalidOperationException: Moving platform event cannot advance time.` in `MovingPlatformRideSolver.Step`, despite the game clock advancing normally.

## Reproduction

The gym's platform reverses every 2.4 seconds. With 1/60-second fixed updates, a split step at the third reversal can calculate an absolute event time of `7.199999999999999` seconds. Modulo gives a phase slightly below the 2.4-second turnaround, leaving an event interval too small to advance that absolute double-precision clock.

A separate case occurs when the relative duration has a tiny positive remainder after the rounded absolute clock has already reached its requested end.

## Fix

* `MovingPlatformTrajectory.Phase` snaps a phase to either analytical turnaround only if it lies within **four ULPs of the absolute clock**, additionally bounded to one 1024th of the one-way duration. Its position and right-continuous velocity evaluation, as well as its next-turn calculation, now agree at representable turnaround boundaries.
* `MovingPlatformRideSolver.Step` consumes an unrepresentably tiny **terminal** relative-time remainder even when the rounded absolute clock is at its endpoint or one ULP earlier. It performs the same analytical horizontal integration and support-departure check rather than spinning or throwing. Interior platform events must still advance absolute time.
* A final full-length segment sets the accumulated relative time **exactly** to the caller's requested duration.

Neither the platform trajectory's speed, its travel extent, the character's acceleration, nor the collision response policy is changed. True nonadvancing internal events continue to throw rather than being ignored.

## Regression checks

`MovingPlatformTurnaroundPrecisionTests` exercises the original 7.2-second failure, an unrepresentable end-of-step remainder, distinct pre-turn events, and 20 seconds of 60 Hz riding across multiple reversal periods.

This fix should be validated by running the full xUnit suite and riding the gold platform in DesktopGL across several direction reversals.
