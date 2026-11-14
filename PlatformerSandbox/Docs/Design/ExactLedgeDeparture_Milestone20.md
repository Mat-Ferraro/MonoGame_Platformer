# Milestone 20 — Precise Ledge Departure

## Objective

Eliminate the full-physics-segment delay between a supported character crossing a
platform edge and gravity taking effect. Keep the authoritative simulation
analytical, headless, and deterministic. Do not alter jump-height, release,
collision-response, coyote-time, or movement-tuning defaults.

## Problem before this milestone

`CharacterMovementController.StepFreeOrGround` sampled support only after an
entire horizontal acceleration segment. It applied zero vertical acceleration
through that segment whenever the character was grounded at its beginning.
For example, a character that crossed a platform edge at t = 0.25 seconds
inside a t = 0.30 second update did not fall for the last 0.05 seconds.
The next update started gravity late. Frame partitioning could therefore
change the vertical outcome.

## New analytical event

`GroundSupportDepartureSolver.FindFirst` takes a current static AABB, horizontal
velocity, horizontal acceleration, interval duration, and static colliders.
It returns the earliest instant when the character ceases to have horizontal
support at its current floor elevation, or null if none occurs.

- A floor supports the character when the character bottom is on its top and
  their horizontal ranges have positive overlap.
- The right support boundary is reached when `body.Left == floor.Right`;
  the left boundary is reached when `body.Right == floor.Left`.
- The character can bridge adjacent floors or gaps narrower than its own width.
  The solver merges their support intervals deterministically.
- Different-height platforms are not merged.
- Motion uses `dx = v_x*t + 0.5*a_x*t²` and an analytical boundary root. When
  horizontal velocity reverses, the solver checks both monotonic halves.
- A computed boundary can be aligned by at most 1e-9 world units to avoid
  falsely retaining ground support due to floating-point root error. No
  broad penetration correction or hidden ledge assistance is introduced.

The movement controller now advances to this event, updates the supported
state, and integrates the rest of the same update with gravity and the AIR
horizontal profile. Analytically defined horizontal acceleration changes
remain explicit update boundaries.

### Worked scenario

The character begins at x=0 with width 1 and an initial speed of 0. Ground
acceleration is +50 units/s² until t=0.2, followed by speed 10 units/s.
The floor ends at x=1.5 (this is the character's LEFT edge exit location).
The character reaches x=1 at t=0.2 and exits the floor at t=0.25.

For a 0.3-second update and gravity -20 units/s², the remaining 0.05 seconds
of unsupported movement produces `vy = -1` and vertical displacement `-0.025`.
The previous implementation reported zero vertical movement for this update.

## Scope and non-goals

- Static axis-aligned floor geometry only; moving-platform support/inherited
  velocity comes in a separate milestone.
- This solver determines actual geometric support loss, not coyote time or
  other accessibility/collision assistance.
- Gameplay's existing coyote-time timer is still recorded at fixed-update
  boundaries rather than using an exact mid-update timestamp. Improving its
  timestamp precision can be handled separately, without moving gameplay
  input policy into Simulation.
- The stepper may internally process a landing within a phase. Ground-
  profile replanning after that landing still occurs at the next analytical
  phase or update boundary. This milestone focuses on *departures*.
- The current motor is not an ability-coordinator or a moving-platform system.

## Files

1. `PlatformerSandbox.Simulation/CharacterMotor/GroundSupportDeparture.cs`
   (new data record)
2. `PlatformerSandbox.Simulation/CharacterMotor/GroundSupportDepartureSolver.cs`
   (new independent geometric boundary solver)
3. `PlatformerSandbox.Simulation/CharacterMotor/CharacterMovementController.cs`
   (replace; event-aware ground departure within the ordinary-motion path)
4. `PlatformerSandbox.Tests/Simulation/CharacterMotor/GroundSupportDepartureSolverTests.cs`
   (new tests for geometry, roots, integration, and partitions)
5. `PlatformerSandbox.Tests/Simulation/CharacterMotor/CharacterMovementControllerTests.cs`
   (replace; updates the single old test asserting the known delayed behavior)

No changes to project files, graphics, existing jump trajectories, collision
policies, or tuning presets are required.

## Validation in Visual Studio

1. Extract the archive at the existing `PlatformerSandbox` root.
2. Replace the two existing `.cs` files listed above, preserving all others.
3. Rebuild the full solution; run every xUnit test.
4. Launch the MonoGame movement gym. Walk slowly and quickly off both sides of
   the pit (x=6 to x=8) and confirm the character begins dropping immediately
   after leaving support, including at the Snappy preset.
5. Check jumping, wall sliding, early-release jumps, coyote time, buffering,
   and the three movement presets for regressions.

The archive was inspected and mathematical examples independently checked,
but it has not been compiled on the user's Windows/.NET environment.
