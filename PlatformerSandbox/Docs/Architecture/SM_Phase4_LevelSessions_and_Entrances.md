# Scene Management — Phase 4: Gameplay Sessions, Level Lifetimes, Entrances

**PlatformerSandbox** · 15 November 2026 · Implementation candidate

## Baseline

Apply after the validated `PlatformerSandbox_SM_GameFlow_Phases1to3_OnePass_Gym.zip` package. This update preserves A08–A12 movement, multiplayer, collision, world event, abilities, fault policies, and the existing F1–F4 gyms. No motor solver or camera-renderer file is changed.

The approved architecture is SM-08, SM-12, SM-13, SM-14, SM-15, SM-19, and SM-20. The first scene-flow package already implemented a basic Restart; this phase completes explicit cross-level entrance routing, gameplay-session identity, world replacement, and owned-resource scope infrastructure.

## Contracts and lifetime policy

- `GameSceneCoordinator` is still the only authority for scene switches. `SceneTransitionRequest.ChangeLevel` is legal only while gameplay is active. The destination is validated before queueing; the new world is prepared before the old scene is retired, and committed outside `WorldSession.Step`.
- `GameplaySession` is **playthrough-scoped**: a stable `SessionId`, participant/character/input identities and the current level destination. It survives level changes and full level restarts. Returning to Title **ends** the session; beginning Play creates a new one. Scene lifetime and gameplay-session lifetime are deliberately different.
- Each `WorldSession` has a **new** world-instance ID and starts at `WorldTick = 0` upon entry/change/restart. It owns live player motors, jump buffers, energy/abilities, moving-platform phases, and committed events. A player-only respawn leaves that world and every other player untouched.
- `LevelRouteCatalog` owns validated `LevelId → entrance → named-spawn` mappings, plus `exit trigger → SceneDestination`. The catalog checks that every exit points to a real level and a real entrance. A room definition also validates its geometry, moving-platform references, and spawn IDs.
- `LevelEntranceDefinition` supports player-specific spawns, with an explicitly authored fallback spawn for late-joined participants. Player identity and input source are independent of which entrance is chosen.
- `SceneResourceScope` is the explicit, idempotent owner for future **scene-local** disposable resources. This graybox slice uses a shared application-owned `SpriteBatch` and `Texture2D`, and does **not** pretend those GPU assets are newly allocated per level. Shared resources must not be put in a scene scope.

## Route Lab fixture

Two data-defined rooms, `ROUTE_A` and `ROUTE_B`, have distinct moving-platform/static geometry and validated entrances `START`, `WEST`, and `EAST`. Their left and right nonblocking exit triggers have IDs `601` and `602`. Both exits lead to the *other* room, but at different entrances:

- Left exit (`601`) → other room at `EAST`.
- Right exit (`602`) → other room at `WEST`.

`START` begins near the center. `WEST`/`EAST` put P1 and P2 at separate authored spawn points, away from the destination exit so there is no immediate ping-pong. `GUEST` is a validated fallback for additional player slots. **Only P1** activates room-exit triggers in this development fixture, so P2 cannot unexpectedly change the level for everyone while P1 is testing. The underlying world still delivers player-attributed trigger events for all players.

A confirmed end-of-tick `TriggerEntered` event is read by the host and converted to a typed scene-change request. No new world is created from inside `WorldSession.Step`. Trigger overlap uses the established *end-of-tick* overlap contract, so high-speed pass-through of very thin triggers remains a known follow-up outside this milestone.

## Playable verification

1. From Title press **F5** or select **PLAY LEVEL ROUTES**. This launches `ROUTE_A / START`. F6 launches `ROUTE_B / EAST` for direct entrance validation. Optional command-line entry: `--dev-level=ROUTE_B --dev-entrance=WEST`.
2. Observe `SESSION`, `ENTRIES`, `WORLD ID`, `TICK`, `LEVEL` and `ENTRANCE` in the bottom-left Phase 4 panel.
3. Walk **P1** (A/D + Space, or F8-selected Gamepad 1) through the yellow right gate (`ZONE 602`). The next update should enter `ROUTE_B / WEST`. `SESSION` remains constant; `ENTRIES` increases; `WORLD ID` changes and `TICK` resets.
4. Walk toward the yellow left gate (`ZONE 601`) to return to `ROUTE_A / EAST`. Instead of walking, you can press **1** (left exit) or **2** (right exit) to test the identical catalog mapping immediately.
5. Let a world advance, then **R** respawns P1 and **U** respawns P2; neither operation changes `WORLD ID`, `TICK` or the other player's snapshot. Press **T** to restart the entire level; this changes `WORLD ID` and resets the clock while keeping the `SESSION`, destination and entrance.
6. Pause with **P**, **Esc**, or Gamepad Start; ticks must freeze. Resume, then choose **RETURN TO TITLE**. Starting a new Route Lab creates a *new* `SESSION`.
7. Try a direct launch with a nonexistent entrance: `--dev-level=ROUTE_A --dev-entrance=MISSING`. The invalid destination is rejected and Title remains usable. In-app `RequestChangeLevel` rejects invalid/unknown IDs without retiring the current scene.
8. Regression check: Title F1–F4 and F4 → F2 → N, F4 → F1 → Q/E/B remain available. Both worlds are independent, with no changes to the collision motor, A09/A10 or A12 simulation.

### Controls

| Context | Keys |
| --- | --- |
| Title | F1 movement; F2 authored; F3 Motion Lab; F4 shared world; **F5 routes A / START; F6 routes B / EAST** |
| Route Lab P1 | A/D, Space; F8 toggles keyboard vs. Gamepad 1 |
| Route Lab P2 | J/L, I |
| Route Lab scene exit | Walk P1 into left/right yellow zone, or **1** / **2** respectively |
| Respawn | **R** P1 only; **U** P2 only |
| Restart level | **T** or Pause → Restart Level |
| Pause/title | **P/Esc**, navigate pause menu, Return to Title |
| Fault policies | F5/F6/F7 during gameplay are unchanged; the route-launch shortcuts F5/F6 work at Title only |

## Automated regression tests

Run `LevelSessionLifecycleTests`, then `GameSceneFlowTests`, `WorldGameplayEventTests`, `SharedWorldClockTests`, the A09/A10/A12 tests, and finally the full suite.

The new tests validate entrance selection and distinct spawns, both exit links, missing IDs and invalid authoring, actual trigger-entry event generation, fresh-world vs. persistent-session lifetimes, player-specific respawn, same-entrance restart, source/character identity, transactional participant validation, deferred scene changes, duplicate/busy conflict policy, failed preparation fallback, pause, return-to-title and resource-scope disposal.

## Explicit scope limits

- `RouteLevelGym` is a **typed, in-code fixture**. This phase does not implement a file loader, editing UI, full game-content authoring suite, checkpoint persistence, or saving to disk.
- Session continuity is **identity and device ownership**. Health, inventory, progress flags and inter-level ability retention are not invented; existing ability/character state is world-local and resets upon level entry.
- Only the new route lab has authored automatic exit links; the original movement/authored/motion/shared fixtures remain regression gyms.
- Scene resources are owned explicitly through a `SceneResourceScope`, but the current graybox routes allocate no new scene GPU assets. Actual ContentManager/texture disposal gets exercised when level-specific assets are added.
- Scene transitions remain synchronous without fake progress bars or fades. SM Phase 5 handles justified loading UX/fade/recovery refinements; transparent failure handling stays strict for programming faults.
- .NET compilation and test execution were **not possible in the delivery environment**. Source structure checks are not a substitute for Visual Studio verification.
