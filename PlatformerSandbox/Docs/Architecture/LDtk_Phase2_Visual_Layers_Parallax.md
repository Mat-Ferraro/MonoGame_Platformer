# LDtk Phase 2 — Visual Layers, Parallax & Presentation Isolation

## Status
Implementation candidate for local .NET build, xUnit and interactive acceptance. Preserves the user's validated LDtk Phase 1 collision/spawn setup and the previously corrected single-player `UpdateWindowTitle` code. Neither test builds nor GPU rendering were executable in the preparation environment.

## What this implements

1. A headless JSON visual importer (`LdtkVisualImporter`) that reads LDtk 1.5.3 Tiles/AutoLayer instances separately from the authoritative `LdtkLevelImporter`.
2. Named draw planes: `BG_*` behind geometry, `Terrain_Art` around terrain, `FG_*` above the player. Layer order within a plane follows LDtk's topmost-first order reversed for SpriteBatch.
3. LDtk tile positions and source rectangles, tile alpha, layer opacity, X/Y flips, per-layer X/Y parallax factors and optional parallax scaling.
4. A scene-owned PNG loader (via MonoGame `Texture2D.FromStream`) that validates paths/dimensions and disposes resources on scene exit, restart, or failed preparation. Invalid files do not replace the current playable scene.
5. One shared camera transform for visible world geometry and artwork, Y-up physics converted to Y-down only for rendering; resize changes camera presentation, never physics.
6. F9 toggles a collision-bounds overlay in either LDtk scene; no other F9 fixture changes.
7. A separate 2560 × 1280 LDtk `ParallaxDemo.ldtk` with six named visual layers and a **temporary, original** 512 × 512 PNG atlas (`ParallaxPlaceholderAtlas.png`). The demo is intentionally a low-fidelity engineering fixture, **not final game art**.

## Existing source preserved

- **F7 at Title:** original `FirstAttempt.ldtk` (not replaced by this package). It still has hidden IntGrid physics and its independent PlayerSpawn entity.
- **F8 at Title:** new `ParallaxDemo.ldtk` to exercise visual layers and camera scrolling.
- **F8 during gameplay:** existing keyboard/gamepad input-source selector.
- F1–F6 gyms, F4→F1/F2, Route Lab/F9 stress testing, scene flow, events, A08–A12, physics solver and WorldSession unchanged.

## Authoring contract

### Layers

| Layer name | LDtk type | Rendering | Recommended factor X |
|---|---|---|---:|
| `FG_Close` | Tiles or AutoLayer | Above player | +0.35 |
| `FG_Near` | Tiles or AutoLayer | Above player | +0.17 |
| `Terrain_Art` | Tiles or AutoLayer | Before player | 0 |
| `Collisions` | IntGrid | Authoritative physics; auto tiles also allowed as fallback terrain visuals | 0 |
| `BG_Near` | Tiles or AutoLayer | Behind terrain | −0.30 |
| `BG_Mid` | Tiles or AutoLayer | Behind terrain | −0.58 |
| `BG_Far` | Tiles or AutoLayer | Farthest visible layer | −0.80 |

Any `BG_*` and `FG_*` prefix is accepted. Existing `Entities` contains the `PlayerSpawn` and is gameplay-only; arbitrary entity artwork is not interpreted yet. Unrecognized populated visual layers fail validation rather than silently rendering in the wrong order.

A factor of `0` is world-locked. Negative values scroll more slowly; positive values scroll more quickly. Horizontal and vertical factors are independent. `Scale?` can be enabled for layer zoom around camera center; disabled keeps tile sprites at authored pixel dimensions. These rules are a practical runtime representation; LDtk editor preview scaling/camera boundaries can still differ subtly from MonoGame.

Use transparent PNGs, preferably larger organic overlay pieces, rather than assuming collision-cell-sized visible tiles. For this pass, the built-in placeholder atlas uses 128px art pieces with alpha. Actual free-positioned decor entities, animation, rotation, lighting, and final render-pass integration remain future milestones.

### Runtime asset paths

Tileset image paths must be relative to their `.ldtk` project file (for example `ParallaxPlaceholderAtlas.png`) and point to a PNG. Unsafe parent traversal or absolute paths are rejected. PNG dimensions must match LDtk's tileset metadata. All source rectangles must fit the atlas.

### Ownership and reload

At a safe scene-transition boundary, the importer reads one JSON snapshot, validates both collision and visuals, prepares GPU images, then creates the world. On failure, the previous scene remains intact. When the scene is replaced the previous scene's textures are disposed. `T` restarts the current LDtk scene and reloads saved JSON/PNG files. No hot reload during an authoritative physics tick.

## Interactive acceptance steps

1. Install the ZIP at project root, preserving your own `Content/Levels/FirstAttempt.ldtk`.
2. Build the solution and run `LdtkVisualLayerTests` plus existing `LdtkLevelImporterTests` and the complete test suite.
3. Title → **F7**: original authored level still moves/jumps/respawns/restarts unchanged.
4. Title → **F8**: parallax demo loads with six layers and visible original placeholder art. The HUD reports 107 visual tiles.
5. Move **right** long enough for camera scrolling (demo is deliberately 2560px wide). Distant background should shift less than terrain; near foreground should shift faster. Jump to inspect Y parallax; the demo is also tall enough for vertical camera travel.
6. Press **F9** to reveal yellow collision outlines, then again to hide them. Movement and player state must remain identical.
7. Open `Content/Levels/ParallaxDemo.ldtk` in LDtk. Move a visual tile, save; return to game and press **T**. Artwork changes without touching the physics C# source.
8. Temporarily rename the PNG file, press **T** to trigger preparation failure. The previous scene should remain playable. Restore the file and press T again; reload should work.
9. Return to title, launch F4/F2 and F4/F1, and rerun F9 Route Lab smoke test to confirm old fixtures are unaffected.

## Deliberately deferred

- Free-placed/rotated/scaled decoration entities and overlapping arbitrary-size sprite placements outside Tiles/AutoLayer grids.
- Entity/tile animation, normal maps, scene lighting/shadows, custom shaders, streaming, draw-batch optimization.
- Collision shapes more complex than Phase 1's authoritative grid-to-AABB union.
- Separately stored LDtk external level JSON, simplified export mode, arbitrary atlas formats and auto-import of LDtk `.ldtkl` metadata.
- Fully designed art style: the provided atlas is a functional placeholder only.

## Acceptance policy

No motion/collision regression, no unintended edit to the user's `FirstAttempt.ldtk`, correct background/terrain/character/foreground ordering, observable parallax in both axes, scene-local texture cleanup and failure recovery, deterministic headless visual parsing and transform tests.
