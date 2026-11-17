# LDtk Phase 3 — Resizable Scenery Sprites (one-pass implementation candidate)

## Goal

Support overlapping, oversized, independently rendered artwork for illustrative environments, without making any visual layer or sprite a physics collider. Preserve Phase 1 collision+spawn imports, Phase 2 BG/Terrain/FG tile rendering and X/Y parallax, existing scenes, and the debug HUD controls.

## Authoring schema

- `ScenerySprite` is a resizable LDtk **Entity**, placed in the existing `Entities` layer.
- Native LDtk position `px`, `width`, `height`, and `__pivot` control the rendered anchor/size. The image is selected using the `Art` Tile field (fallback: built-in `__tile` preview).
- `Plane`: `Background`, `Terrain`, `Foreground` (also accepts `BG` / `FG`). Determines draw pass; uses scenery-only ordered depth within a plane.
- `Depth` (integer, -10000 to 10000): lower draws earlier among scenery sprites **in the same plane**; identical depths sort by stable instance IID.
- `RotationDeg` (-360..360): clockwise screen-space rotation around the entity pivot.
- `Opacity` (0..1), `FlipX`, `FlipY`.
- `ParallaxX`, `ParallaxY` (-100..200): percentage relative to normal camera travel. -75 means 0.25x camera speed; +35 means 1.35x camera speed.
- Source art is selected from a project-relative PNG LDtk tileset, validated against the atlas dimensions. Large displayed sizes are independent of the source texture-rectangle size.

Runtime draw sequence per frame:

1. BG Tiles/AutoLayers, then BG ScenerySprites in Depth/IID order;
2. graybox collider fallback and Terrain art tiles, then Terrain ScenerySprites;
3. player sprites;
4. FG tiles, then FG ScenerySprites;
5. optional collision outlines, followed by UI and transition overlays.

Sprite depth currently orders **scenery relative to other scenery** in the same plane, not between arbitrary LDtk tile layers. This is intentional: it preserves Phase 2 stable scene draw order. More granular layer slots can be added after real content requires them.

## Runtime boundary

`LdtkSceneryImporter` belongs to Gameplay/Content and parses only presentation data. It never creates `WorldDefinition`, colliders, triggers, players, or motors. `LdtkVisualSceneAssets` owns all scene textures, deduplicates shared atlases across tiles and scenery, and disposes them at scene exit. A bad tile reference or invalid visual field rejects scene preparation and preserves the old active scene.

## Demo

- **F9 at Title:** `Content/Levels/SceneryDemo.ldtk` (internal identifier `SCENERY_DEMO`).
- **F8 at Title:** unchanged original `ParallaxDemo.ldtk`.
- **F7 at Title:** unchanged `FirstAttempt.ldtk`.
- The new demo uses the existing 512x512 placeholder atlas and contains 8 resizable art entities (3 BG / 2 Terrain / 3 FG), varied sizes, rotation, transparency, depth and independent horizontal/vertical parallax.
- F9 **inside** an LDtk level still toggles collision outlines; F11 cycles Full/Compact/Hidden; backtick cycles HUD globally.
- In LDtk, open `SceneryDemo.ldtk`, select `Entities` and click or resize a `ScenerySprite`; edit its `Art`, `Plane`, `RotationDeg`, `Opacity`, `ParallaxX/Y`, `Depth`. Save, then press **T** in MonoGame to reload the current level. Rotation/parallax are runtime transformations; LDtk's editor preview shows the tile and resize bounds, not necessarily those transformations.

## Tests

`LdtkSceneryImporterTests`: imported count/plane, size/source independence, stable order, camera transform, proper art tile selection, validation, physics isolation, old tile import preservation, and missing-level selection. Also run `LdtkVisualLayerTests`, `LdtkLevelImporterTests` and the full xUnit suite.

### Manual QA

- Verify F7 and F8 are unaffected; F9 displays overlapping art on a live scrolling scene.
- Walk/jump underneath FG decorations and above BG decorations, press F9 to see unchanged collision geometry.
- Move, resize and rotate an entity; save and press T to reload without rebuilding.
- Try changing Plane and Depth; scenery should change draw order without collision changes.
- Verify F11/tilde hide diagnostics and Pause always displays as before.
- Exit to title and enter several times; watch for stale textures, crashes, or disappearing visuals.

No custom engine editor, per-pixel collision, shadows, lighting, sprite masking, or arbitrary PNG-per-entity field has been added. Those are potential later content/presentation milestones.
