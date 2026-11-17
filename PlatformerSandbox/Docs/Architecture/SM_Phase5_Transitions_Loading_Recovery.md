# SM Phase 5 — Transition Presentation, Synchronous Loading & Recovery

**PlatformerSandbox · 15 November 2026 · implementation candidate**

## Decision alignment

This is the fifth approved Scene Management & Game Flow phase. It implements **SM-09** (measured synchronous preparation), **SM-10** (truthful, unobtrusive feedback), **SM-18** (recoverable preparation failures) and completes the input/transition serialization aspects of **SM-07**. Existing scene-owned disposal boundaries from Phase 4 remain in force. Phase 6 remains broad integration validation.

## Pipeline and ownership

1. Typed request is validated and queued by the existing `GameSceneCoordinator`. A successful request starts a presentation-only `SceneTransitionCurtain`.
2. Fade out for **0.12 seconds of presentation time**. Gameplay is gated immediately; the original scene/world is still active but receives no simulation steps.
3. Show a fully covered state for one update-to-render boundary. No fabricated percent or forced seconds-long loading screen.
4. On the next safe `Update`, call the original coordinator's `CommitPending` synchronously. It validates preparation results, prepares the destination before retiring the previous scene, then enters and disposes in correct order. A `Stopwatch` measures the whole synchronous **prepare+commit** operation.
5. Fade in for **0.12 seconds of presentation time**. World simulation remains gated until the cover finishes. Held input state is seeded at the boundary before allowing another gameplay step.
6. If the factory reports a recoverable preparation failure, the old scene remains the active scene and the failed request is removed. The curtain fades back to the preserved scene and an error banner remains for six seconds of presentation time. The old pause/menu overlay is preserved if the failure began there.

Scene-transition time is **not world simulation time** and never advances player cooldowns, moving platforms, character states, or abilities. A route trigger generates a committed gameplay event, but the actual world replacement only occurs later in the scene coordinator. UI controls are gated for the short transition to prevent duplicate/jump actions.

## Synchronous-loading scope and limitations

`SceneTransitionCurtain` is headless and has no dependency on MonoGame types. `GameFlowController.AdvanceTransition` owns the timing and delegates scene preparation to the existing factory. Actual content/world preparation is currently synchronous (the approved starting policy) and therefore **cannot animate a continuously advancing loading bar while a long preparation call blocks the main thread**. For the current small graybox worlds, a simple black transition cover with `PREPARING DESTINATION` is sufficient. No percentage is displayed.

A slow scene load can still interrupt frame rendering during its synchronous preparation call. The Route Lab HUD reports measured `PREP+COMMIT <ms>`. If representative authored levels become slow enough to cause noticeable hangs, profile their loading and introduce explicitly staged CPU-safe preparation in a later milestone. Any GPU asset creation stays on the supported graphics context/thread.

## Gym scenario (Route Lab)

From Title, press **F5** for `ROUTE_A/START` or **F6** for `ROUTE_B/EAST`. Touch the yellow left/right exits or press **1/2** to switch levels. Observe fade-out, complete cover, and fade-in. The world tick freezes during the transition and is reinitialized in the new level; the gameplay session and participant IDs persist. Press **T** for restart, **P** for pause. Return to Title through Pause. Prior F1–F4 shortcuts and F4→F2→N/F4→F1 remain unchanged.

In the Route Lab, **F10** triggers one intentionally failed destination preparation. Confirm: the old level, world instance and session survive; a `LOAD FAILED` banner appears; `PREP+COMMIT` shows measured time; the gate releases after fade-in; walking and keys 1/2 succeed on the next attempt. The fixture is deliberately limited to Route Lab so it cannot unexpectedly disrupt other gyms. `F10` retains the earlier resolver-test meaning in the normal movement gym.

## Acceptance matrix

| Scenario | Expected |
|---|---|
| Normal title→gameplay, route A↔B, restart, return to title | One transition and one disposed prior scene per successful request |
| Duplicate or conflicting request during transition | Duplicate coalesced / conflicting request busy; no double scene commit |
| During fade-out, cover or fade-in | No fixed world ticks; no jump/input edges leaking into destination |
| Trigger-based route changes | Requested after committed world step, applied only at safe update |
| Bad destination at request time | Rejected without starting curtain; current scene preserved |
| Simulated load failure via F10 | Current world preserved, banner shown, transition gate eventually released |
| Menu navigation while covered | No modal focus changes or double activation |
| Direct developer `--dev-level=...` | Immediate boot still supported, no title delay |
| Synchronous preparation diagnostics | Actual measured milliseconds, never fake percentage |
| Full prior physics/regression suite | No motor/collision/ability changes |

## Verification

Run `SceneTransitionPresentationTests`, `LevelSessionLifecycleTests`, `GameSceneFlowTests`, and finally the full solution xUnit suite. Manually test Route Lab exits, the F10 failure fixture and retry, holding jump/confirm through a transition, pause/resume, and direct developer boot. This package is structurally inspected but **has not been .NET compiled or executed in this environment**.
