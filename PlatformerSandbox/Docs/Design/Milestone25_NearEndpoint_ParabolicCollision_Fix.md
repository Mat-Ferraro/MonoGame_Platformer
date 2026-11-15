# Milestone 25 — near-endpoint parabolic collision precision

## Failure

`StrictModeClosesAnAlreadyResolvedNearEndContactWithoutRecovery` asserted that the near-end contact exists, but `result.Contacts` was empty.

Its collider starts at `Math.BitDecrement(6.0)` and the body moves toward it with `vx=10` over `0.5 s`. The face root is `0.4999999999999999`. The midpoint between the face root and the interval end is representable as `0.49999999999999994`, but the body right edge at that midpoint rounds to the wall's left edge, so the strict positive-overlap test returns false. At `t=0.5` the body *does* have positive X overlap. As the detector discarded the candidate interval, it returned no contact and the stepper advanced into the wall.

## Change

Preserve the analytic root and entering-face ordering. If an interval's midpoint does not have positive overlap AND that interval ends exactly at the requested query endpoint, additionally inspect whether the endpoint itself has **positive-area overlap on both axes**. If so, report the original root as the contact. Keep mere endpoint touches and corner grazing excluded; the preexisting endpoint-contact path separately handles moving inward at an exactly touching endpoint.

The original failing stepper test stays **unchanged**. Two additional detector regressions cover (1) a one-ULP endpoint penetration being detected and (2) a one-axis-only corner graze still being ignored.

## Files

- `PlatformerSandbox.Simulation/Collision/Detection/ParabolicAabbCollisionDetector.cs` — corrected endpoint-overlap classification.
- `PlatformerSandbox.Tests/Simulation/Collision/ParabolicAabbCollisionDetectorTests.cs` — adds two regression tests.

This correction does not modify fault policies, platform trajectories, jump physics, overlap tolerances, or existing test expectations. The combined-gym Milestone 25 patch must already be installed.

## Verification

Run the entire xUnit suite, including the original `StrictModeClosesAnAlreadyResolvedNearEndContactWithoutRecovery` test, then exercise the combined gym in Strict mode. C# compilation and execution were not available while packaging.
