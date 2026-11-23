# L17C–D — Contact-hardening shadows and production validation

Status: **implementation candidate / workstation verification required**.
Depends on 17B Gaussian shadow fix and Phase 16D/E production dynamic blocker integration.

## Rendering model

- **Hard** (manual Shadow Edge = HARD or quality LOW): original Phase 16 visibility mask, no blur render-target work, no profile upload. All self-shadow exclusions, receiver routing, layer depth, and shadow budgets are unchanged.
- **Uniform**: unchanged Stage 17B separable Gaussian filtering. Select Soft 2/4/8 px in DEV TOOLS. No contact profile is generated.
- **Contact**: each actual shadow mask's original `ShadowVisibility.BuildPolygon` fan is used to derive a **1024-sample polar first-hit distance profile**. No duplicate scene raycaster is introduced. The GPU decodes this profile and evaluates a clamped penumbra radius per pixel:

  `width = min(edgeWidth, max(0.75px, sourceRadiusPx * max(0, receiverDistancePx - hitDistancePx) / max(2px, hitDistancePx)))`

  This is **approximate screen-space contact hardening**, not physically accurate area-light sampling. The width grows as the receiver gets farther behind the first blocker. Pixel-space source radii are intentionally bounded. A tiny baseline antialiasing radius avoids a hard silhouette seam.
- **Shadow Quality = MANUAL**: uses the explicit `Shadow Edge` preset. Manual `HARD` is also the fallback/reference mode.
- **Shadow Quality = AUTO**: derives edge width from the existing lighting quality: Low=Hard *filter* (and the existing Low budget disables projected shadows), Balanced=Soft 4 px, High=Soft 8 px. Filter selection (Uniform / Contact) stays independent of quality.
- **Edit Light / Light Size**: cycles authored lights and sets the selected light's approximate emitter radius to 2 / 4 / 8 / 12 screen pixels. The baseline for lights without an override is 8 px. Values are keyed by authored light ID. This changes only contact-hardening blur, not light color, intensity, radius, position, or light selection priority.

## Integration and ownership

`ShadowContactHardening` is a pure Foundation geometry/reference utility. `GameplayLightingRenderer` owns the reusable profile buffer, the lazy 1024×1 RGBA profile texture (4 KiB), the GPU effect parameters, and per-frame counters. `Game1` owns nonpersistent developer controls; game simulation and LDtk authoring content are unchanged.

The profile comes **from the current mask's fan**, after production layer routing, radius pruning, and (for material surfaces) the *self wall ID exclusion*. Therefore nearby occluders, animated/rotating hulls, back-rim visibility, and normal diffuse shadows share the same obstruction model. The shader simply filters the resulting mask at a locally varying radius.

The phase intentionally does not increase GPU material light budgets, alter the Stage 14 lab's separate shader, add physics to animated walls, or introduce HDR/bloom. The first-hit profile is angularly quantized and can show fine angular artifacts at extreme geometry/light scales: examine thin/slanted edges under High quality and compare against Hard.

## CPU / GPU cost and diagnostics

Phase 17D adds the following **CPU-side / workload** metrics to the Full HUD:

- `CONTACT PROFILES`: number of contact profiles generated this frame, zero for Hard and Uniform;
- `PROFILE CPU`: cumulative milliseconds generating, encoding and uploading polar profiles, **not GPU execution time**;
- `UPLOAD BYTES`: profile texture upload volume, 4096 bytes per generated profile;
- `FILTER PASSES`: two full-viewport Gaussian passes for every softened visibility mask;
- `FILTER PX`: sum of pass destination pixel areas, an estimated shader work proxy, **not measured GPU fragments**.

Stage 15C's rolling CPU time/P95, shadow mask counts, material-mask geometry CPU cost, Stage 16's pruned/skipped masks, and render-target resize counters remain available.

## Acceptance procedure (Windows DesktopGL)

1. Extract the ZIP into the **working Stage 17B project** and build/test:

   `dotnet build .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj`

   `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj`

2. Open `LIGHTING_INTEGRATION_TEST` at the same window size for all comparisons. In **DEV TOOLS**, disable Stress Lights, enable Moving Walls, enable Full HUD, and use Lighting Quality HIGH. Freeze Moving Walls to compare exactly the same geometry across filter modes, then re-enable motion to check temporal stability.
3. With Shadow Quality MANUAL, select **HARD**, then **UNIFORM + SOFT 8PX**, then **CONTACT + SOFT 8PX**. Move to the blue-light crate: its shadow should have a narrow edge near the crate and broader edges farther away. The old parallel-bright-band artifact must not return.
4. Select **EDIT LIGHT** to switch between RED/BLUE/PINK/GREEN, and cycle **LIGHT SIZE**. Only the selected light's contact-penumbra width should change. Light location, falloff, depth, normal-map shading, hard visibility, and shadow budget must remain stable.
5. Re-enable moving/rotating walls. Look for detached shadows, popping rims, seams near angular 0°, leaked visibility through other blockers, ghost shadows when a caster moves off screen, and correct self-shadow exclusion for the backlit pillar.
6. Enable Shadow Quality AUTO and cycle Lighting Quality LOW/BALANCED/HIGH. Verify that Low performs no projected-shadow work per its existing budget, Balanced caps softness at 4 px, and High caps it at 8 px. Returning to Manual should restore the explicitly selected shadow edge.
7. With the same camera/geometry/resolution and Stress OFF, record 120+ samples for each variant. Then repeat with Stress ON. Report `CPU P95`, `PROFILE CPU`, `CONTACT PROFILES`, `FILTER PASSES/PX`, `MAT/LEG masks`, `RT-MB` and `RT-RESIZE`. GPU timing/FPS requires an actual GPU profiler or external frame-time tool; these HUD readings **do not establish GPU speed**.
8. Regression: verify `LIGHTING_FLAT_TEST`, `LAB / LIGHTING BASICS`, `LIGHTING_INTEGRATION_TEST`, scene reload, pause/resume, window resize, color/brightness, and hidden regression fixture launch. No new keyboard shortcuts have been added.

### Practical pass/fail criteria

- Zero compiler/test failures; no shader compile errors.
- Hard mode draws identically to Stage 16/17B Hard, with zero contact profile uploads and no added blur passes.
- No rim visible through occluders; a behind-wall light does not light its front face; correct shadowing as dynamic polygons rotate/translate.
- In Contact mode at a fixed edge-width cap and emitter size, the penumbra near the blocker is visibly narrower than farther behind it; changing one light source size does not alter other sources.
- `RT-RESIZE` stabilizes after initial allocation/resizing; no accumulating render-target growth. GPU time is measured externally before claiming performance acceptance.

This is an implementation closeout, **not a claim of GPU-validated Phase 17 completion**. User workstation acceptance closes the phase.
