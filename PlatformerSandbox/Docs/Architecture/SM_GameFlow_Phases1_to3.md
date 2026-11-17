# Scene Management & Game Flow — implementation slice

**Status:** Delivered for local validation, 15 November 2026. This is a source
update against the user-validated A12 movement package, **not** a claim that
MonoGame compilation or integration tests have already passed.

**Decision baseline:** `MonoGame_Platformer_Scene_Management_and_Game_Flow_Architecture(1).docx`.
The full SM-01–SM-20 design remains approved. This patch implements the first
three phases plus typed level restart and safe developer entry; it does not
claim all six phases have shipped.

## Architecture

- `PlatformerSandbox.Gameplay/GameFlow/GameSceneCoordinator.cs`: headless
  coordinator and minimal scene lifecycle contract. It prepares new scenes,
  queues typed transitions, rejects duplicate/conflicting/unsupported changes,
  and commits only when `Game1.Update` calls `CommitPending` outside physics.
  An unsuccessful `TryPrepare` preserves the previous scene.
- `PlatformerSandbox.Gameplay/GameFlow/GameUiNavigation.cs`: headless UI stack
  and semantic navigation policy. Title is a major-scene view; Pause, Settings,
  and Controls are overlays. Focus returns to the prior selection on Back.
- `Source/Game1.cs`: MonoGame input and presentation adapter, reusable
  graybox scene factory, title screen, menu rendering, and synchronous
  preparation of existing world/gym fixtures.
- `GameSceneFlowTests`: headless lifecycle, duplicate/failure handling,
  UI-focus/navigation, pause-time and reset-policy tests.

## Ownership

`Game1` owns graphics and application-scoped input bindings. The coordinator
owns *which major scene* is active and the controlled switch between scenes.
The UI layer owns overlays and focus. The current `WorldSession` owns tick,
players, collision and committed events; neither the UI nor scene coordinator
calls the motor or directly mutates player positions.

Four stable level IDs are available in this vertical slice:
`MOVEMENT_GYM`, `SHARED_WORLD`, `AUTHORED_WORLD`, `MOTION_LAB`.
`START` is the only entrance currently supported; unknown destinations are
rejected rather than silently mapped to an arbitrary gym.

## Behavioral contract

1. On launch, instantiate Title. The user can select or directly launch a gym.
2. When Play/Restart/Return is requested, queue the typed request. A duplicate
   request is coalesced; a different request while one is pending is `Busy`.
3. At a safe update boundary, prepare the destination while the current scene
   is still available. If preparation fails, preserve it and report the error.
4. Exit the old scene; enter the new scene; release previous scene ownership.
5. When an overlay is open, consume UI input first and advance no gameplay
   ticks; presentation and UI controls remain responsive.
6. On resumed gameplay, reseed the current device state so a held confirm/jump
   cannot become a new action edge.
7. An explicit level restart creates a fresh scene/world; R/U still perform
   targeted player-only respawns without resetting the shared world clock.

## Acceptance checks

- Title -> each of four gyms -> Title can repeat without stale pause state.
- F4 -> F2 -> N still joins P3; F4 -> F1 reaches wind/dash/impulse Motion Lab.
- Pause at nonzero world tick, wait, navigate Settings and Controls, and resume:
  the tick count does not advance until unpaused; the next tick is a single
  fixed step, never a burst.
- Settings navigated from Title returns to Title; from Pause returns to Pause;
  Controls returns to the same Settings screen and preserves its focus.
- Restart resets world time; player-only respawn does not.
- No physics changes. Rerun A08, A09, A10, A12, World Events and collision
  xUnit regression suites.

## Next planned scene work

Complete SM Phase 4 with actual authored level/entrance relationships and
scene-owned resource disposal (beyond current graybox references), then
Phase 5 fade/loading/recovery hardening when real content requires it,
followed by Phase 6 integration and playtest validation.
