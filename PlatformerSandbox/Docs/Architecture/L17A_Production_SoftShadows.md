# Stage 17A — Production soft shadow edges (screen-space penumbra)

## Scope and baseline

Build on the validated Phase 16D/E production LDtk renderer. **Hard remains the default**;
original radial hard-visibility polygons, ownership, depth routing, self-shadow exclusion,
shadow-caster pruning, visible light budgets, and animated wall transforms stay unchanged.

This is an intentionally bounded *art-directed screen-space mask filter*, not physical
area-light shadowing, PCSS, or distance-growing contact hardening. The penumbra is fixed
in screen pixels. Shadow interiors still have zero visibility beyond the filter radius.

## User controls

`DEV TOOLS` → `SHADOW EDGE: HARD / SOFT 2PX / SOFT 4PX / SOFT 8PX`.
A single mouse-operated button cycles four settings. It is independent of `QUALITY`, which
continues to set light/ray budgets. In Low quality shadow-light budget is zero and the
softness setting is inert. Hard uses the unchanged original mask multiplication path.

## Implementation

- Hard radial visibility polygon is computed once for every required shadow mask.
- Stage 17 does not add render targets or rerun the CPU ray trace.
- Ordinary LDtk layers: `SoftLayerVisibility` effect technique samples nine points of the
  finished visibility texture, using a symmetric 3×3 kernel (center 1/4, 4 cardinal
  neighbors 1/8 each, 4 diagonal neighbors 1/16 each). Only the multiply step changes.
- GPU material surfaces: apply the **same nine-point visibility filter** to each actual
  per-light production mask in `SpriteDrawing`. A neutral 1×1 white mask always stays
  white and uses a single hard sample. The softness is applied to each light's
  visibility *before* it reaches diffuse lighting and the directional back-rim, so
  diffuse and rims agree about occlusion. Ambient stays untouched.
- UVs are full viewport coordinates even for scissored Stage 16C material quads;
  rotated/translating Stage 16D artwork uses the current frame's occluder hulls.
- After a soft layer mask draw the effect's prior technique is restored to prevent
  the UI, other materials, or debug views from inheriting the mask shader.

## Validation on Windows

Run `dotnet build .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj`
then `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj`.

Open `LIGHTING_INTEGRATION_TEST`, disable stress lights, pause moving walls, then
compare hard/soft at the same camera and light positions. Turn moving walls on;
check straight and sloped shadow edges, rotated geometry, overlapping shadow casters,
backlit rim receiving shadow from another caster, and self-shadow exclusion.

In the full HUD inspect `L17 SHADOW EDGE ... LAYER SOFT N MAT SOFT M` as well as the
Stage 15C CPU-P95 and Stage 16 counters. Compare quality Balanced and High with the
same fixed soft setting. Use DEV TOOLS buttons only; no keyboard sequences added.

A green rim behind an unrelated blocker must not become fully illuminated; only a
narrow boundary of partially visible pixels may appear at the blocker shadow edge.
If the blurred mask looks like unwanted light bleeding at 8px, reduce to 2 or 4px.

## Limitations / next steps

1. The Lighting Basics *analytic three-polygon* reference lab remains hard-shadowed
   to preserve regressions. This stage covers the real LDtk production renderer.
2. Characters/forward-only sprites that use CPU sample visibility still have their
   previous crisp occlusion. Do not advertise these as soft until separately ported.
3. Softness is constant in screen pixels; it does not grow with distance from the
   caster or account for light-source physical extent.
4. This increases GPU texture sampling cost for actual masks; the CPU submission
   timer does not measure GPU duration. Profile before choosing 8px as default.
5. The per-material two-light limit remains; Phase 17 does not expand it.
6. The main material pixel shader now has two optional nine-tap branches; verify
   DesktopGL `ps_3_0` shader compilation on the target machine.
