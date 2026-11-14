# Milestone 21 — Moving Platform Kinematics and Rider Support

## Goal and architectural contract

Implement the analytical foundation of A05 before allowing dynamic colliders
into the authoritative character motor. The character is **never parented**
to a moving platform. A platform supplies a motion contribution; the motor
will ultimately be responsible for applying that contribution and resolving
all world-space collisions exactly once.

This milestone is **headless and additive**. It does NOT add moving floors to
the playable MonoGame gym yet, and it does NOT pretend that the previous
static AABB sweep can detect impacts against a moving obstacle.

## New parts

- `MovingPlatformTrajectory`: deterministic, periodic linear path from an
  initial AABB to the AABB displaced by a designer-specified travel offset,
  then back. Independent of MonoGame, input, and frame rate.
- `MovingPlatformSample`: immutable platform AABB and instantaneous velocity.
  At exact reversal instants, velocity is right-continuous (the new direction).
- `MovingPlatformRideSolver`: analytically composes platform displacement with
  a supported character's relative horizontal displacement. Splits at every
  platform turnaround and at the first support-departure event. It returns a
  proposed state; it does **not** mutate a character motor.
- `MovingPlatformRideResult`: proposed world bounds and world velocity,
  whether support remains, and the elapsed time when support is lost.
- `InheritLaunchVelocity`: explicitly adds the platform's instantaneous
  **world-space** velocity **once** to a launch velocity defined in the
  platform's local frame. Vertical inheritance is included in the vector.

## Analytical definitions

Designer constraints: travel vector D and one-way duration T. During a single
leg with elapsed leg time s (0 <= s <= T):

    platform_position = leg_start + leg_velocity * s
    outbound_velocity = D / T
    inbound_velocity  = -D / T

Turnaround events happen at integer multiples of T. Exact endpoint snapshots
are used at these transitions to avoid drift. While supported:

    world_body_displacement = platform_displacement + relative_body_displacement
    relative_body_dx = relative_vx * dt + 0.5 * relative_ax * dt^2
    world_body_velocity = platform_velocity + (relative_vx, 0)

Support loss is determined in platform-local coordinates, reusing the exact
`GroundSupportDepartureSolver` from Milestone 20. It occurs when the body
stops having positive horizontal overlap with its supporting platform.

**Important:** `MovingPlatformRideSolver.Step` stops **at** departure. If the
caller requested 0.5 s but support ended at 0.2 s, the result reports only
0.2 s advanced. The other 0.3 s **must** be processed by the real airborne
character motor, including gravity and collision events. No invented carry
or gravity is applied for that remaining interval.

Similarly, after a jump, do not keep applying platform displacement every
frame. Capture platform velocity at takeoff, add it to the relative launch
velocity *once*, and use the authoritative airborne motor thereafter.

## Examples

A platform starting at x=0 moves 4 units to the right over 2 seconds, then
moves back. A rider with local left x=1 and no relative horizontal movement
will be at x=3 after one second and x=5 at the two-second reversal, with
world horizontal velocity -2 immediately *after* that reversal.

A rider running right at +1 unit/s relative to the same platform will be at
x=4 after one second. The resulting world velocity is +3 units/s while the
platform is traveling right.

If a platform is moving at (+3,-1) units/s and a jump is specified as
(+4,+7) units/s relative to the platform, the resulting initial world
velocity is (+7,+6) units/s. No extra platform delta is subsequently applied.

## Explicit scope boundaries

This milestone implements:

- Analytical platform travel, including diagonal and reversed directions
- Deterministic turnaround events and right-continuous velocity
- Carry motion for a rider **already supported** by one moving platform
- Relative acceleration, braking and departure from either edge
- A pure velocity-inheritance calculation for takeoff
- Validation for invalid geometry, nonfinite parameters and event limits

This milestone does **not** implement:

- Relative swept collision *landing onto* a moving platform from above
- Being pushed sideways/upward by a moving platform or crushed by scenery
- More than one simultaneous supporting surface
- World-space static obstacle collisions during the proposed carry
- Connecting moving-platform support into `CharacterMovementController`
- Rendering or keyboard interaction for moving platforms
- Automated launch of `SteerableJumpMotor` using inherited vertical speed

Those omissions are intentional. Injecting moving AABBs into a detector that
assumes static geometry would give incorrect collision times and violate A03.
The next milestone should build relative moving-body collision queries and
then connect their events and support IDs to the authoritative character
motor. After that, expose a moving platform in the MonoGame gym.

## New files only

- `PlatformerSandbox.Simulation/MovingPlatforms/MovingPlatformSample.cs`
- `PlatformerSandbox.Simulation/MovingPlatforms/MovingPlatformTrajectory.cs`
- `PlatformerSandbox.Simulation/MovingPlatforms/MovingPlatformRideResult.cs`
- `PlatformerSandbox.Simulation/MovingPlatforms/MovingPlatformRideSolver.cs`
- `PlatformerSandbox.Tests/Simulation/MovingPlatforms/MovingPlatformTrajectoryTests.cs`
- `PlatformerSandbox.Tests/Simulation/MovingPlatforms/MovingPlatformRideSolverTests.cs`
- `Docs/Design/MovingPlatformKinematics_Milestone21.md`

Nothing replaces the existing character controller, jump code, configuration,
or host. There are no NuGet or project-reference changes.

## Verification

1. Merge this ZIP into `C:\ProgrammingProjects\MonoGame_Projects\PlatformerSandbox`.
2. Rebuild the solution and run all xUnit tests.
3. Inspect the new MovingPlatforms test classes: they check path endpoints,
   reversal signs, partitions, rider carry, exact ledge departure, velocity
   inheritance, and invalid inputs.
4. The existing playable gym should behave exactly as before; moving floors
   are not yet rendered or interactive.

The archive was source-checked and mathematically reviewed, but the C# build
and xUnit tests still require local verification in Visual Studio.
