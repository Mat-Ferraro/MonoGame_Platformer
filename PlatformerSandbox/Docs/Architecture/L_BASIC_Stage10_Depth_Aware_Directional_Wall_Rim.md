# Stage 10 — Depth-aware directional wall rim and clean editor overlays

## Problem observed

Stage 9 outlines use an opaque editor gizmo pass: teal for the selected blocker, amber for the others. They are **not the lighting rim** and can cover the rim in Final view. Separately, Stage 9 only suppresses a wall's light response if the light's XY origin is inside the silhouette and is assigned Behind mode. That is insufficient for a lower-depth light outside a foreground wall.

## Changes

1. Editor blocker outlines and numeric wall indices default to **hidden**. They can be toggled from `Shadows > Light Rules > SHOW WALL DEBUG OUTLINES`. Disabling outlines does not prevent click-select/drag or change the actual wall silhouette.
2. Three explicit *virtual depth planes*, ordered back to front: `BACK (LOW)`, `WORLD (MIDDLE)`, `FRONT (HIGH)`. Every light and wall blocker has its own independently authored plane; existing default is WORLD for all. The scene still draws walls in authored order. This is a light-to-wall material response rule, not a complete sorted depth renderer.
3. For one wall and one light:
   - If light depth is **lower** than wall depth: this light's front diffuse and material rim contributions are suppressed on this wall, **regardless of XY overlap**. It supplies only a colored directional *back-rim* contribution along the silhouette edge facing the light. The ambient and other lights remain independent.
   - If depths are **equal**, the original Stage 9 Front/Behind-with-Rim choice applies **only when the light origin lies inside the exact polygon silhouette**. Outside that origin-overlap, ordinary light/shadow behavior is preserved.
   - If light depth is **higher**, the wall receives ordinary illumination from that light (subject to its normal material and visibility settings). The equal-depth overlap dropdown does not override the explicit depth order.
4. Rim edge direction is derived from the wall's captured alpha silhouette, not the old uniformly bright outline. For a light outside the wall, the rim emphasizes contours facing the light. For an origin inside the wall, the rim direction follows the origin's offset from the wall center; exactly centered origins receive a balanced rim to avoid a direction singularity.
5. Existing Stage 6–9 presets and old `GetWallLightOverlap(shape)` API remain backward-compatible by resetting/assuming WORLD depth. A new **LOAD DEPTH RIM DEMO** places a blue light outside a foreground wall but behind its virtual depth.

## Controls

Go to `Select Level > LAB / LIGHTING BASICS`, then `Shadows > Light Rules`:

- `LIGHT n DEPTH LAYER`: BACK / WORLD / FRONT for whichever light is currently selected in the Lights tab.
- `BLOCKER n DEPTH LAYER`: BACK / WORLD / FRONT for the selected blocker in Shadows > Blockers or by clicking a blocker.
- `IF SAME DEPTH + ORIGIN INSIDE`: FRONT OF WALL / BEHIND WALL + RIM (Stage 9 policy).
- `LOAD FRONT / BEHIND`: Stage 9 original overlap comparison.
- `LOAD DEPTH RIM DEMO`: new non-overlapping layer comparison.
- `SHOW WALL DEBUG OUTLINES`: OFF by default, ON for selection outlines and indices.

To inspect the rim, select `Final Image`, switch off outlines, load the depth demo, and move the blue light horizontally on the left side of Blocker 3. Turn ambient down and disable Light 1 if necessary to isolate the blue contribution. The rim should follow the light-facing silhouette, not paint a full-width face highlight. Move the light to WORLD or FRONT and compare: diffuse lighting on the wall should return.

## Scope and limitations

- **Lab-only**: these explicit depth planes are not yet connected to LDtk scene layer IDs or production draw ordering.
- Per-light material response is controlled on the *wall that is being drawn*. The background and material samples still use their Stage 9 light routing.
- **Shadow projection** is unchanged: exact XY light-origin overlap continues to suppress that wall's projected shadow as before; a light farther back but outside the shape still uses normal occlusion tests for other receivers. Full inter-plane receiver-aware occlusion remains future work.
- Inner silhouette rim only (no bloom or external halo yet). This is not physically based ray-traced lighting.
- Inspector outlines use editor colors and are excluded from the shader passes; when enabled they may mask the light-generated rim.

## Acceptance

1. The Stage 8 polygon preset draws neutral surfaces without neon wall borders when outlines are OFF; click walls to select and move them anyway.
2. Toggle outlines ON: selected wall is teal, others amber. Toggle OFF: only real material lighting remains.
3. Depth demo: the blue light lies outside Blocker 3 but is BACK relative to the FRONT wall. Its visible wall face stays at ambient/other-light brightness, with a blue contour only on the light-facing edge.
4. Move the blue light to the other side of the wall without changing layer: highlight direction changes; normal front-face illumination from the blue light must not appear.
5. Change the blue light depth to FRONT: wall becomes face-lit by blue light, and the depth-driven back-rim disappears.
6. Original overlap demo with all depths WORLD: amber FRONT wall is lit; blue BEHIND wall has dark face and special rim while its origin is inside the polygon.
7. Disable the blue light: its wall rim disappears; amber illumination is unchanged.
8. One / Three / Poly presets restore WORLD for both lights and every wall so older acceptance tests are reproducible.
9. Build and run the full suite; visual shader acceptance must still be done on DesktopGL because unit tests cannot verify blending or real GPU shader compilation.
