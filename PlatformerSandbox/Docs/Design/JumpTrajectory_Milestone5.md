# Milestone 5 — Constraint-based vertical jump trajectories

These files implement the first supported analytical reference-trajectory family for A06/A07.
They deliberately do not instantiate a character, modify position in a game world, or handle collision.

## Authoritative designer inputs
- `Height`: maximum vertical distance above takeoff (positive-up).
- `RiseTimeSeconds`: elapsed time from takeoff to apex.
- `FallTimeSeconds`: elapsed time from apex back to the takeoff height in an unobstructed reference simulation.
- `ApexRegionFraction`: fraction of height below apex that counts as *near apex*. `0.10` means top 10%.
- `TimeNearApexSeconds`: optional duration within that region, summed over ascent and descent. **Not a literal pause.**

## Exact profile family
With height H, rise time Tr, fall time Tf, exponent p >= 2:

- Rising: `y(t) = H * [1 - (1 - t/Tr)^p]`, for `0 <= t <= Tr`.
- Falling: `y(t) = H * [1 - ((t-Tr)/Tf)^p]`, for `Tr < t <= Tr+Tf`.

Velocities and accelerations are the analytical derivatives of these expressions.
The default p=2 reproduces constant-acceleration rise and fall.

Time in the apex region is `(Tr+Tf) * fraction^(1/p)`.
If specified, the solver obtains `p = ln(fraction) / ln(hang / (Tr+Tf))`.

This **single curve family cannot represent every possible set of constraints**. It uses p>=2 to keep acceleration finite at the apex, which means near-apex time cannot be shorter than `(Tr+Tf)*sqrt(fraction)` without selecting a different profile family. The solver rejects such requests instead of silently changing constraints.

## Boundaries and future work
- The reference curve is only defined through return to launch height. At that time velocity is still downward; no implicit landing or ground contact is applied.
- A runtime motor must handle collisions, platform motion, early release, and interruptions through A03/A07/A08. Actual trajectories can diverge from the designed reference.
- Horizontal displacement requires a reference input and horizontal movement model and is intentionally not yet part of this solver.
- Literal apex *holding* (a motion phase with exactly zero vertical velocity), minimum jump height, jump cancellation, other curve families, and editor visualization are future extensions.
- Current implementation validates finite input, duration, apex-region width, feasible hang time, and finite derived motion values. Real-world tuning ranges and user-facing diagnostic messages can be expanded later.

## Installation
Extract into the root of the existing `PlatformerSandbox` solution. These are only additive source, test, and documentation files; no `.csproj` or solution changes are necessary. Build and run tests locally in Visual Studio / .NET 10.
