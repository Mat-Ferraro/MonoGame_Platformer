# Milestone 14 — Stateful analytical jump motor (first runtime slice)

## Goal

Turn the reference trajectory and collision detection from Milestones 5–13 into an authoritative **headless runtime state** that can advance over multiple scheduler updates. No MonoGame dependency, no direct input-device integration, no hidden numerical integrator, and no automatic geometry correction.

## Files

- `PlatformerSandbox.Simulation/CharacterMotor/JumpMotorPhase.cs`
- `PlatformerSandbox.Simulation/CharacterMotor/JumpMotorContact.cs`
- `PlatformerSandbox.Simulation/CharacterMotor/JumpMotorStepResult.cs`
- `PlatformerSandbox.Simulation/CharacterMotor/JumpKinematicMotor.cs`
- `PlatformerSandbox.Tests/Simulation/CharacterMotor/JumpKinematicMotorTests.cs`

The package is additive and relies on the existing public Milestone 5–13 types. No project references or existing C# files are changed.

## State machine and ownership

`Reference` -> `FreeMotion` when the designer reference reaches its endpoint without collision, or when the first blocking collision interrupts it under `Slide`.

`Reference` -> `Halted` when the first impact occurs under `Stop`.

`FreeMotion` -> `Halted` when a later impact occurs under `Stop`.

`Halted` remains halted: a later gameplay coordinator must create/reset a motor under explicit rules. This package does not implement a repeatable jump coordinator, action-based input mapping, movement request resolution, or ability state machine.

All authoritative mutations happen inside `JumpKinematicMotor.Step`. It returns an immutable snapshot of the updated bounds, velocity, phase, elapsed time, ground-contact indicator, and contact events. A failing step does not commit partial motor state. The caller/scheduler owns fixed timestep selection; this class only accepts a finite nonnegative step duration.

## Reference phase

The motor precomputes the first collision event of its fixed designer reference against a **snapshot of static colliders**. At each requested time before that event, it directly evaluates the exact designer reference at **absolute time since takeoff**; update partitioning does not change the reference. All scheduled horizontal input changes, early-release events, apex phases, and designer power curves are evaluated by the existing reference library.

There is no numerical integration or frame-line sweep for reference movement.

At contact, the exact impact bounds/velocity and the explicit stop/slide response are authoritative. The original reference is invalid after impact and is never resumed.

## Free-motion phase

After an impact, or after the unobstructed jump reference ends, the caller explicitly supplies a **constant 2D acceleration for the requested step**, such as `(0, -20)` for gravity in a positive-up coordinate system. This is a runtime request—not a change to the original designer reference.

The existing `AcceleratedKinematicStepper` solves exact parabolic motion and interrupts it at one or more analytical collision events. It projects inward velocity and acceleration against contact normals for the remainder of that step. For a reference impact, the first normal also projects acceleration immediately to avoid reporting the same floor collision twice within one step.

When `Stop` is selected, motion halts at the earliest contact, but the requested scheduler interval is still considered elapsed.

The motor calculates `IsGrounded` from exact face contact against the static world, with horizontal overlap and non-upward vertical velocity. It is a geometric snapshot, **not** a general contact-manifold solver.

## Example

```csharp
var vertical = JumpFlightSolver.ForHeldJump(
    JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3)));
var horizontal = HorizontalMotionSolver.Solve(
    new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
var reference = JumpReferenceCollisionPlan.Create(
    vertical, horizontal,
    [new JumpTravelInputChange(0, HorizontalDirection.Right)]);

var startBounds = new Aabb(new Vector2D(0, 0), new Vector2D(1, 1));
var floor = new StaticAabbCollider(1,
    new Aabb(new Vector2D(-20, -1), new Vector2D(20, 0)));
var motor = new JumpKinematicMotor(startBounds, reference, [floor]);

// In a future deterministic simulation scheduler:
JumpMotorStepResult frame = motor.Step(1.0 / 60.0, new Vector2D(0, -20));
// Presentation can read frame.Bounds; it does not determine the physics.
```

## Explicit limits and planned follow-up

1. **One configured jump per motor.** No `StartJump`/`CancelJump`/`JumpAgain` API yet, and no input buffering. The initial reference and its input schedule are immutable. Input changes *after* the first collision are not reconstructed from the scheduled design reference.
2. **Constant acceleration only after reference interruption.** The custom apex/power-curve behavior remains exact *until* the first impact; after impact an explicit constant-acceleration law takes over. Maintaining a still-valid vertical reference after a purely horizontal wall collision requires a future per-axis resynthesis policy.
3. **Static AABB snapshot only.** Moving platforms, one-way platforms, slopes, collision assistance, penetration recovery and contact manifolds are out of scope.
4. **Persistent support/ledge transitions not solved yet.** When grounded, acceleration may be projected to zero for the remainder of the current analytical segment. If a character moves off a ledge partway through that segment, gravity might not restart until the next scheduler update. Exact support-loss events are future work.
5. **No horizontal speed cap after a collision.** Post-contact acceleration is caller-defined; horizontal profile constraints apply only during the scheduled reference, until a future movement-request resolver rebuilds post-impact movement segments from the profiles.
6. **No framework/host integration yet.** This is a testable first authoritative simulation slice, not a playable input-controlled controller.

## Validation

Tests cover absolute-time reference evaluation, step partition invariance, floor landing and continued tangential motion, ceiling interruption and descent, wall collision response, multiple events in a single step, Stop behavior, explicit acceleration after the reference, early release/apex hang, schedule reversal, immutability, invalid inputs and initial penetration.

Build and run `PlatformerSandbox.Tests` in Visual Studio. This environment has no `dotnet` SDK, so the ZIP has not been compiled here.
