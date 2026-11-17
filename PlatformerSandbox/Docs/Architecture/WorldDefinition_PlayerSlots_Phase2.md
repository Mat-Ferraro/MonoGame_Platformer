# World Definition and Player Slot Phase 2 — Data-defined gym

## Dependencies
Apply **after** Shared World Clock Phase 1, A11 Phase 1 and A08 Phase 2.
This is an additive gameplay feature: it does not modify the character motor, collision scheduler, moving platform kinematics or numerical fault policy.

## New contracts
- `WorldDefinition`: validated stable `LevelId`, immutable arrays of static colliders and moving-platform trajectories, and named spawn points. IDs are unique across static/moving colliders and across spawn points. Initial spawn overlaps, nonfinite/default boxes, unknown platform anchors and invalid levels are rejected before the runtime world is created.
- `WorldSpawnPoint`: static position or moving-platform-relative anchor. `ResolveSpawnBounds(spawnId, worldTime)` translates the authored spawn by the selected moving platform's analytical displacement since time zero; this happens only on join/respawn, not every frame.
- `WorldPlayerSlotRegistration` / `WorldPlayerSlotInfo`: explicit `PlayerId`, separate `CharacterId`, nullable `InputSourceId`, named `SpawnId`. Positive unique IDs; a physical/logical input source can belong to at most one slot. Reassignment updates metadata without moving or respawning characters. Input polling remains in Game1, and world commands still target PlayerId.
- `WorldSession(WorldDefinition, ...)` overload + `AddPlayer(registration)`, `GetPlayerSlot`, `PlayerSlots`, `AssignInputSource`, `SetPlayerSpawn` and parameterless `RespawnPlayer(id)` overload. Original geometry constructors / `AddPlayer(id,bounds)` / `RespawnPlayer(id,bounds)` remain backwards compatible.
- `AuthoredWorldGym`: second F4 fixture with two moving platforms, a floor, four named spawns, two initial players and an optional third late join.

## Playable verification
1. Launch and press `F4`. The **original shared-clock** gym is still selected; its existing behavior is unchanged.
2. Press `F2` while in F4. The new **WORLD DEFINITION + PLAYER SLOTS** panel appears, with `GYM_TWO_PLATFORMS`, two independently moving platforms (IDs 201/202), and P1/P2 on them.
3. Wait through platform turnarounds. `WORLD TICK` continually increases; both supports continue to identify their own platforms.
4. Move/jump P1 with `A/D + Space` (or `F8` for gamepad) and P2 with `J/L + I`. Source IDs remain independent; P1 F8 changes only its input-source registration, not PlayerId or CharacterId.
5. Press `R` / `U` after the world has advanced: one player respawns on the **present** position of their named platform. The other player and world tick are unaffected.
6. Press `V`: P1's HUD spawn changes between `LEFT_RIDE` and static `GROUND`, but P1 does not move until `R`. Use `R` to verify P1 respawns at the selected location. Press `V`, `R` again to return to platform.
7. Press `N` while the right platform is moving: player 3 (yellow, character ID 1003) joins at `RIGHT_GUEST`, anchored to the right platform's current position. P2 is unaffected. Press `M` to remove P3; use `N` to join again.
8. Press `P` to pause: tick and platforms freeze. Press `P` again to resume. Press `T` to restart: tick resets to zero and the two-player original authored configuration returns. Press `F2` to switch to the **original** F4 test; press `F4` to leave.
9. Switch to the normal movement gym and verify all collision-assistance, crush and request-priority tests still work.

## Automated tests
Run `WorldDefinitionPlayerSlotTests` in Test Explorer and then run the entire suite. Includes data validation, anchored spawn timing, defensive copies, stable player/character/source IDs, duplicate-source protection, independent reassignment, atomic failed registration, late join/remove, spawn switching, respawn at nonzero world time, and deterministic player update ordering.

## Explicitly deferred
No disk JSON parser, level streaming, collision rules between characters, spawning gameplay entities, input remapping UI, scene coordinator, or editable level designer. These are separate future milestones.

## Correctness notes
The existing shared-clock F4 test is still the default F4 mode; the new authored F2 mode is isolated. Both use one WorldSession clock. Named-spawn validation is an authoring-time check at t=0; runtime motor creation also guards late spawning against actual live geometry. Independent player physics still occurs in ascending PlayerId order and stages updates transactionally.
