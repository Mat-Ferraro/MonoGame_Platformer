# Stage 12 — per-light shadow visibility for polygon back-rims

## Reported visual issues

1. Blue/amber back-rims remained visible on a wall even when a different wall stood between the light and the highlighted edge.
2. Narrow bright pixels still appeared on edges facing away from the backlight, particularly on triangle/trapezoid corners.

These arise because the Stage 11 shader added `backRim` independently of shadow visibility, and because its corner handling could still select a light-facing edge close to a dark-facing edge. The wall's `ReceivesShadows = false` setting prevented an ordinary visibility lookup from being reused directly: that setting exists to avoid self-shadowing on the wall face.

## Stage 12 contract

- **Each back-rim contribution is multiplied by the same per-light visibility used by diffuse shading.** The shadow caster for the *currently drawn wall* is excluded from its own rim ray; every other active polygon still participates.
- GPU slots are the **compacted active shadow array**, not LDtk/editor blocker indexes. `BasicLightingLab` supplies the exact current slot, and `GameplayLightingRenderer` validates that it matches the wall and binds `WallRimSelfSlot` for each draw. No coordinate-based matching or persistent GPU parameter reuse.
- The visibility remains opt-in per light: disable **Light N Casts Shadows** and that light ignores blocker occlusion for both diffuse and back-rim. The wall's `ReceivesShadows = false` does not bypass back-rim occlusion. Light 1 and Light 2 remain independent.
- For an external light, each edge faces the light according to the signed outward normal and direction from the nearest point on that segment. For an interior overlapping light, direction comes from vertex-centroid to the light. A perfectly centered light has **no preferred rim direction**, so no rim is drawn (instead of an undirected ring).
- An edge that faces away from the backlight has **zero** rim. If such a dark-facing segment also lies within 2 screen pixels of the nearest light-facing segment at a corner, the dark edge vetoes the rim to prevent corner wrapping. This deliberately creates a small, clean dark corner rather than a stray bright pixel.
- No change to the original Stage 8 geometry/clipping, Front/Behind rules, normal material mapping, ambient light, light receiver channels, production LDtk compositor, or stage presets.

## Visual acceptance

1. Open **Select Level > LAB / LIGHTING BASICS > Shadows > Light Rules**.
2. Choose **Load Depth Rim Demo**. The Stage 11 demo intentionally turns *both lights' shadow casting OFF* to isolate rim appearance. To test rim occlusion, select Light 1 and/or Light 2 via **Lights** and enable **Casts Shadows** for the corresponding light under **Shadows > Light Rules**. For blue back-rim, enable Light 2 casting specifically; toggling only Light 1 does not make blue rim cast shadows.
3. Move the light so that an *additional* wall sits between the light origin and the rim-receiving wall. The occluded rim section must disappear, while unobstructed sections retain their color. Move or disable the intermediate wall: only that occlusion returns.
4. Verify the selected wall never self-occludes its own rim. Verify Light 1 and Light 2 independently, including one casting shadows while the other does not.
5. Move an external backlight around a triangle and a trapezoid. The rim follows only the facing edge; the away-facing edge and any adjacent corner pixels remain dark. Test directly inside a wall as well, including the perfectly centered origin (no artificial all-around ring).
6. Verify **One**, **Three**, **Poly**, and **Front/Behind** demos still work with their original settings.

## Boundaries

- Three convex shadow slots, hard binary visibility, no soft penumbrae or height-dependent penumbra.
- Only the Lighting Basics lab is modified. Full 2.5D inter-layer shadow ordering for LDtk is a future phase.
- No .NET SDK or MonoGame GPU runtime was available to compile the effects in the packaging environment. Headless reference tests and static checks do not replace a DesktopGL visual test.
