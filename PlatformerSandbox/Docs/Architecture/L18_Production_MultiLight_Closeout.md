# Phase 18 — Production Multi-Light GPU Materials

## Scope and reason for the change

Previously the production GPU-material bridge selected only the two strongest lights reaching each opted-in LDtk artwork surface. The 2-slot shader was therefore a *per-material* hard cap even though the gameplay presenter and global quality budgets supported 4/8/12 selected lights.

Phase 18 evaluates up to **12 relevant lights per production surface** (the existing High global budget) by reusing the proven two-slot material shader over sequential additive passes. `LIGHTING BASICS` remains the intentional two-channel analytic reference. This is **not** physically based HDR lighting and does not change the global quality budgets.

## Renderer contract

1. The gameplay presenter determines camera-visible lights under the existing quality budget and preserves its shadow-light quota.
2. `ProductionSurfaceLightSelection.Select(..., maximum: 12)` filters to lights routed to the receiving layer and with nonzero center influence; it ranks by attenuation and stable ID.
3. The surface's albedo, optional normal map and optional edge map are captured **once**. Empty maps retain Stage 16B neutral textures.
4. The first clipped material draw writes **ambient plus alpha only** to the material result target (`ProductionLightPassMode=1`).
5. Selected lights are processed in pairs, each using shader slots 0 and 1. Each pair regenerates *its own* per-light visibility masks, with exact per-surface self-caster exclusion and the Phase 16/17 shadow pruning and contact-hardening filter. Each additive pass writes only RGB, with alpha zero (`ProductionLightPassMode=2`).
6. The result is alpha-composited into the ordered LDtk canvas **once**, retaining the Stage 16C screen-space bounds/scissor optimization.
7. Shader mode 0 preserves the Stage 14 lab material path; its analytic polygon limit and UI behavior do not change.

**Order of operations matters:** each pair must finish shading before its mask targets are reused for the next pair. The shader must receive fresh light, depth/rim and visibility parameters for every pair; it may not reuse an earlier surface's state. Zero lights must still receive the single ambient pass. Odd last batches must zero the unused second-light slot.

### Light and quality policy

| Quality | Presenter maximum visible lights | Production material maximum | Projected shadow-light quota |
|---|---:|---:|---:|
| Low | 4 | 4 | 0 |
| Balanced | 8 | 8 | 3 |
| High | 12 | 12 | 5 |

The `MATERIAL LIGHTS: COMPARE 2` DEV TOOLS option intentionally restores the former **per-material** two-light cap for immediate visual A/B comparison. `ALL` is the default. This switch does **not** alter the general scene's light budget or layer lighting; only the opt-in GPU materials change.

## Phase 18 LDtk acceptance fixture

The existing `LIGHTING_INTEGRATION_TEST` remains playable and preserves its moving platform, three moving/rotating blockers, normal-mapped panel, casting hybrid pillar, and original four lights. Seven low-intensity authored lights have been added **to the PLAYER lighting layer only**, so the added sources primarily exercise the opt-in surfaces rather than washing out the entire background.

- **Normal-mapped panel:** about X=1500–1724 pixels, center X=1612. It has **five** eligible lights at its center, in distinguishable warm/orange, purple, lime, rose, and original pink tones.
- **Hybrid pillar:** about X=2350–2462 pixels, center X=2406. It has **four** eligible lights: the original green rear light, an amber rear light, a violet rear light, and a blue front light. This checks that lower-depth sources contribute back-rim but not front-face diffuse lighting, while the front light still illuminates the face. Other casters must still occlude rear rims.
- The dynamic crate, moving triangle and rotating trapezoid remain available; no additional gameplay collision or scripted state is introduced by the new emitters.

## Manual DesktopGL acceptance

### A. Core rendering / functional correctness

1. Install over a known-good **Phase 17C/D** build. Run `py tools\l18_fixture_preflight.py`, compile DesktopGL and run the full test project.
2. Open **LIGHTING_INTEGRATION_TEST**. In DEV TOOLS choose `QUALITY: HIGH`, `STRESS LIGHTS: OFF`, `LIGHTING: ON`, `MATERIAL LIGHTS: ALL`, `SHADOW EDGE: HARD`, and `HUD: FULL`.
3. Move to the **normal-mapped panel**. Verify the `L18 MATERIAL` HUD line shows `MAX/SURFACE` of **at least 5** while it is visible. The panel should show more than two light contributions; its normal shading should vary as the moving source changes direction.
4. Toggle `MATERIAL LIGHTS: COMPARE 2`. `MAX/SURFACE` should become at most **2**, and `DROPPED` should increase when the panel is rendered. The panel should lose some secondary colored lighting while the rest of the scene remains lit normally. Toggle back to ALL: the extra color should return without a scene reload.
5. Move toward the **hybrid pillar**. With ALL mode and High quality, the `MAX/SURFACE` value for its visible material must reach **at least 4**. Observe that rear lights change the silhouette rim but not front-face diffuse illumination, and that the blue front light can reveal the face. Move other geometry between a source and the pillar to check rim/shadow occlusion.
6. With `MOVING WALLS: PLAY`, verify translated and rotating shadows and their soft masks track the current blocker artwork. Pause, resume, and reload; no ghost masks or accumulating lighting should persist.
7. Turn `SHADOW FILTER` between Uniform/Contact, compare Hard / Soft 4 / Soft 8, and verify that each additional material light still respects its own shadow visibility and rear-rim rules.
8. Reopen `LAB / LIGHTING BASICS` and `LIGHTING_FLAT_TEST` to confirm the lab's two-light implementation and older fixture still behave as before.

### B. Mode and quality expectations

| Mode / quality | Expected maximum per visible material | Extra work |
|---|---:|---|
| Compare 2 + High | 2 | 1 ambient + at most 1 light pass |
| ALL + Low | 4 | Hard/no projected shadows under the existing Low budget |
| ALL + Balanced | 8 | Up to 4 pairs, 3 shadowed lights globally |
| ALL + High | 12 | Up to 6 pairs, 5 shadowed lights globally |

The exact light count on a given surface varies with camera position, ambient level, occlusion and light movement. `MAX/SURFACE` measures evaluated selected lights; `MAT-VIS` counts generated masks, not lights.

### C. Performance and stability acceptance

Record `L15C CPU-MS` P95 and `L18 MATERIAL LIGHT-PASSES`, `MAT-VIS`, `L17D FILTER PASSES`, `RT-MB`, and `RT-RESIZE` at steady camera position for both Compare 2 and ALL, with stress **off**, then repeat with stress **on** and High quality. CPU timings are CPU-side submission measurements; do not report them as GPU milliseconds or FPS. Extra light batches can legitimately cost more. Watch for runtime GPU device errors, stale mask texture artifacts, unexplained texture reallocation while window size is fixed, drift on dynamic blockers, and memory growth across reloads. Record screenshots of the two comparison modes at the **same** camera location and lighting time (pause moving walls if needed).

## What is deliberately deferred

- No automatic reduction in light/shadow masks for surfaces whose center is outside a light's radius but whose large artwork still intersects its circle (surface-extent selection can be improved separately).
- No HDR/linear-light target, bloom, tone mapping, physically based area lights, GPU timing query infrastructure or removal of the global 12-light High budget. These belong to later milestones.
- Dynamic normal-map tangent-space rotation still requires a separate production feature; Stage 16D restricts those assets accordingly.

## Verification status

Headless tests verify batching, stable selection, fixture reach, depth routing and self-caster filtering. Source and LDtk consistency can be checked without GPU access. The shader must still be compiled by MonoGame's content builder and the additive target blending verified in DesktopGL on the developer workstation. **Do not mark Phase 18 GPU-validated without these manual checks.**
