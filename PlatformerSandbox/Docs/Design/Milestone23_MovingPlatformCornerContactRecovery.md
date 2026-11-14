# Milestone 23 — Moving-platform corner contact and overlap recovery

## Observed crash and ordering

The gym does not advance the moving platform in a separate post-character step. It
renders `movingPlatform.Evaluate(motor.ElapsedSeconds)`, and the controller's
collision queries also use that clock. However, a collision that was ignored by
the controller could leave the character penetrating the independently evaluated
platform on the next update.

The existing ordinary-fall integration recognized only `Normal.Y > 0` landing
contacts. Side and underside contacts were discarded, then the static-world
stepper advanced the character without considering the moving platform. That is
a real bug, independent of floating-point tolerance.

## Corrections

- **All moving contact normals:** Ordinary airborne movement now applies slide
  response in the platform's relative frame to sides and undersides, as well
  as landings from above. Only a downward arrival on the top face establishes
  moving-floor support.
- **Sustained lateral contact:** When a horizontally moving platform is touching
  the character's side, horizontal acceleration trying to move *into* it is
  constrained. Steering away is not constrained.
- **Controlled recovery:** Before a new motor interval queries a moving
  collider, the motor may correct a small pre-existing positive-area overlap.
  This is an explicit opt-in motor-level operation, not a detector option.
  The default correction budget is **0.125 world units**. The smallest
  valid axis-aligned separation is chosen deterministically, and no correction
  may penetrate a static collider. The recovered contact passes through the
  normal slide response. Side correction does **not** cancel the designed jump
  ascent. Underside/top corrections use the same vertical transition rules as
  ordinary collisions.
- **Strict detector preserved:** `MovingPlatformCollisionDetector.SweepFirst`
  still rejects initial penetration. Deep overlaps and corrections that would
  intersect static scenery still fail explicitly, with bounds diagnostics.

## Boundaries and further work

- This milestone supports the gym's single **horizontally translating** moving
  platform. Vertical moving platforms, multiple colliders in simultaneous
  contact, crushing, and general continuous static/moving collision arbitration
  require a unified event scheduler and are not solved by recovery.
- A recovery contact is a diagnostic signal: even if gameplay continues, a
  normal correction can indicate an earlier integration fault. Track these
  contacts when diagnosing gameplay issues.
- Arbitrary or deep overlap should **not** trigger an unlimited teleport.
  Preserve an actionable failure for an invalid world state instead.
- The patch adds no new external dependencies or project references.

## Regression checks

1. Platform approaches an airborne character's side — contact stops interpenetration.
2. Diagonal/corner arrival from above — landing establishes support only on the top face.
3. Jump into the platform underside — upward motion stops, no false support.
4. 120 fixed 60-Hz updates after side contact — no initial-overlap exception.
5. Minimum-translation recovery for shallow side and top overlaps.
6. Deep penetration and static-obstacle interference — deterministic rejection.

Run all prior xUnit tests and then play the gym, approaching the platform from
both sides, hitting its underside, and riding through multiple turnarounds.
