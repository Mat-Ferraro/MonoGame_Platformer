# Milestone 11 — Deterministic collision response and constant-velocity swept integration

## Scope / decisions

Implements a narrow, headless portion of **A01** (authoritative movement), **A03**
(surface-normal response), **A07** (event-aware analytical motion), **A14**
(framework-independent simulation), and **CL-01** (only the stepper owns resulting
position and velocity for this operation).

This milestone deliberately works on **constant-velocity straight segments only**.
It **does not** approximate a whole parabolic or power-curve jump with a straight
segment. That would miss curved-path impacts and violate the intent of A07.
The next work item is continuous collision queries against the actual analytical
acceleration/trajectory profile, followed by a more general motor.

No existing file, project reference, or NuGet package is changed.

## New types

| Type | Responsibility |
| --- | --- |
| `CollisionResponseMode` | Explicit `Slide` or `Stop` behavior |
| `CollisionResponse.Apply` | Pure response: remove inward normal velocity or stop |
| `LinearKinematicContact` | Collider ID, absolute contact time, normal, impact bounds, velocities before/after |
| `LinearKinematicStepResult` | End AABB, end velocity, requested duration, read-only contacts |
| `LinearKinematicStepper.Step` | First swept hit → response → remaining-time continuation |

All types live in `PlatformerSandbox.Simulation`; geometric primitives and
immutable AABBs remain in `PlatformerSandbox.Foundation`.

## Coordinate system and API

The world is **Y-up** (same as jump math). Contact normals point **outward from
obstacle into moving body**, consistent with Milestone 10:

- Wall right of mover: `(-1, 0)`
- Wall left of mover: `(1, 0)`
- Floor: `(0, 1)`
- Ceiling: `(0, -1)`

Example:

```csharp
Aabb player = Aabb.FromCenterAndHalfExtents(
    new Vector2D(0.5, 0.5), new Vector2D(0.5, 0.5));

var obstacles = new[]
{
    new StaticAabbCollider(100, new Aabb(
        new Vector2D(3, -10), new Vector2D(4, 10)))
};

LinearKinematicStepResult result = LinearKinematicStepper.Step(
    player,
    new Vector2D(4, 2),
    durationSeconds: 1.0,
    obstacles: obstacles,
    responseMode: CollisionResponseMode.Slide);

// result.Bounds == Aabb(2, 2)-(3, 3)
// result.Velocity == (0, 2)
// result.Contacts[0].TimeSeconds == 0.5
// result.Contacts[0].Normal == (-1, 0)
```

The code sample assumes `using` directives for:
`PlatformerSandbox.Foundation.Geometry`,
`PlatformerSandbox.Simulation.Collision.Detection`,
`PlatformerSandbox.Simulation.Collision.Resolution`, and
`PlatformerSandbox.Simulation.CharacterMotor`.

## Response rules

- `Slide` removes **only the inward normal velocity** and retains exact tangent
  velocity. It never adds friction, restitution, or outward motion.
- `Stop` zeroes both components of velocity on the first contact and keeps the
  body at the contact point for the remainder of the requested step duration.
- A starting *face touch* moving inward is processed at `t = 0`. Tangential
  motion and moving away are not blocked.
- Exact equal impact times use the Milestone 10 detector's **smallest stable
  collider ID**, regardless of obstacle enumeration order.
- Remaining time is evaluated after each impact, allowing a body to slide
  along a wall and then hit a floor within the same step.
- A collision at exactly the endpoint still updates final velocity (so the next
  step begins with the correct normal constraint).
- No nudges or collision epsilons have been hidden in the solver. The overlap
  detector's positive-area semantics remain unchanged.

## Boundaries and explicit failures

1. **Initial penetration** is rejected with an exception naming the collider.
   There is no implicit depenetration, because multiple overlapping surfaces
   require an explicit deterministic policy and bounded correction.
2. Collider IDs must be unique; null obstacles and invalid time rejected.
3. `maxContactEvents` defaults to 8. Exceeding it **throws** rather than
   silently finishing the frame with invalid position or velocity. Realistic
   non-penetrating static AABB geometry with slide-only response normally
   consumes at most the two inward axes, but the cap remains explicit.
4. Result contacts are read-only and record absolute times **within the step**.
5. This is a stateless single-step helper, **not** the final persistent character
   motor. The caller owns moving body bounds and passes the current velocity.
6. No gravity, acceleration integration, jump interruption, grounded-state
   inference, moving platforms, one-way platforms, collision assistance,
   friction, wall jump, or rendering is implemented.
7. Floating-point math is deterministic for a fixed runtime/input/order within
   these exact tie policies. No cross-platform bit-for-bit claim is made.

## Verification

Extract into the folder containing `PlatformerSandbox.sln`, rebuild all six
projects, run all xUnit tests. Added classes:

- `CollisionResponseTests`
- `LinearKinematicStepperTests`
- `LinearKinematicStepperValidationTests`

The tests check normals, response policies, wall/floor/ceiling sliding, contact
times, tunneling, t=0/t=1 events, deterministic IDs, simultaneous contact,
zero duration, split-step equivalence, initial overlap rejection, and event
limit enforcement.

**Acceptance for Milestone 11:** constant-velocity AABB collision **response**
and event-aware continuation are tested. **Not yet acceptable:** calling this
stepper with a full jump trajectory's net displacement and claiming collision
safety for the actual curved jump.

## Next milestone

Implement continuous contact search against the true analytical trajectory
segments, stopping at the first boundary event, preserving the profile's
exact position/velocity at that event, and starting a new analytical segment
under an explicitly chosen response policy. A bounded root-search tolerance
can be introduced for curved collision *queries* (not for movement integration),
per A07 / CL-04.
