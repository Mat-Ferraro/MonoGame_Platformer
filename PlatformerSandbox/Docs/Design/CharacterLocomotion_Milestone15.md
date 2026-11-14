# Milestone 15 — Ground movement and repeatable jump activation

## Purpose

Introduce one headless, stateful locomotion owner around the previously verified mathematical and collision systems. The public `CharacterMovementController` coordinates **ground/free-fall motion** and **one active jump flight at a time**. It is a vertical slice for testability, **not** a complete ability framework or input subsystem.

Architecture coverage: A01, A03, A06, A07, A08 (initial integration), A12 and A14. No MonoGame dependency; the host will read snapshots rather than changing simulation state.

## Files

- `PlatformerSandbox.Simulation/CharacterMotor/CharacterMovementController.cs`
- `PlatformerSandbox.Simulation/CharacterMotor/CharacterControlStepResult.cs`
- `PlatformerSandbox.Simulation/CharacterMotor/CharacterControlContact.cs`
- `PlatformerSandbox.Tests/Simulation/CharacterMotor/CharacterMovementControllerTests.cs`

Everything is additive. No previous `.cs` files, `.csproj` files, configuration, or NuGet dependencies change.

## Inputs and ownership

```csharp
CharacterControlStepResult result = controller.Step(
    1.0 / 60.0, HorizontalDirection.Right, jumpPressedThisStep: false);
```

- The scheduler supplies a duration; for deterministic gameplay use a consistent fixed interval.
- `direction` is a logical action (`Left`, `Neutral`, `Right`), not a device key.
- `jumpPressedThisStep` means a **discrete press event** in this update, not a held button. Input mapping/edge detection is a later module.
- One controller owns its `Bounds`, `Velocity`, support state, clock and jump count.
- `CharacterControlStepResult` is an immutable read-only snapshot. The presentation layer does not drive the motor.
- World geometry is a snapshot of static AABB colliders with unique IDs. Overlapping initial geometry is rejected.

## Motion rules

### Ground movement and ordinary free fall

On each step with no active jump, the controller chooses the solved ground or air `HorizontalMotionProfile`, based on support at the beginning of the step. The horizontal acceleration is calculated from the requested direction and current velocity. The step is partitioned at the analytical transition times for reversal and reaching cruise speed. Each segment uses the previously verified `AcceleratedKinematicStepper` against the static world, with `Slide` response. No numerical Euler integration and no high-speed tunneling approximation are introduced.

On supported ground, Y stays constrained at zero velocity. When airborne outside a jump, gravity (`gravityAcceleration`, negative under positive-up coordinates) applies. Initial support and final support are calculated from exact AABB face adjacency with strict horizontal overlap.

### Jump and landing

A jump-press action is accepted only when supported and no jump is active. The controller constructs a **new** `JumpReferenceCollisionPlan` for a held jump, selecting the solved air horizontal profile and inheriting the current horizontal running velocity. A fresh `JumpKinematicMotor` then owns the movement until a landing. Its reference flight and collision handling follow Milestone 14 exactly. Ground and air controllers never both write position during the same step.

When the jump motor reports floor support, ownership returns to the ground/free-fall branch for the *next update*. Another press on that or a later update starts a new jump. Requests while airborne are ignored; double jump, buffer, and coyote time are abilities to add explicitly later.

### Validation

The controller rejects invalid durations and directions, non-finite/non-downward gravity, duplicate collider IDs, invalid references and initial penetration. A duration of zero only returns a snapshot and never starts a jump.

## Example usage

```csharp
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Simulation.Motion.Constraints;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Motion.Solvers;

var ground = HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(10, 0.2, 0.1, 0.35));
var air = HorizontalMotionSolver.Solve(new HorizontalMotionConstraints(8, 0.35, 0.2, 0.55));
var flight = JumpFlightSolver.ForHeldJump(
    JumpConstraintSolver.Solve(new JumpConstraints(4, 0.4, 0.3)));
var floor = new StaticAabbCollider(1,
    new Aabb(new Vector2D(-50, -1), new Vector2D(50, 0)));
var body = new Aabb(new Vector2D(0, 0), new Vector2D(1, 1));
var controller = new CharacterMovementController(body, [floor], ground, air, flight, -20);

// In a future fixed simulation scheduler; this action is a *press edge*.
CharacterControlStepResult takeoff = controller.Step(1.0 / 60.0,
    HorizontalDirection.Right, jumpPressedThisStep: true);

// Presentation observes takeoff.Bounds; it does not change authority.
```

## Explicit limitations — important before host integration

1. **One fixed held-jump reference after takeoff.** Input direction changes during an active reference do not alter its scheduled horizontal input. The dynamic air-input resolver is a future milestone.
2. **No live early-release event.** Earlier solvers support configured early-release trajectories, but modifying an active jump at an arbitrary frame requires rebuilding reference authority from the current analytical sample. This milestone intentionally does not approximate that behavior.
3. **Ledge departure is not continuous-time exact yet.** Ground support is checked at subsegment/update boundaries. If a character crosses off a ledge inside one ground segment, gravity begins at the next segment/update; it does not start at the precise edge-crossing time. An event-aware support-loss search should be added before final tuning.
4. **Simple static AABB geometry only.** No moving or one-way platforms, slopes, depenetration, or collision assistance.
5. **No input buffering, coyote time, double jump, ability gating or action resolution.** The caller must supply edge-triggered actions; repeated `true` values while grounded mean separate jump requests.
6. **No MonoGame host wiring.** This is a headless simulation component. A future movement gym can drive it with a fixed-step adapter.
7. Ground and post-impact movement still follow the existing `Slide` response. Ground contacts are geometric support, not a general manifold/contact solver.

## Validation

The included xUnit cases cover stationary and accelerating ground motion, cruise, neutral stopping, reversal and partition consistency, wall collision, grounded-only jump activation, inherited ground speed, repeat jumping after landing, airborne falling and landing, stepping off short platforms, clock advancement, zero-duration snapshots and constructor/input validation.

Build the six-project solution in Visual Studio and run all xUnit tests. No .NET SDK is installed in the packaging environment, so local compilation/test execution is the final verification.
