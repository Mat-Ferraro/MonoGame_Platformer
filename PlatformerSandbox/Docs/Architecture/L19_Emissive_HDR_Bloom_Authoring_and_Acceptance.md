# Phase 19 — Production Emission, HDR Accumulation & Bloom

## Scope and limits

**Implemented:** optional true RGBA16F accumulation for four scene targets (illumination, individual-light, lit layer, final layered canvas), a guarded SDR fallback, exponential tone mapping, independently authored emissive GPU materials with optional texture masks, half-resolution soft-knee bright pass + horizontal/vertical Gaussian bloom, and mouse-operated developer controls. Stage 18's per-light shadow masks, dynamic blockers, self-shadow exclusions, depth-aware back-rims, 12-light material budget, and two-light comparison remain in place.

**Not implemented in Phase 19:** physically based exposure calibration, fully linearized input assets/sRGB conversion, emissive objects creating physical point lights, a full-frame post pass that includes characters/UI, per-light 2D diffraction or lens effects, and GPU-query timings. Those belong to later full-frame/presentation refinement. Only the **lighting canvas** is tone-mapped/bloomed; character/UI presentation drawn after the layered frame is intentionally not blurred.

## Render sequence

1. LDtk world lighting draws using the existing Phase 18 layered/shadow/material renderer.
2. If HDR is requested and the driver supports `SurfaceFormat.HalfVector4`, illumination/individual/lit-layer/canvas targets accumulate half-precision **values above 1.0**. The other targets (albedo, normals, edge, occlusion, etc.) remain RGBA8. Device allocation failures fall back to SDR without silently reporting HDR success.
3. A production GPU material with `EmissionIntensity > 0` adds `EmissionColor * intensity * EmissionMap.r * artworkAlpha` **once** in the ambient/alpha pass, not in each two-light additive pass. It remains visible in `MATERIAL LIGHTS: EMISSION ONLY` mode even when zero local material light contributions are selected.
4. Bloom, if on, extracts pixels over a soft luminance threshold into an approximately half-resolution target; horizontally and vertically blurs; then composites into the HDR/SDR scene. No blur is applied to the HUD.
5. HDR scenes pass through a bounded exponential tone map into the backbuffer, preserving energy above 1 until that point. With HDR off, the original SDR scene is preserved unless Bloom is enabled.
6. GPU material and shadow geometry remain owned by the same existing production pipeline; no new physics/simulation state is created.

## LDtk TerrainArtwork fields

| Field | Type | Valid | Meaning |
|---|---|---|---|
| `EmissionColor` | Color | `#RRGGBB` | Emissive tint; white default |
| `EmissionIntensity` | Float | 0..8 | Linear-ish radiance contribution; **0 is off** |
| `EmissionMap` | FilePath | optional PNG | Bright pixels in the map's red channel emit; black pixels do not |

Emission requires `MaterialShading` to be `Flat`, `Normal`, `Rim`, or `Hybrid`. It does not change light depth, normal response, cast/receive settings, shadow hulls, or collision. An unsafe or nonexistent map path fails before rendering; authored emission on Legacy shading is rejected instead of being silently ignored. All old levels remain non-emissive by default.

## Integration test environment

Load **LIGHTING_INTEGRATION_TEST** from Select Level. The Phase 18 scene has **11 authored lights**, **three dynamic shadow-casting blockers**, and **two opted-in GPU material examples**. Phase 19 adds only authored emission fields and two mask PNGs:

- **Normal-mapped panel** around world X 1500–1724: cyan glowing border and cross; five nearby light candidates.
- **Hybrid pillar** around X 2350–2462: magenta vertical luminous strips; four candidate lights (three behind/one in front). Back-rim must still respect occlusion.
- Both samples have clearly patterned emission masks, so every visible pixel should **not** glow uniformly. The rest of the level, including the unlit reference, remains a non-emissive control.

The panel's **MATERIAL LIGHTS** cycles `ALL` → `COMPARE 2` → `EMISSION ONLY` → `ALL`. `EMISSION ONLY` suppresses local GPU-material light batches but *does not* disable the entire scene, letting testers separate emissive radiation from nearby colored point lights. It also retains the old two-light comparison.

## Mouse-operated acceptance matrix

Open **DEV TOOLS → LIGHTING / POST FX**. Click first `PAGE` row to switch to `SYSTEM / DIAGNOSTICS` (stress, moving walls, full HUD, export report); click it again to return. No hidden key chords.

1. **Baseline:** HDR OFF, EMISSION OFF, BLOOM OFF, Material Lights ALL, Stress OFF. Shadows, colored lights and textures should match the Phase 18 scene; all old levels behave unchanged.
2. **Emission alone:** Material Lights EMISSION ONLY, EMISSION ON, HDR OFF, BLOOM OFF. The cyan/magenta *patterned* surface regions remain visible even without direct material lights. Toggle EMISSION OFF: patterns disappear. Ordinary scenery lights continue to work in the rest of the scene.
3. **HDR accumulation:** Material Lights ALL, HDR ON, BLOOM OFF, EMISSION ON. Full HUD should display `L19 HDR RGBA16F`; tone-mapping should roll off intense areas rather than hard-clipping all three channels to white. If it says `UNSUPPORTED`, test SDR fallback but do not count HDR acceptance as passed.
4. **Bloom:** HDR ON and BLOOM ON. Change BLOOM THRESHOLD and BLOOM GAIN. Glow should extend a little beyond the masked emissive details and bright light pools with *smooth* falloff, not concentric hard bands. Disable BLOOM: the halo disappears, but emissive details remain.
5. **Interactions:** Compare ALL vs COMPARE 2; vary Quality, Contact vs Uniform shadow filter, moving walls PLAY/PAUSE. Moving shadows and back-rims must not detach, and the bloom must not brighten otherwise dark shadowed pixels unless a **visible emissive source** is present.
6. **No light contributions:** EMISSION ONLY + EMISSION OFF must leave the surfaces with ambient only (not a black or transparent full-frame failure).
7. **Performance:** With Full HUD and unchanged camera location, record CPU P95, `L19 POST CPU`, render-target MB, `BLOOM PASSES`, and `BLOOM PIXELS` across 30 seconds for HDR/Bloom OFF vs ON; repeat at 1280×720 and 1920×1080 if possible. CPU timings are *not GPU frame times*; use GPU profiling if a strict performance budget matters. Expected `BLOOM PASSES` = 0 with Bloom OFF, 4 with Bloom ON and successful target allocation. RT resizes should stop once the viewport is stable.
8. **Stability:** Resize the window repeatedly; toggle HDR/Bloom repeatedly; pause/resume animated walls; reload LDtk scene; visit original LIGHTING BASICS lab. Confirm no shader sampler/RT aliasing errors and no leaked render-target count.

## Build/test commands (project root)

```powershell
py tools\l18_fixture_preflight.py
py tools\l19_fixture_preflight.py
dotnet build .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj
```

**Acceptance gates:** preflight pass; .NET + MonoGame effect compile pass; all unit tests pass; HDR hardware path confirmed; clear patterned emission and smooth bloom visible; no shadow/rim regression; no repeated render-target allocations at fixed resolution; stable frame times at target resolution. A source-level check **cannot** certify the GPU result or performance.

## Future improvement boundary

Phase 20 can introduce explicit linear-to-sRGB conversion, multi-resolution bloom, an optional shader-graph/material authoring layer, color grading, and a completely unified final presentation pass once the artistic target is chosen. Do not turn this into an engine-global renderer or alter gameplay determinism merely to implement post processing.
