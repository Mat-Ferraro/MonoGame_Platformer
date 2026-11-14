# Milestone 9: Reference jump travel and horizontal distance constraints

## Purpose

A designer may now specify a target **signed horizontal jump distance** in world
units, given a complete vertical reference flight (Milestone 7) and an explicit
left/right/neutral reference input schedule (Milestone 8). This creates a
complete, independently testable, *unobstructed design reference* with X and Y
samples. It is not a runtime character motor, a platform collision, or an input
polling subsystem.

The implementation uses only our existing, framework-independent Simulation
project. No MonoGame dependency, numerical integration, physics rigidbodies,
additional packages, or project changes are required.

## Forward evaluation

1. Solve a `JumpFlightTrajectory` using the existing Jump solvers.
2. Solve a `HorizontalMotionProfile` using the existing horizontal solver.
3. Define one or more `JumpTravelInputChange` entries. Every schedule must begin
   at **t = 0**, and subsequent entries must be strictly increasing and fall
   strictly before the flight ends. Direction choices are `Left`, `Right`, or
   `Neutral`.
4. Call `JumpTravelSolver.Create(flight, profile, schedule, initialX, initialVx)`.
5. Use `trajectory.Evaluate(t)` to obtain both X and Y displacement, velocity,
   and acceleration. `SignedTravelDistance` is final X minus initial X.

The flight duration is set by the **vertical** reference: a full jump and a cut
jump can travel different horizontal distances with exactly the same input and
horizontal motion rules.

When an input changes at time `t_event`, the new horizontal analytical segment
starts from the **previous segment's exact X and Vx** at `t_event`.
Acceleration can change instantly; X and Vx are continuous. At the event instant,
the new segment is authoritative (right-continuous acceleration). No numerical
integration step or sampling rate is required to produce these results.

### Example: sustained right input

- Full reference jump: height 4, rise time 0.40s, fall time 0.30s; total 0.70s.
- Horizontal motion: full speed 10 world units/s, time to max speed 0.20s.
- Initial X = 0 and initial Vx = 0.
- Reference input: `Right` at t = 0, held for the entire flight.

The reference covers 1 unit while accelerating for 0.20s and 5 more units while
cruising for the remaining 0.50s, for **6 units total**.

If input becomes `Neutral` at t = 0.30s with 0.10s full-speed stop time, the
reference travels **2.5 units** and then stays still horizontally until the
end of the flight.

### Early-release example

For an immediate-release cut jump with minimum height 1, the previously defined
vertical flight duration is 0.25s. Sustained right input under the same
horizontal profile now covers **1.5 units**. This distance is calculated for
that particular cut-jump reference, not for every possible runtime release time.

## Inverse designer constraint: solve maximum horizontal speed

`JumpTravelSolver.SolveForDistance(verticalFlight, constraints, schedule, initialX)`
selects the **maximum horizontal speed** required to achieve the desired signed
reference travel distance. The inputs include:

- `SignedTargetDistance`: positive = right, negative = left, nonzero.
- `TimeToMaximumSpeedSeconds`, `TimeToStopSeconds`, `TimeToReverseSeconds`:
  unchanged duration constraints from Milestone 8.
- `InitialVelocityFractionOfMaximumSpeed`: signed value in [-1, 1].
- The *fixed* schedule, unchanged for the inverse solve.

Why we normalize initial velocity: For these particular constant-acceleration
horizontal profiles, the acceleration/deceleration magnitudes scale with max
speed `S`. If the starting Vx is also proportional to `S`, the entire reference
X displacement obeys:

```
D(S) = S * D(1)
S_required = D_target / D(1)
```

Here `D(1)` is the travel distance for a speed of 1 world unit/s, with exactly
the same timing constraints, initial velocity fraction, schedule, and flight
duration. The solution is algebraic; there is no iterative optimizer or
numerical search. The returned `JumpTravelTrajectory.HorizontalProfile.MaximumSpeed`
is the solved value, and `SignedTravelDistance` is the achieved result.

The engine rejects impossible or underdetermined requests: zero signed target,
no movement under the schedule, a target pointing opposite to the reference
travel, invalid time constraints, nonfinite inputs, or an unsafely represented
numerical solution. When a target is requested, the result is checked against
that target rather than silently accepting a poor floating-point approximation.

**Tradeoff:** A designer cannot simultaneously fix all four of maximum speed,
acceleration time, stopping time, reversal time, *and* an independent jump
travel distance under a fixed schedule and flight duration. This inverse solver
chooses maximum speed as the variable to derive, preserving the other timing
choices. If maximum speed is already a fixed requirement, use the forward
reference and report incompatible distance constraints instead.

## Limits intentionally retained

- The vertical reference is bounded by its solved return-to-launch-height time;
  the endpoint is **not** an automatically resolved landing collision.
- A reference schedule is authored ahead of time. The input mapping, buffering,
  ability coordinator, authoritative motor, interruption from obstacles, moving
  platforms, and collision response are separate planned modules.
- The input model is digital left/right/neutral, not analog strength.
- The inverse solver's linear scaling requires initial horizontal velocity to
  be a fraction of maximum speed. It does not promise linearity for arbitrary
  fixed, unscaled initial Vx or for future nonlinear movement profiles.
- A target distance is **signed X displacement**, not horizontal path length.
  A jump that moves right and then left can have large path length and small
  signed displacement.
- Results are deterministic under the same arithmetic and event inputs, but
  not guaranteed bitwise-identical across different runtime math libraries.

## Verification and installation

The additive ZIP includes two xUnit classes:

- `JumpTravelTrajectoryTests.cs`: forward reference, vertical reuse, stop,
  reversal, continuity, early release, apex hang, event scheduling, immutable
  evaluation, and invalid time input.
- `JumpTravelSolverTests.cs`: inverse distance targets, signed direction,
  changed-input schedules, normalized initial velocity, invalid/underdefined
  constraints and schedules, numeric precision and null validation.

Extract into the `PlatformerSandbox` solution root. No `.csproj`, `.sln`, or
existing source files are changed. Rebuild and run all tests in Visual Studio.
