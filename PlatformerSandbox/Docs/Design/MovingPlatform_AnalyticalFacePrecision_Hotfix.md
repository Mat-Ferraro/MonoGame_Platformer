# Moving platform — analytical entering-face precision hotfix

## Symptom

The playable moving-platform gym may throw:

`InvalidOperationException: An entering face could not be determined at binary64 precision.`

The stack trace runs through `SteerableJumpMotor.Step` and
`AnalyticalAabbCollisionDetector.SweepFirst` while jumping or landing near
an obstacle or moving platform.

## Root cause and scope

`AnalyticalAabbCollisionDetector` isolates candidate boundary-crossing times
by binary search, then tests the open time intervals for positive-area
intersection. Originally it required an entering-face event time to be **bitwise
equal** to the interval's left endpoint. At binary64 representability limits,
face-crossing time isolation and translating the world-space AABB can round
slightly differently. The overlap interval can therefore be real in floating
arithmetic even when no face event has exactly that timestamp. Previously the
solver treated this as fatal.

## Correction

The original deterministic fast path remains unchanged. Only if exact
event-time matching fails, recover a face from the bounds at the interval
start. A candidate must:

1. Be aligned to the relevant collider face within the **existing**
   `contactAlignmentTolerance` (default `1e-8` world units).
2. Have positive-area overlap along the other axis in the verified interval.
3. Have inward displacement or inward velocity; tangent or separating
   contacts must not count.

The smallest face-position correction wins; exact ties prefer X. The existing
`CreateHit` method still enforces the same tolerance and snaps the reported
contact to the collider face. There is no arbitrary velocity injection,
global epsilon-overlap rule, changed physics law, or exception suppression.
If no valid face exists inside the stated tolerance, the query still throws.

## Regression coverage

The new test file exercises sub-ULP initial gaps, exact-zero-time inward
contacts, parabolic ceiling impacts, end-of-step landings, takeoff away from a
floor, tangential contacts, corner grazes, and moving-platform-relative
landing queries around repeated turn boundaries.

## In-game check

Rebuild and run all tests, then ride the gold platform and jump repeatedly,
especially through turnarounds. Also verify that a wall still blocks inward
motion and does not increase jump height, and that landing and ceiling
contact behave normally.

This patch corrects collision **normal recovery** only. It does not implement
vertical lifts, multiple moving platforms, crushing behavior, or new
movement mechanics.
