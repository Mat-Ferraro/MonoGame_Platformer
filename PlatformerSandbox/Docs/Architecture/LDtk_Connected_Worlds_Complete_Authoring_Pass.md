# PlatformerSandbox — LDtk Connected Worlds and Authoring Consolidation

**Status:** implementation candidate; requires local .NET 10 build, xUnit suite, and an LDtk 1.5.3 editor-open test. This package builds upon the user-validated LDtk Phases 1–4, menu selection, HUD toggle, and static-floor precision fix.

## Scope of the one-pass update

1. **Visual authoring in LDtk:** `PaintedSprite` is a tileset-backed, resizable Entity. An actual illustration is selected via its `Art` Tile field and displayed on the entity in LDtk; MonoGame imports its tile source rectangle and renders it in the chosen Background / Terrain / Foreground plane with depth, opacity, rotation and parallax. The four illustration samples are in `Content/Levels/IllustratedTerrainAtlas.png`. This works alongside existing `ScenerySprite` and standalone-PNG `TerrainArtwork` entities; it does not change physics.
2. **Linked levels in one LDtk project:** `ConnectedWorldDemo.ldtk` has `CAVERN_A` and `CAVERN_B`, each with authored IntGrid solids, visual layers, large scenery, entry markers, exits, checkpoint, wind region and moving platform. Both appear in the existing **Select Level** UI; there are no new mandatory function-key shortcuts.
3. **Typed gameplay importer:** `LdtkConnectedWorldCatalog` reads every embedded level from the same JSON snapshot, preserves original collision merging, adds world-owned moving platforms/spawns/triggers, validates stable IDs and all exit targets before the candidate scene becomes active.
4. **Committed interaction behavior:** `LevelExit` requests the scene coordinator to change the level, after the committed trigger-enter event; `Checkpoint` updates just the entering player's next respawn marker; `WindZone` changes the next tick's environment velocity through `WorldMotionRequest` rather than changing the motor directly. No additional physics implementation or ECS.
5. **Debuggability:** In these two levels, `F9` draws collision bounds plus colored trigger outlines: amber for exits, green for checkpoint, blue for wind. Moving-platform bounds are visible. F10 shows/hides the gray collider backing; F11 cycles HUD visibility. Debug shortcuts `1` and `2` can test the two exits without having to walk across the entire prototype. The old gym fixtures and shortcuts remain unchanged.

## Authoring contract — LDtk 1.5.3

The project must use normal embedded JSON, with `Collisions` (IntGrid, solid value=1, 32px) and `Entities` (zero-offset) layers. The `PlayerSpawn` marks the implicit `START` entrance. Additional entities in `Entities`:

| Entity | Required LDtk custom fields | Runtime meaning |
|---|---|---|
| `LevelEntrance` | `EntranceId` (String) | Named valid entry point. Resizable marker; character spawns with its feet at marker bottom. `START` is reserved for `PlayerSpawn`. |
| `LevelExit` | `TriggerId` (Int), `TargetLevel` (String), `TargetEntrance` (String) | Nonblocking volume that requests a validated deferred level transition on P1 entry. |
| `Checkpoint` | `TriggerId` (Int), `SpawnId` (String) | Nonblocking volume; its marker is also the next player-respawn location once touched. |
| `WindZone` | `TriggerId` (Int), `SpeedPercent` (Float) | Signed **additive velocity** as a percentage of normal walking maximum; +75% assists right movement and opposes left; -75% is the reverse. |
| `MovingPlatform` | `ColliderId` (Int), `VelocityX` (Float), `VelocityY` (Float), `TravelSeconds` (Float) | The engine's existing moving-platform trajectory with an authored starting AABB. |
| `PaintedSprite` | `Art` (Tile), `Plane`, `Depth`, `RotationDeg`, `Opacity`, `ParallaxX/Y`, `FlipX/Y` | Readable image preview in LDtk, runtime-only transforms. Does not contribute collision. |

Custom field names are case-sensitive. Entity instance IDs, spawn names, and collider/trigger IDs must be unique in their respective scopes. Exit `TargetLevel` and `TargetEntrance` must identify an existing destination. Starting character bounds must not intersect solid terrain or platform geometry. No invalid content is substituted with defaults: scene preparation fails and the previous scene remains playable.

### Visual/editing workflow

- In LDtk, open `Content/Levels/ConnectedWorldDemo.ldtk`, double-click `CAVERN_A` or `CAVERN_B` in the world view, choose the **Entities** layer, then select the `PaintedSprite` entity. Position/resize it with native editor handles and change `Art` using the tileset picker. Use `Plane` and `Depth` for ordering; fields like rotation and parallax are applied in MonoGame and should be inspected there.
- For individual transparent PNGs, `TerrainArtwork` still uses a `FilePath` and displays a placement rectangle rather than the image in LDtk. `PaintedSprite` offers native visual placement at the cost of packing artwork into a tileset; the atlas is included as a working example, not a required asset-conversion step for every project.
- Paint collision **only** on the `Collisions` IntGrid. Visuals never create gameplay colliders. Keep existing `BG_*`, `Terrain_Art`, `FG_*` layers for parallax tile art. Normal geometry never scrolls with parallax.
- Save the file, then restart the active scene with `T`. The scene factory reads a single snapshot of the LDtk project and prepares all data and textures before scene commit. The previous world survives invalid content loads.

## First integration test in MonoGame

1. Build the solution, open **Select Level → LDtk / Connected Cavern A**.
2. Toggle `F11` to Hidden to inspect composition and `F9` to see collision and interaction regions. Character movement, dash and the collision solver should behave identically with overlays on or off.
3. Press `1` or `2` to request each authored exit. The game should fade to `CAVERN_B` at `EAST` or `WEST` and preserve the session. Repeat in reverse. Normal walking into the trigger volume must request the same destination.
4. In A, move into the blue wind zone. Expected: 75% leftward environmental motion. Move outside to remove it. In B, the zone pushes right at 75%. Walking with the current can exceed normal speed; against it reduces net speed. Apply `B` dash while inside if desired.
5. Enter the green checkpoint, then press `R`. Only Player 1 respawns at that checkpoint, while WorldTick continues; a full `T` restart resets the world and returns to the selected entrance's initial state.
6. Edit a `PaintedSprite` location and save in LDtk. Press `T` in game and verify the visual placement changed without altering collision bounds.
7. Repeat the existing F7/F8/F9/F10 LDtk scenes and F4 movement/physics gyms to check regression isolation.

## Automated verification

Run `LdtkConnectedWorldImporterTests`, `GameLevelSelectionTests`, all pre-existing `Ldtk*Tests`, then the entire xUnit suite. Check the Visual Studio build output, especially errors (the old CS8632 nullable annotation warnings are separate cleanup work).

## Explicit limits / follow-up

- This integrates **two curated embedded levels**, not automatic runtime discovery of arbitrary user-created LDtk projects in the menu. New registered level identifiers need app/catalog wiring until a general dynamic content registry is designed.
- Trigger entry is currently evaluated on committed tick endpoints. A character traversing an extremely thin trigger in one tick may miss it; make authored volumes sufficiently wide. Swept-trigger logic is a separate physics feature and has not been silently invented here.
- Game art is still placeholder. This is an editor/pipeline and gameplay authoring foundation, not a finished seamless art system, full animation pipeline, lighting engine, particle implementation, or sound system.
- LDtk's native tileset can preview and resize illustrations; **per-instance runtime rotation and parallax should be checked in MonoGame** rather than assuming editor imagery reproduces every shader/camera effect.
- xUnit and GPU/LDtk editor opening are local acceptance gates; source and archive checks cannot substitute for them.
