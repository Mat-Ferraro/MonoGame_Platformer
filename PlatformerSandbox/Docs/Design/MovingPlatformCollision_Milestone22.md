# Milestone 22 — Moving-platform collision queries and landing/riding composition

## Scope

This is the collision/interaction foundation for A05 and A07. It builds on the
Milestone 21 `MovingPlatformTrajectory` and `MovingPlatformRideSolver`, and the
Milestone 12 `ParabolicAabbCollisionDetector`.

**New API:**

- `MovingPlatformCollisionDetector.SweepFirst(...)`: finds the first contact
  along a constant-acceleration world-space body trajectory against any number
  of separately moving, periodic ping-pong platforms. Returns an immutable
  `MovingPlatformSweepHit` with world-space body/platform bounds and velocities.
- `MovingPlatformInteractionSolver.Step(...)`: for one moving platform, proposes
  a pure state transition for (a) airborne motion, (b) an airborne landing
  followed by platform-relative riding, (c) an initially supported rider, or
  (d) stopping at a non-floor contact or exact support departure. It never
  mutates a character controller.

All source files are additive. No change to existing `.csproj` files,
third-party packages, or previous motor behavior is required.

## Analytical model

Over an uninterrupted body segment:

`P_body(t) = P0 + V0*t + 0.5*A*t²`.

On each uninterrupted platform leg:

`P_platform(t) = Q0 + U*t`.

In that leg's platform-relative frame:

`P_relative(t) = (P0-Q0) + (V0-U)*t + 0.5*A*t²`.

The existing parabolic AABB detector can therefore solve every relative
face-crossing without sampling or assuming a stationary moving platform.

At each turnaround, the query splits the interval. The platform's bounds are
continuous and its velocity changes instantaneously; samples at a turnaround
use the new/right-continuous velocity.

A moving platform's surface velocity is subtracted **exactly once**. The
collision query reports the body's **world velocity**, the platform's world
velocity, and their difference, so the authoritative motor can select its
response explicitly.

## Determinism and collision policies

- Input platform IDs must be unique. Exact tied contact times prefer the
  numerically lowest platform ID independent of enumeration order.
- Positive-area starting overlap is a hard error; no hidden penetration
  recovery or collision assistance is performed.
- Face-to-face touches with no inward relative motion do not produce a hit.
  An inward collision at the exact end of a query is reported.
- A bounded face alignment tolerance (default `1e-8` world units) handles
  binary64 root roundoff. The platform motion itself is never snapped to the
  character and the world clock is never rounded.
- A configurable platform reversal-event limit prevents unbounded loops.
- An interaction ends early at a wall/ceiling collision or when support is
  lost; `AdvancedSeconds` tells the caller precisely how much of the requested
  duration was consumed.

## Landing and carry semantics

The single-platform interaction solver is intentionally explicit:

1. A body already supported by the platform rides it, with **relative** X
   velocity and acceleration. An upward-moving body (or one accelerating upward from rest) is not glued to support.
2. Otherwise, find the first relative collision during its exact airborne
   constant-acceleration movement.
3. If no hit, return the uninterrupted airborne endpoint.
4. If a side or ceiling hit, return the first impact and stop. A floor
   contact where the platform has just reversed away is also interrupted
   rather than silently treated as support. The authoritative
   motor must supply the next collision-response policy.
5. If an upward normal (floor) hit, calculate the remaining duration using
   the existing relative ride solver. Horizontal velocity is **preserved in
   world space on landing**; this implies a potentially nonzero horizontal
   velocity relative to a moving platform. There is no unrequested snap to
   platform X speed. Vertical velocity is constrained to the platform while
   supported.
6. If the rider subsequently crosses a platform edge, stop exactly at the
   departure instant; the character motor must integrate the remainder as
   airborne motion. No time is silently dropped.

The caller must not apply platform carry a second time to the returned world
position or velocity.

## Important remaining integration work

This is **not** yet a complete moving-platform-aware `CharacterMovementController`
and does not add moving platforms to the rendered gym. The existing
`CharacterMovementController` still treats its static collider list as static.
Before enabling moving platforms in gameplay, the authoritative motor needs to:

- choose the earliest event among static scenery and all moving colliders;
- select and persist the grounded platform ID and the platform-relative
  horizontal movement state (without transform parenting);
- rerun its analytical trajectory after collision and during the remaining
  fixed-timestep fraction;
- convert takeoff velocity to world space exactly once with
  `MovingPlatformRideSolver.InheritLaunchVelocity`;
- handle moving platforms colliding with static scenery or crushing/pushing
  the character under an explicit deterministic policy;
- support designer-defined power-curve reference jumps (not just parabolic
  free motion) when testing hits against moving platforms.

Adding a visual moving floor *before* those integration steps would give false
confidence: rendering a moving object is not equivalent to a correct moving
collision world.

## Tests

`MovingPlatformCollisionDetectorTests` covers horizontal moving contacts,
relative acceleration, falling onto moving floors, floors moving into a body,
reversals, terminal contacts, pure grazes, stable-ID ordering, invalid inputs,
and bounded event processing.

`MovingPlatformInteractionSolverTests` covers initially supported riders,
vertical/horizontal carry, landing plus remaining time, side impact,
launching upward, precise support departure, and pure/transactional results.

Run the full existing `PlatformerSandbox.Tests` suite in Visual Studio. These
files are written against the Milestone 21 APIs. No .NET SDK is available in
this authoring environment, so a local C# compile and xUnit run are required.
