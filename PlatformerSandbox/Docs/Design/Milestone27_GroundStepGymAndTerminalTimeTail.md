# M27 — Walkable Ground-Step Fixture + Moving-Platform Time-Tail Hotfix

## Scope

This update is based on the validated `PlatformerSandbox_Milestone27_Final_CollisionAssistance` package. It does not change the step assistance planner, its thresholds, crusher/collision rules, or moving-platform trajectory.

### Test bump

- The default `Both` gym adds static collider **16**, x [-2.40, -1.20], y [0, 0.10].
- It is immediately to the **LEFT** of the player spawn (x [0, 0.8]).
- Drawn gold and labeled `STEP TEST - WALK LEFT - F9`.
- **F9 off:** Walking left causes a conventional wall collision; the character cannot step up.
- **F9 on:** Walking left causes the existing `GroundStep` planner to perform a 0.10-unit vertical correction (0.12 max) plus bounded 0.04-unit toe placement, producing an assist event.
- The bump is limited to default `Both` mode. Single-platform test gym configurations retain their previous geometry.
- Existing static ID count increases from 13 to 14 in default mode.

### Moving-platform event cannot advance time

The reported exception had `now=60.0` and `segment=2.2204460492503131e-15`: this interval is positive in local time but cannot advance an absolute binary64 clock at t=60. `MovingPlatformCollisionDetector.SweepOne` now consumes this specific kind of *terminal* tail only if the relative body/platform displacement on each axis is bounded to <=1e-9 world units. Any pending internal event or physically significant travel still throws, and the public query still rejects nonrepresentable input intervals.

This normalization is intentionally local to the detector, not a general tolerance in static/moving collision response or the Strict/Diagnostic/Resilient modes.

## Verification

1. Build solution and run all xUnit tests. New tests cover the visible bump, F9 on/off, collision invariants, 60-second platform-turn tail, significant-tail rejection and invalid input.
2. Start gym in F5 Strict, leave F9 off, walk LEFT to the gold bump: player should stop.
3. Press F9 (resets spawn), walk LEFT again: player should step onto the bump and the assist counter increases.
4. Hold neutral input for at least 65 simulated seconds, then test moving platforms, turns, and four crush stations as before.

No dotnet executable was available in the packaging environment: source and archive validation are not a substitute for the user's Visual Studio build and tests.
