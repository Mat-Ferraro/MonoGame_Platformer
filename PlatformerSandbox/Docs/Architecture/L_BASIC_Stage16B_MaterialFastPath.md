# Stage 16B — Material Fallback Texture Fast Path

## Context
Stage 16A's range pruning is the prerequisite. Stage 16B changes **only** the production per-artwork material fast path. The lab shader paths and gameplay lighting equations are unchanged.

## Previous work per production material surface
- One full-screen albedo RT render, used for shading.
- Two full-screen normal/edge render target clears, even when the map was absent (neutral RGB/A values).
- Up to two white clears of full-screen production visibility masks when the light was absent, shadows disabled, out of budget, or there was no relevant caster.
- Real visibility fans still generated for each light–surface pair with occluders, with exact own-caster exclusion.
- One full-screen shader pass and one full-screen layer composite.

## Optimized behavior
- Reuse three immutable 1×1 point-sampled textures: flat normal `(128,128,255,255)`, absent edge `(128,128,128,0)`, full visibility `White`.
- Skip neutral normal/edge render target capture when callbacks are absent.
- Skip SetRenderTarget+Clear for unused production visibility mask slots, bind 1×1 white instead.
- Always bind both maps and both visibility inputs for **every** production material draw. A preceding material's textures cannot leak into a subsequent material's draw.
- Never cache actual shadow masks across distinct surfaces or frames; per-surface self-shadow exclusion, moving light positions and dynamic blocker routing still recompute correct masks.
- Retain existing falloff, rendering, quality budgets, cast/receive, virtual depth and shadow-pruning logic.
- Only the production path binds the new fallback textures; the Stage 14 lighting lab continues its own maps and shader bindings.

## Diagnostics
In the full HUD, `L16B MATERIAL FALLBACK: NEUTRAL MAPS N WHITE MASKS M` reports the count of otherwise-full-screen render target clears avoided **in the current frame**. These are avoided CPU/GPU command submissions, not measured GPU times or guaranteed FPS savings. The existing rolling CPU P95 offers an approximate comparison but fluctuates with machine load.

## Validation
1. Build, run the headless tests, and visit `LIGHTING_INTEGRATION_TEST`.
2. Show Full HUD via visible `DEV TOOLS`, verify `MAT=2`, and inspect `L16B` counters. Map fallback counts depend on which test materials have maps. White-mask reuse depends on selection, budget, visibility and lighting locations.
3. Move past the normal-only panel and the hybrid pillar. Confirm the pillar's green back-rim still respects occluders and its front face stays dark.
4. Use `DEV TOOLS` to compare low/balanced/high and lighting stress on/off, comparing CPU P95 **over multiple stable windows**; avoid inferring GPU timing from CPU measurements.
5. Visit `LAB / LIGHTING BASICS` and the flat lighting regression scene to confirm unchanged behavior.

## Remaining work
This stage does **not** eliminate the full-screen material shader/composite passes, shrink RT allocation, add GPU timing, or lift the two-light material budget. A future stage can add conservative per-surface scissor rectangles and investigate batched material rendering. Those require careful alpha/viewport and rotated-sprite regression tests first.
