# Scene Management — Phase 6: Regression, Usability, and Performance Validation

**PlatformerSandbox / 15 November 2026**

## Baseline

Install on top of the user-validated **SM Phase 5: Transitions, Loading & Recovery** implementation. This delivery does not replace the collision scheduler, any movement motor, abilities, world definitions, existing scene destinations, route entrance catalog, or previous gym fixtures.

The approved Scene Management architecture calls Phase 6 **full regression, usability, and performance validation**. This pass adds traceable scene-flow metrics, scene-instance ownership observability, an interactive stress test using actual transitions, and headless regression coverage. It does **not** claim that unattended local gameplay tests, target-hardware performance measurements, or screen/controller usability testing have already passed.

## Implemented changes

1. `SceneFlowValidationMetrics` — bounded 16-record trace and counters for accepted, duplicate, busy, invalid requests; successful and failed preparations; real preparation duration (last/mean/maximum). No gameplay-tick processing and no global runtime bus.
2. `GameSceneCoordinator` — counts scene instances prepared/disposed and live/peak scene instances. These are **scene lifetime counters**, not a measurement of GPU memory, content packages, or native backend resources.
3. `SceneFlowSmokeRunner` — bounded eight-action test plan covering level transitions, same-level restart, deliberately failed preparation, and successful recovery. It accepts immutable route/session/world observations, compares expected invariants, reports first failure, and never changes character physics itself.
4. **Route Lab F9** — starts the automated sequence from any active route scene. The same `GameFlowController` handles every queued request; fades, input gating, resource disposal, and preparation remain real. While running, the host deliberately avoids authoritative world ticks and ordinary gameplay/menu commands; Escape aborts the sequence. The sequence ends in a playable route world.
5. Route HUD — reports live requests, commits, failed loads, busiest request classes, scene lifetime counts, measured preparation durations, and smoke-test result.
6. New headless regression tests — repeated scene changes, disposal invariants, fast/conflicting requests, pause/settings focus, failure/retry, stats, and stress-runner success/failure paths.

## Playable stress test (F5 at Title → F9)

1. From Title, press `F5` to enter `ROUTE_A` and wait until the fade finishes. `F6` can also launch `ROUTE_B/EAST`.
2. Locate the **SM PHASE 6** panel near the lower-left. Record `SESSION`, `WORLD ID`, `SCENES: LIVE`, and `PREP MS`.
3. Press **F9**. Eight automated actions run with the existing fade and loading rules. They include level changes with different entrances, level restarts, one intentionally rejected preparation, and a successful retry.
4. While the test runs, the HUD shows `RUN x/8`. It does not step player physics. It validates that the gameplay session ID remains constant, successful transitions create new world IDs and increment entry count, world tick initializes at zero, and the failed preparation leaves the original level, world tick, and entry count unchanged.
5. At completion the panel should show **PASS 8/8** and **SCENES: LIVE 1**. The failure count should have increased by **exactly one** for the intentional failure, while the final route remains playable. `PEAK LIVE 2` is expected as the new scene is prepared before the old is disposed.
6. If it reports `FAIL`, record the displayed step and reason. `ESC` cancels the scripted test; remaining manual tests can still be performed.
7. Run it again. Check that each run passes and the live scene count remains one.

This is **not** a fake transition simulation. The smoke controller generates actual typed requests; `GameFlowController` and `GameSceneCoordinator` prepare/commit the real next world. The smoke controller only verifies the resulting state.

## Manual acceptance matrix

| Test | Sequence | Expected |
| --- | --- | --- |
| Ordinary exits | `1`, `2`, or walk P1 into yellow trigger in Route Lab | Correct destination and entrance; unchanged session ID; new world ID; tick reset |
| Manual failed preparation | F10 in Route Lab | Fade out / failed preparation / fade in; previous world and session retained; retry works |
| Mixed pause/exit | P → Settings → Controls → Back twice → Resume → exit | Exactly one focus owner; simulation frozen while paused; no duplicate scene change |
| Held action handoff | Hold Space / controller A through title-to-level and level-to-level fade | No synthetic jump or menu confirm after transition |
| Repeated restart | T repeatedly or Pause → Restart | New world clock and resource scope every time; same gameplay session |
| Independent player respawn | R then U in Route Lab | World clock and other player continue |
| Return and re-entry | Pause → Return to Title → F5 | New session ID; no stale overlays or gated input |
| Alternate gym regressions | Title F1–F4; F4 → F2 → N; F4 → F1 → Q/E/B | Original movement, player slots, wind, knockback and dash remain accessible |
| Input devices | Keyboard and Gamepad 1; disconnect/reconnect; controller-only menu navigation | Clean edge ownership, no duplicate activation |
| Screen resize / small viewport | Resize title, menus, route and loading overlay | No clipping, missing focus, hidden controls; note screenshots and resolution |
| Load profiling | Repeat transitions on representative low-end hardware | Record actual prep timings and user-visible stall; no invented percentages |
| Fault sensitivity | Movement gym F5 / F6 / F7 | Original strict, diagnostic, resilient modes retained |

## Headless suite

Run `SceneFlowPhase6RegressionTests`, then `SceneTransitionPresentationTests`, `LevelSessionLifecycleTests`, `GameSceneFlowTests`, and **the complete xUnit suite**. The new Phase 6 suite checks dozens of repetitions and explicit cleanup accounting; it does not substitute for running the real MonoGame host locally.

## Remaining limitations and review gates

- Loads are still synchronous. The display reports real preparation times; asynchronous or staged asset streaming is **not** introduced without measured need.
- The metrics count managed scene instances. Native graphics allocations, OS handles, and ContentManager lifetime still require real host inspection/profiling; these numbers should never be interpreted as a GPU leak proof.
- The F9 automated route test intentionally freezes world physics while asserting world ownership. Character motion, abilities, collision and input timing are separately covered by the pre-existing gym fixtures and full regression suite.
- No absolute maximum load-time or FPS performance budget is asserted here without target-hardware evidence.
- The UI should be tried at realistic window sizes and with a gamepad. The automated suite cannot prove visual focus, controller ergonomics, or actual platform audio/graphics health.

**Acceptance for Phase 6**: the F9 in-game sequence passes, manual matrix has no unresolved regressions, all local automated tests pass, scene-live count stabilizes at one during normal gameplay, repeated failures recover, and measured target-hardware loading/performance is acceptable or logged for later optimization.
