# Milestone 23 hotfix: repeated zero-time floor contact

## Observed failure

`CharacterMovementController.StepFreeOrGround` exceeded `_maxContactEvents` (8) after recording nine identical contacts: collider 7; time 0; outward normal `(0,1)`; incoming/outgoing velocity `(9,0)`. No time advancement or state change occurred.

## Cause

The controller acquired moving floor support only if `RelativeVelocityAtImpact.Y < 0`. The detector legitimately returns a zero-time floor event when the body begins flush with the top surface, has zero vertical velocity, and gravity would subsequently accelerate it into the floor. The controller previously classified that event as *not landed*, so the next iteration detected it again.

## Fix

Acquire support for non-rising top contacts (`relative Y <= 0`) only when `MovingPlatformRideSolver.IsSupported` confirms exact top alignment and positive horizontal overlap. The motor then switches to the already-existing moving-floor ride path for the remainder of the update. Real upward departures, side contacts, and underside contacts are not classified as landings. Do not increase or disable the collision-event limit.

## Regression coverage

`MovingPlatformRestingContactRegressionTests` reconstructs the precise transient contact state using test-only reflection, without exposing an unsafe state mutation API to game code. It verifies that a zero-time floor hit produces one contact and support, that the clock advances, and that rising motion does not attach to the floor.

## Scope and verification

Only `CharacterMovementController.cs` is modified in production code. No project file or package changes. This addresses the proven repeated floor event but does not replace the planned Milestone 24 unified static/moving scheduler and fault sensitivity policy.

Build and run all tests in Visual Studio. The C# compiler is not available in the packaging environment.
