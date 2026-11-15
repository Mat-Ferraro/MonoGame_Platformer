# Milestone 25 — authoritative elevator support-plane precision fix

## Reported failure
`VerticalElevatorRuntimeTests.LongRunningElevatorRideRemainsAtExactSupportPlane` failed with expected `3.2115384615384825` and actual `3.2115384615384817`.

## Root cause
The analytical ride solver returns a box with its bottom exactly equal to the platform top at the solver's own sample time. The controller's final clock, however, is committed as `previousElapsedSeconds + durationSeconds`. Several analytical phases can accumulate slightly differently and yield a final ride sample time a few ULPs away from that authoritative clock. Because support equality is intentionally exact, that drift violates the contact invariant.

## Correction
Before committing the controller's next state, if the body is supported by its moving platform:
- Evaluate the platform at the authoritative end time already computed for the update.
- Compare the current feet against that support plane using a bounded, ULP-and-platform-velocity-based rounding budget, capped at 1e-9 world units.
- Reject larger discrepancies via `SimulationNoProgressException` rather than hiding lost support or a physics fault.
- Rebuild the supported body's AABB with its bottom exactly equal to the authoritative support top; refuse to introduce penetration into static scenery.

No time integration, jumping trajectories, collision query ordering, or support departure behavior is changed. This is an end-of-update geometric canonicalization, not a broad recovery or relaxed test tolerance.

## Tests
The reported exact-equality test is unchanged. One new test verifies the same invariant over 1,800 frames with alternating input directions and repeated vertical elevator reversals.

## Installation
Extract the ZIP at the solution root. Replace `CharacterMovementController.cs` and `VerticalElevatorRuntimeTests.cs`; the design note is new. Rebuild and run all tests, followed by the playable elevator mode (F8). Neither `.csproj` nor content assets change.

Note: This package was source-inspected and archive-validated but could not be compiled here because the .NET SDK is not available in this environment.
