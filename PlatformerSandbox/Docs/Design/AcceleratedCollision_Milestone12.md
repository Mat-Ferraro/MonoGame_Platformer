# Milestone 12 — Continuous parabolic AABB collision and event-aware acceleration

## Scope

This milestone extends **A03** (surface-normal response), **A07** (analytical
and event-aware integration), **A14** (headless simulation), and **CL-02**
(unobstructed reference trajectory versus collision-interrupted outcome).

It adds *constant-acceleration* 2D movement, including a truly curved
parabolic path, and continuous collision detection against **static AABBs**.
It does **not** replace the working Milestone 10 linear sweep or Milestone 11
constant-velocity stepper. It neither samples the curve at a succession of time
steps nor substitutes its endpoint chord for the actual curve.

**Important limit:** Our Milestone 5–9 jump profiles are often **power-law,
piecewise-analytical trajectories, not constant-acceleration parabolas**.
This milestone is a working first family of curved motions; **do not** pass a
whole `JumpFlightTrajectory`/`JumpTravelTrajectory` as its endpoint
velocity/acceleration or treat it as covered. Adapters with explicit analytic
phase boundaries and monotonic intervals are a future milestone.

## Added production API

| Type | Role |
|---|---|
| `MotionSample2D` | Immutable 2D position/velocity sample |
| `ConstantAcceleration2D.Evaluate` | Exact analytical 2D evaluation via the existing Milestone 4 solver |
| `ParabolicSweepHit` | Contact time (seconds), cardinal normal, impact box and instantaneous velocity |
| `ParabolicAabbCollisionDetector.Sweep` | Earliest contact along a parabolic translation against one static box |
| `ParabolicAabbCollisionDetector.SweepFirst` | Earliest contact in a world, exact ties broken by smallest collider ID |
| `AcceleratedKinematicStepper.Step` | Event-aware slide/stop, with acceleration projection and remaining-time continuation |
| `AcceleratedKinematicContact` | Full contact event: incoming/outgoing velocities AND accelerations |
| `AcceleratedKinematicStepResult` | Final bounds, velocity, constrained acceleration, read-only event history |

### Coordinate conventions

World Y increases upward. Surface normals point from the static collider
toward the moving AABB, matching Milestones 10 and 11. Time is seconds;
position tolerance is world-coordinate distance. Collision time is returned as
**seconds since step start**, not the normalized fraction used by the linear
sweep.

## Continuous contact algorithm

For each axis, displacement under constant acceleration is exactly:

`d(t) = v0*t + 0.5*a*t*t`.

The moving box overlaps a stationary box on X only when its X displacement
satisfies:

`target.Left - moving.Right < dx(t) < target.Right - moving.Left`.

The analogous strict inequalities hold on Y. The detector solves for every
real root of the FOUR face-equations (two per axis) in the requested time
range. These exact root candidates partition time into intervals: within one
open interval, overlap on each axis cannot change. Checking overlap at the
interval midpoint classifies that WHOLE interval without time-step sampling.

The left boundary of the first interval with positive-area overlap is the
continuous entry event. The detector identifies the entering face from its
analytic root and the sign of instantaneous velocity (or acceleration when
velocity is zero). Equal X/Y entry times prefer X. For stationary face
contact, pure tangential movement does not count. An isolated tangent to a
curve or a corner with no interval of positive-area overlap is not blocking.
An endpoint face touch counts if the motion is inward, matching Milestone 10.

Roots are computed with a stable quadratic formula. Numerical arithmetic is
still `double`, so exact mathematical roots are represented approximately.
**`contactAlignmentTolerance`** (default **1e-9 world units**) explicitly
bounds snapping the selected impact face to the obstacle's face after the root
is evaluated. The algorithm **throws** if the correction exceeds this bound;
it never silently makes an unbounded penetration fix. This is deterministic
floating-point housekeeping, **not** player-assistance/stair-snap behavior.
No unbounded iterative collision root finder is used in this family.

## Response semantics

`CollisionResponseMode.Slide` removes the inward normal component from **both**
the instantaneous velocity and the currently applied acceleration. The
remaining tangential or outward components continue over the remaining time
in a **new exact constant-acceleration segment**. For example, on a floor with
downward gravity, vertical velocity and inward gravity are constrained to
zero until the end of that step, allowing horizontal movement to continue.
On a ceiling with downward gravity, upward velocity is removed but downward
gravity remains, so the body begins falling. `Stop` removes both axes' motion
on the first collision and holds the impact position for the rest of the step.

This acceleration projection is an explicit **short-lived contact constraint**:
it does not permanently modify a jump designer's gravity setting or next
frame's movement request. A future authoritative motor will establish how
new abilities, jump profiles and contacts produce the next active segment.

Initial positive penetration throws in the stepper. `Sweep` itself returns
null on penetration, matching the original linear query's convention.
Zero duration produces no contact event. Duplicate collider IDs are errors.
Exact equal event times prefer the lowest stable ID. Event count is bounded;
exceeding `maxContactEvents` throws rather than returning a truncated result.
No random offsets, automatic penetration fixes or friction are introduced.

## Example usage

```csharp
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;

var player = new Aabb(new Vector2D(0, 0), new Vector2D(1, 1));
var ceiling = new StaticAabbCollider(7,
    new Aabb(new Vector2D(-2, 2.5), new Vector2D(2, 4)));

// Starts and ends at the same vertical displacement. The curved apex would
// intersect the ceiling; an endpoint-only sweep would miss this collision.
var result = AcceleratedKinematicStepper.Step(
    player,
    new Vector2D(0, 8),
    new Vector2D(0, -16),
    durationSeconds: 1,
    obstacles: new[] { ceiling });

// result.Contacts[0].TimeSeconds == 0.25
// result.Contacts[0].Normal == (0, -1)
// result.Contacts[0].IncomingVelocity == (0, 4)
// result.Contacts[0].OutgoingVelocity == (0, 0)
// result.Contacts[0].OutgoingAcceleration == (0, -16)
// result.Bounds.Bottom == -3  (falls back down during remaining 0.75 s)
```

## Test acceptance

- Exact unobstructed constant-acceleration movement in both axes.
- Arc/ceiling contact even when net displacement is zero (a chord would miss it).
- Apex tangency and isolated corner grazing do **not** create blocking contacts.
- Immediate, end-of-step, and high-acceleration contacts; stable normals.
- Sliding across surfaces while preserving acceleration tangent to the surface.
- Gravity held by a floor during the remaining step; ceiling hit retains fall acceleration.
- Multiple events in one step, stable collider IDs, bounded event count.
- Reject invalid durations, initial penetration, duplicate collider IDs and invalid tolerances.
- Split-time equivalence for uninterrupted analytical motion.

The archive contains only new `.cs` and documentation files; no existing
project files or source files are overwritten. Visual Studio should discover
`.cs` files automatically in SDK-style projects. Run **Rebuild Solution** and
**Test Explorer → Run All Tests**. The package has been structurally verified;
actual C# compilation and xUnit execution must be done on your Windows host.

## Next

Support collision searches over the actual piecewise **power-law
`JumpFlightTrajectory`** and timed **`JumpTravelTrajectory`** (including
release, apex, horizontal acceleration transitions and input events). Each
profile should publish explicit phase boundaries and guaranteed monotonic
intervals or an equivalent inversion contract. Then build a persistent
character motor that interrupts reference trajectories at collision events,
without handing gameplay authority to animation, rendering or input adapters.
