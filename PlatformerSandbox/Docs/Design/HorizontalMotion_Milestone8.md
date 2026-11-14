# Milestone 8: Analytical horizontal movement

## Scope

This milestone adds a framework-independent horizontal **reference motion** system.
No MonoGame APIs or input-device polling are involved. It operates on a single
positive-right axis and returns position, velocity, and acceleration without
mutating a character, resolving a collision, or deciding which locomotion mode
is active.

## Designer constraints

| Parameter | Meaning |
|---|---|
| `MaximumSpeed` | Maximum movement speed (world units per second) |
| `TimeToMaximumSpeedSeconds` | Time to reach full speed from rest with sustained full directional input |
| `TimeToStopSeconds` | Time to stop from full speed when input becomes neutral |
| `TimeToReverseSeconds` | **Total** time to go from +full speed to -full speed with sustained opposite input (or vice versa) |

All settings are positive, finite. The last must exceed `TimeToMaximumSpeedSeconds`
for this two-stage reversal family.

## Analytical equations

Let `S` be maximum speed, `T_a` acceleration time, `T_s` stopping time, and
`T_r` full reversal time. Derived acceleration magnitudes are:

- `a_accel = S / T_a`
- `a_stop = S / T_s`
- `a_reverse_brake = S / (T_r - T_a)`

A full reversal brakes to zero over `T_r - T_a`, then accelerates in the new
direction over `T_a`. The policy intentionally allows reverse braking to differ
from neutral-input stopping. A previous velocity larger than `S` is smoothly
reduced with `a_stop`; it is not silently clamped.

Each phase is constant-acceleration, with the established A07 equations:

`x(t) = x0 + v0*t + 0.5*a*t*t`

`v(t) = v0 + a*t`

Once the target is reached, the trajectory cruises at constant speed. The
phase boundaries are calculated directly; no Euler/Verlet numerical integration.

## API and integration

1. `HorizontalMotionSolver.Solve(constraints)` validates inputs and derives rates.
2. `profile.CreateTrajectory(x0, vx0, direction)` creates an immutable trajectory
   for **constant** digital direction `Left`, `Neutral`, or `Right`.
3. `trajectory.Evaluate(t)` returns a `HorizontalMotionSample` at any finite,
   nonnegative elapsed time.
4. When directional input or permitted movement changes, the future runtime
   movement controller constructs a new trajectory from the current authoritative
   position and velocity. Collision responses are handled by A03.

Different ground and air movement behavior is obtained by creating distinct
instances of `HorizontalMotionProfile` with different constraints. A09/A10
locomotion/ability logic will choose and coordinate the appropriate profile;
this library does not choose it.

## Deliberate limits

- Full-strength digital left/right/neutral input only. Analog input, variable
  input strength, external modifiers, and time-varying input policies come later.
- Free, unobstructed reference motion. Contact, slope, platform inheritance,
  damage impulses, and scene state are not handled here.
- Jump horizontal *distance* is not a globally fixed value under arbitrary
  directional changes. A06 will eventually express it under an explicit
  reference input sequence and vertical flight duration.
- Analytical trajectories and phase transition times can be calculated using
  floating-point arithmetic; this does **not** imply cross-platform bit-exactness.
- Samples at transition times select the newly active acceleration. Position and
  velocity are continuous across transitions.

## Verification

`PlatformerSandbox.Tests/Simulation/Motion/HorizontalMotionSolverTests.cs` and
`HorizontalMotionTrajectoryTests.cs` cover acceleration, cruising, stopping,
reversal, initial overspeed, changed input, substep invariance, and invalid
constraints.

Extract this package into the existing solution root. All files are new and
require no project-reference changes or NuGet packages. Build and run all
xUnit tests in Visual Studio to verify.
