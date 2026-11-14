# Milestone 10 — Framework-independent collision geometry and swept AABB detection

## Status and scope

Additive implementation for A02 (custom 2D collision), the detection portion of A03
(swept surfaces and outward normals), A07 (well-defined event reporting), and
A14 (headless, MonoGame-independent libraries). No existing source or project
configuration is changed. All units are arbitrary but **must be consistent**.

**World coordinate convention:** X increases rightward, Y increases upward.
This matches the existing jump profiles. A future MonoGame adapter must convert
screen-space Y-down coordinates rather than changing this core convention.

## New modules and ownership

| Path | Purpose |
| --- | --- |
| `Foundation/Geometry/Vector2D.cs` | Validated immutable 2D vector, no MonoGame dependency |
| `Foundation/Geometry/Aabb.cs` | Validated axis-aligned bounds and translation |
| `Simulation/Collision/Detection/StaticAabbCollider.cs` | Static obstacle bounds with stable ID |
| `Simulation/Collision/Detection/AabbCollisionDetector.cs` | Stateless overlap and linear swept queries |
| `Simulation/Collision/Contacts/OverlapContact.cs` | Initial-penetration query result |
| `Simulation/Collision/Contacts/SweepHit.cs` | First impact and surface normal (no state mutation) |

The `Foundation` types do not reference `Simulation`, and collision detection
never references `Gameplay` or `MonoGame`. Callers own the collider collection;
there is no mutable global collision world in this milestone.

## Queries and exact conventions

### Positive-area overlap

`AabbCollisionDetector.Overlaps(moving, obstacle)` is true only for **strictly
positive** intersection width and height. A face touch or corner touch is not
a penetration.

`FindOverlap(moving, collider)` reports a single minimum-axis translation
(normal from obstacle toward mover, plus positive separation distance).
This does **not** execute depenetration. For containment, distance is to the
nearest *exit face*, not merely intersection width. Ties use the X axis and
then the positive direction. There is no guarantee this independently selected
translation is sufficient or optimal for **multiple simultaneous obstacles**.
That belongs to the motor's bounded resolution policy.

### Linear swept AABB

`Sweep(moving, displacement, collider)` computes the first time a translating
box would enter one stationary AABB on a **single linear displacement segment**.
`TimeOfImpact` is the normalized fraction in **[0, 1]**; physical contact time
is `startTime + TimeOfImpact * segmentDuration` when duration is supplied by the
caller. The returned normal points **from the obstacle toward the mover**:

- Landing on floor: `(0, +1)`
- Hitting underside of ceiling: `(0, -1)`
- Running into wall to the right: `(-1, 0)`
- Running into wall to the left: `(+1, 0)`

The box at contact is reported but **not corrected or nudged**.

Boundary and tie conventions:

1. An initial **positive penetration** is deliberately not a swept collision;
   query `FindOverlap` separately before integration.
2. A face touch at **t=0** is a hit only when the mover moves inward.
3. A face touch at **t=1** is reported, so the next segment can handle contact.
4. Mere tangential motion along a touching face is not a hit.
5. A grazing corner at one isolated instant (entry time equals exit time) is
   not a blocking contact.
6. If simultaneous X/Y slab entry is a genuine inward collision, choose X
   as the contact normal.
7. Multiple obstacles: choose the smallest impact time; **exactly equal** times
   choose the smallest collider ID irrespective of input order. Duplicate IDs
   are rejected. No hidden epsilon is used to merge near-equal times.

### Why this does not yet make jumping collision-safe

A full jump profile is **not linear**. Calling a single straight-displacement
sweep from a jump's start to end is not an exact collision test: a curved jump
can hit a ceiling even if the endpoints are below it. Milestone 10 only solves
collision along a specified **linear segment**. Do not use it to claim exact
continuous collision detection for the existing analytical vertical profiles.

A later milestone will connect analytical trajectory segments to collision
boundary events (using the profile's exact position/velocity and a documented
root-search tolerance), then invoke explicit response policies. This is allowed
under A07 and CL-04; it must not silently replace analytical motion with a
numerical integrator.

## What is intentionally NOT implemented

- Collision response, wall sliding, ground-state decisions, friction, or
  authoritative character position writes (A01/A03)
- Step-up, corner correction, jump forgiveness, other bounded assistance (A04)
- Moving platforms or relative swept motion (A05)
- One-way platforms, slopes, tile/world broad-phase acceleration structures,
  shape casts for circles/capsules, dynamic obstacle transforms, or rotation
- Fixed-timestep motor scheduler or MonoGame rendering bridge

## Validation procedure

1. Extract into the root containing `PlatformerSandbox.sln`.
2. Rebuild the existing six-project solution; no `.csproj` changes are required.
3. Run all existing xUnit tests plus the new Foundation Geometry and Simulation
   Collision test classes.
4. Ensure existing Milestones 4–9 remain green.

**Implementation acceptance:** deterministic reported time/normal/contact
for crossing, penetration, edge-touch, tunneling, corner/tie, and multi-obstacle
cases. No assertion that game physics integration is complete.
