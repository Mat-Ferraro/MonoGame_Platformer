# LDtk Phase 4 — Organic Terrain Artwork & Compositing

**Status:** one-pass integration candidate; user build/LDtk open/runtime QA pending.

## Goal

Allow illustrative, overlapping 2D terrain to be authored independently from hidden `Collisions` IntGrid data. Maintain correct presentation ordering, collision physics, parallax, camera movement, and safe scene replacement. Do not create a second physics system, general ECS, or custom level editor.

## Data & authoring

- The new resizable `TerrainArtwork` entity lives on LDtk's existing `Entities` layer. Its *native* LDtk `px`, `width`, `height`, and `__pivot` are the artwork transform handles.
- Entity `Image`: LDtk **FilePath**, expected to name a **relative `.png`** in the same project folder tree. Path traversal, absolute and non-PNG paths are rejected. Missing PNGs fail scene preparation, preserving the old active scene.
- `Plane` = `Background` / `Terrain` / `Foreground` (aliases `BG`/`FG`). Terrain drawings move with the world (ParallaxX/Y 0) unless the author deliberately changes it. Background/foreground may move at different rates.
- `Depth` (int -10000..10000), `RotationDeg` (-360..360), `Opacity` (0..1), `ParallaxX/Y` (-100..200), `FlipX`, `FlipY`, `Tint` (`#RRGGBB`) are validated. Artwork within a plane sorts by Depth and then stable IID, across both standalone PNG artwork and prior `ScenerySprite` instances.
- LDtk *level custom Bool field* `DrawCollisionBackdrop` defaults **true** when absent (preserves older projects). When false, the renderer omits the gray collision filler shapes; actual colliders are still authoritative and visible by F9 outlines.
- Standalone images are deduplicated with tile/scenery textures, checked for dimensions and owned/disposed by the scene. Render code never mutates WorldSession.

## Draw sequence

1. Background tile layers, then background scenery + standalone art sorted by Depth/IID.
2. Optional debug graybox collider backdrop (F10), terrain tile layers, then terrain scenery + standalone art.
3. Player rendering and all authoritative gameplay-independent visual snapshots.
4. Foreground tile layers, then foreground scenery + standalone art.
5. Optional F9 collider outlines; debug HUD, UI, pause and transitions last.

The legacy graybox mode is preserved for the earlier LDtk projects. Art-only terrain is an explicit level-specific choice, never inferred because some visual tiles exist.

## Demo and acceptance

- **F10 from Title:** `Content/Levels/TerrainDemo.ldtk`. Separate from `FirstAttempt.ldtk`, `ParallaxDemo.ldtk`, `SceneryDemo.ldtk`.
- Demo uses original, placeholder rock faces and hanging foliage in four PNG files, placed as nine independently transformable entity instances. The existing collision map, spawns, parallax tiles, and prior scenery remain.
- Confirm: F10 in-game toggles graybox backing (visual only); F9 toggles collision outlines; F11 hides debug UI; moving/jumping still collide with the same geometries; PNG move/rotate/resize/tint and saved LDtk fields reload on T; returning to title and re-entering doesn't leak content.
- Invalid/missing art PNG rejects scene preparation; active world stays playable.
- Run `LdtkTerrainArtworkImporterTests`, previous LDtk tests, then whole suite.

## Known limitations

- **LDtk does not automatically preview an arbitrary PNG selected by a `FilePath` entity field in world view.** The editor shows resizable entity handles; final art is rendered by MonoGame. To preview exact art in LDtk, import the image as a tileset and use the existing `ScenerySprite` `Art` Tile field; both workflows remain supported.
- Placeholder art is not a polished visual style, and its exact contours do not replace the importance of testing reachable surfaces and collision silhouettes.
- No runtime visual masking, stencil/normal mapping, lighting pipeline, arbitrary Photoshop-layer imports, hot reload without restart, new camera, or automatic collision generation from artwork. These remain separate milestones.
