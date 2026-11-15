# Milestone 26 Phase 1 — Crush respawn → elevator re-landing ride handoff

## Reproduction

In the combined gym, a legitimate vertical crush was classified and respawned. After returning to and jumping toward the elevator, `MovingPlatformRideSolver.Step` failed its strict initial-support contract: `body.Bottom == support.Bounds.Top`. The screenshot shows the body and platform surfaces at nearly equal high-precision coordinates. This is consistent with slightly different floating-point evaluation orders across the analytical jump-contact and moving-platform sample times.

## Correction

- Retain `MovingPlatformRideSolver.IsSupported`'s exact contract.
- At a **confirmed moving-floor landing** (`Y`-positive contact with that collider), align the landing body to the platform surface evaluated at the landing event's world timestamp, but only when the discrepancy fits a bounded floating-point ULP budget.
- Before subsequent **already supported** ride phases, normalize only the same representational drift, including changes in addition order around a platform turnaround.
- Reject lost horizontal coverage, real gaps, and any alignment that would overlap static or other moving collision geometry.
- Continue the existing transactional controller step: no failed step publishes partial state.
- Keep crush respawns separate from numerical-fault fallback and keep strict ride validation unchanged.

## Validation

`Milestone26RideHandoffRegressionTests` covers sub-ULP supported-phase drift, rejection of meaningful gaps, jump → moving-platform landing → sustained riding, and classified crush → new motor → jump → landing. Previous Milestone 25 exact-support and Milestone 26 crush tests should remain unchanged.

The archive was checked structurally, but cannot be compiled in this packaging environment. Build and run the full .NET 10 suite in Visual Studio, then recreate the original sequence in Strict (F5) mode.
