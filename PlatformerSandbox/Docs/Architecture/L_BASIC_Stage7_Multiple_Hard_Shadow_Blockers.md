# Lighting Basics — Stage 7: Multiple independent hard-shadow blockers

## Design goal

Extend the validated Stage-6 scene to **up to three rectangular shadow blockers** without modifying the production LDtk renderer, adding normals/heightmaps, introducing polygon triangulation, or changing any default lighting/material response. Keep the existing single-blocker test reproducible.

## Technical contract

- `BasicLightingPreviewState` owns three normalized [0,1] rectangles and three independent enabled flags. Slots are bounded; there is no unbounded allocation or dynamic topology editing in the lab.
- The existing `ShadowBlocker`, `ShadowBlockerEnabled`, and `ActiveShadowBlocker` refer **only to slot 1** for backward compatibility with Stage-6 tests and callers. New lab code uses `ActiveShadowBlockers` and selected-blocker setters.
- The `GameplayLightingRenderer.BeginBasicMaterialFrameWithBlockers` lab entry point takes a snapshot of all enabled blockers; its existing `BeginBasicMaterialFrame` entry point remains callable.
- The GPU shader binds each active rectangle into three explicit parameter slots (`ShadowRectPixels`, `ShadowRect1Pixels`, `ShadowRect2Pixels`), with `ShadowSlotEnable` clearing unused slots every draw. Unused parameters must never leak previous frame shadows.
- Each light tests the union of **only enabled blockers**. If one blocker is between a particular light and receiver, that light's direct contribution is hidden; ambient and other unoccluded lights are untouched.
- A light or receiver inside a particular rectangle skips that rectangle under the existing Stage-6 open-segment policy. Other rectangles continue to be evaluated. No geometric penumbra, height/depth, or self-occlusion is implied.
- Rectangular blocker visible faces are still independently front-lit surfaces with `ReceivesShadows=false` and are composited after the material samples to avoid blocker self-shadow.
- The selected blocker has a green outline; other enabled blockers have amber outlines. Each is numbered.

## In-game inspector

Run normally and choose **SELECT LEVEL → LAB / LIGHTING BASICS**. The **SHADOWS** tab has:

- `SELECT BLOCKER` dropdown: blocker 1, 2, or 3.
- `LIGHT n CASTS SHADOWS` and `SAMPLE n RECEIVES SHADOWS`: same independent light/material semantics as Stage 6.
- `SELECTED BLOCKER ENABLED`: independent for each slot.
- Selected blocker's X/Y, width and height sliders, or Shift + left drag in the scene.
- `LOAD SINGLE BLOCKER`: restores the Stage-6 comparison preset.
- `LOAD 3 BLOCKERS`: arranges three occluders with both lights casting, to validate union visibility and overlap.
- `RESET SHADOWS ONLY`: restores the original disabled defaults without resetting light colors, ambient, material modes or opacity.

## Visual acceptance

1. With defaults, compare visually to Stage 5/6 with all blockers disabled. Nothing should change.
2. Select `LOAD SINGLE BLOCKER`; verify exactly the original one-blocker amber/blue comparison is visible.
3. Select `LOAD 3 BLOCKERS`; verify three numbered, independently shadow-casting pillars. Changing any one must not teleport/resize either of the others.
4. Disable blocker 1 while leaving 2 and 3 enabled; their shadows must remain active. Repeat with blocker 2 and blocker 3 independently.
5. Select Light 1 and Light 2 in **LIGHTS**; inspect **SELECTED LIGHT** and **SHADOW MASK**. The mask must track each light and the union of active blockers, not globally darken both light contributions.
6. Disable casting for Light 1; Light 2's occlusion and ambient remain. Disable `RECEIVES SHADOWS` on sample B; B should receive its allowed light without casting-related darkening.
7. Move one light into a blocker. That blocker must be skipped for that light only, while the others continue to work.
8. Turn all blockers off and compare shaded sprites, transparency, albedo and rim with Stage 5. Check there is no residual shadow from a disabled slot.
9. Test the `LOAD SINGLE BLOCKER` and `RESET SHADOWS ONLY` buttons after all three were enabled; ensure no extra shadow persists.
10. Repeat with a low ambient and a single colored light for strong contrast. Performance should be measured in a real DesktopGL run.

## Headless tests and limitations

`LBasicStage7MultipleBlockersTests` verifies default compatibility, independent editing and enabling, bounds validation, comparison/reset behavior, per-light casting flags and reference CPU ray/rectangle union geometry. It cannot prove GPU shader compilation or raster output.

**Not part of this stage:** arbitrary polygons, concave blockers, moving authored obstacles, 2.5D height and depth, soft shadows/penumbrae, production LDtk integration, HDR/bloom and whole-image post-processing. These remain deferred milestones.
