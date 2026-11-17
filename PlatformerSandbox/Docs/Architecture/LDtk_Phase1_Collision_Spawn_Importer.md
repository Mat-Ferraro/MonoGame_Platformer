# LDtk Level Authoring — Phase 1: Collision and Player Spawn

**Source:** The uploaded `FirstAttempt.ldtk` (LDtk 1.5.3).
**Runtime:** existing MonoGame DesktopGL host + headless Gameplay `WorldDefinition` + `WorldSession`.
**Status:** implementation candidate; local .NET build, xUnit suite, and interactive verification required.

## Uploaded asset facts
- Saved internal level ID: `Level_0` (the filename `FirstAttempt.ldtk` is separate).
- Level bounds: 1280 × 640 LDtk pixels, Y positive downward.
- `Collisions`: IntGrid, 32-pixel grid, 40 × 20 cells, values 0 (empty) and 1 (`Solid`).
- 85 solid cells, deterministically merged to **21 AABB colliders**. No collision mesh/tiling renderer is needed.
- `Entities`: one `PlayerSpawn` marker, top-left pixel `(320,160)`, 32×32 marker, pivot `(0,0)`.
- Imported character physics box: 0.8 × 1.6 **world units**, centered horizontally on marker, feet at marker bottom. World coordinate bounds: left=10.1, bottom=14.0, right=10.9, top=15.6. (1 world unit = 32 LDtk pixels.)
- No tileset, artwork, or background/foreground parallax content in this file yet.

## Runtime contract

`LdtkLevelImporter.FromJson/FromFile` validates the embedded project JSON, one selected level, collision grid, single marker, grid offsets and spawn penetration. It returns a `LdtkImportedLevel` containing a validated `WorldDefinition`. C# Gameplay stays headless; it only needs `System.Text.Json`, **not MonoGame graphics or LDtkMonogame**.

`LdtkLevelGym.Create` constructs the existing authoritative world motor with baseline movement constraints and assigns one player at the authored start spawn. This first step intentionally has **only one player**, because the LDtk file has only one `PlayerSpawn`; older gyms and their independent two-/three-player fixtures are untouched.

`LdtkLevelRenderer` displays placeholder collision geometry and character bounds at 32 screen pixels per world unit, with a camera bounded by the authored level. It is purely presentational; the real physics colliders come from `WorldDefinition`.

### Fixed coordinate conversion

LDtk uses `X right / Y down` in pixels. Simulation uses `X right / Y up` in world units. For any LDtk rectangle with top-left `(x,y)`, width `w`, height `h`, and level height `H`:

```
left   = x / 32
right  = (x+w) / 32
bottom = (H-y-h) / 32
top    = (H-y) / 32
```

PlayerSpawn is an **authoring marker**, not the character's physical hitbox. Its rectangle bottom defines the feet position.

### Collider IDs and merging

The importer first forms horizontal spans of solid cells and then extends *matching* spans vertically. Each merged rectangle is assigned ID `10000 + firstCellRow*gridWidth + firstCellColumn + 1`. This uses stable authored cell locations rather than a transient sequential ID.

## Playable acceptance test

1. Extract package at solution root and build.
2. On title screen choose `PLAY LDTK TEST LEVEL`, or press **F7** (or `--dev-level=LDTK_TEST`).
3. See a dark gray level containing gray physics terrain and one cyan character. Diagnostics should read `LEVEL: Level_0`, `SOLID CELLS: 85`, `MERGED COLLIDERS: 21`.
4. Walk and jump (A/D + Space or F8 controller) on the LDtk geometry. R respawns, P pauses.
5. Exit to Title, open the *repository copy* `Content/Levels/FirstAttempt.ldtk` in LDtk, move/add/remove some solid cells **without removing the PlayerSpawn**, save, then launch F7 again. The geometry should reflect the new file; the executable doesn't need rebuilding.
6. While in the LDtk scene, press **T** after saving to reload/restart it using the latest file. If the edit is invalid, the coordinator should preserve the previous scene and show the load error.
7. Return to older F1–F6 gyms and verify basic movement/level routes still work.

## Automated regression tests

Run `LdtkLevelImporterTests`, then the existing scene, motion, collision and full solution suites. The test project has a **frozen** copy of the uploaded LDtk file in `PlatformerSandbox.Tests/Fixtures/LdtkFirstAttemptBaseline.ldtk`, so **editing the playable file won't break baseline test assertions**.

## Deferred by design

No tilemap art, free-placed illustrated sprites, autotile rendering, parallax, lighting, animated scenery, multiple LDtk levels/entrances, variable entity pivot support, other entity types, or LDtk external-level exports in Phase 1. All unsupported authored conditions fail clearly, rather than silently losing data. Next: graphics layers and parallax (foreground & background) compatible with illustrated, non-tile-looking environments.

### Why not use LDtkMonogame here?

The package the user installed remains in DesktopGL. The gameplay collision importer deliberately uses standard JSON so our established headless tests and world definitions do not take a MonoGame dependency. We can use LDtkMonogame selectively in the future renderer if helpful, without moving gameplay logic into graphics.

## Source layout

- New `PlatformerSandbox.Gameplay/Content/LdtkLevelImporter.cs` and `LdtkLevelGym.cs`.
- New `Source/Gyms/LdtkLevelRenderer.cs`.
- Full replacement `Source/Game1.cs`, `GameSceneCoordinator.cs`, `GameUiNavigation.cs`.
- Editable `Content/Levels/FirstAttempt.ldtk` and frozen regression fixture.
- New `PlatformerSandbox.Tests/Gameplay/Content/LdtkLevelImporterTests.cs`.

**Limitations:** A level with a 32-pixel collision grid can still contain more elaborate artwork later. That grid size is only for this particular test import contract; the rendering system is not restricted to 32-pixel tiles.
