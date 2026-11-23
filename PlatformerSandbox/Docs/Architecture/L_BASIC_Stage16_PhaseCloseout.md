# Phase 16 — Production lighting optimization closeout (Stage 16C)

## Implemented sequence

- **16A: Safe shadow-caster broadphase.** Reject geometry conservatively outside effective light influence, and avoid masks with no relevant casters. Counts: `PRUNED`, `MASKS SKIPPED`.
- **16B: Material fallback fast path.** Reuse neutral 1×1 normal, edge, and visibility textures in place of redundant full-target clears. Dynamic visibility remains per-surface, with exact self-caster exclusion. Counts: `NEUTRAL MAPS`, `WHITE MASKS`.
- **16C: Material raster regions (this package).** Compute a conservative padded region from the *actual* projected artwork anchor, pivot, width, height, and rotation. Clamp it to the viewport, and skip entirely off-screen materials. Shade the **full-viewport sprite with a scissor rectangle**, preserving the shader's global UV coordinates. Composite only the same-pixel source/destination rectangle. Scissor is restored immediately and no LDtk layout, shader, material selection, shadow logic, or layer order changes.

## Why full-viewport UVs matter

The material shader computes pixel light direction, polygon rim distance and shadow-visibility UVs in screen coordinates. Simply resizing `_world` into the smaller region would remap those coordinates and break the light/shadow alignment. In 16C the original full-screen draw and render targets remain intact; the rasterizer clips fragment processing. This is a safer first step than a tiled or suballocated material atlas.

## Diagnostics

`L16C MATERIAL REGION: DRAW D OFFSCREEN O SHADED PX X AVOIDED PX Y`

These counts track regions and *potential shaded fragment area* saved over D full-viewport shader draws. They are not GPU timing, and do not include opaque/transparent overdraw, mask generation, render-target clears, or driver overhead. The same scissor is not applied to visibility fans (their full-screen mapping is required). Existing 15C rolling CPU timing should be compared at a fixed viewport / camera, but GPU-speed claims require real device measurements.

## Windows acceptance matrix

1. Build the DesktopGL project and run the test suite. Open `LIGHTING_INTEGRATION_TEST`.
2. In `DEV TOOLS` enable Full HUD. Confirm material count and 16A/16B counters are unchanged. For the two small panels, `SHADED PX` should be much smaller than two screenfuls and `AVOIDED PX` should be positive.
3. Move the camera so the normal panel and pillar enter and leave the viewport. They must not pop in late or clip at the edges; off-screen regions should increment only when their artwork is completely invisible.
4. Verify the green pillar's back rim, projected shadow from other blockers, and dark front face. Ensure no black tiles or duplicate material ghosts appear around it or along scissor boundaries.
5. Check `Lighting Basics`, `Lighting Flat Test` (direct launch if hidden in selector), foreground graphics and the player's compositing. Scissor state must never leak into other passes.
6. Toggle low/balanced/high and stress via **DEV TOOLS** (no hidden key sequences). Compare CPU P95 over multiple steady-state windows; keep resolution and camera position fixed. Inspect for draw/RT errors during resize.
7. Compare screenshots against Stage 16B at the same camera and configuration: visually they should be equivalent, within rasterization edge tolerance.

## Intentional boundaries / deferred items

- The full-resolution albedo, authored map, actual visibility-mask render targets **still exist** and are still cleared when used; this pass reduces only *material shader fragment shading and final material composite fill*. Measured device gains may be small for the current two-panel fixture.
- The current material shader selects up to **two lights per surface**. This is a known feature budget, not a Phase 16 optimization defect. Supporting many simultaneous surface lights requires an explicitly designed additive/accumulation path with correct ambient-once behavior, per-light masking, per-wall depth and self-caster rules. Do not silently raise `maximum=2` without changing the shader.
- Geometry rotation supported for flat authored surfaces; advanced tangent-space normal-map/edge-map rotation still has the existing authoring restriction.
- No soft shadows, HDR/bloom, atlas/tile suballocation, performance GPU-query instrumentation, or speculative engine-wide batching introduced in Phase 16.
- Phase 16 code completion is **not** GPU validation: `.NET`, test execution, DesktopGL rendering, and performance comparisons must be run on the target workstation.
