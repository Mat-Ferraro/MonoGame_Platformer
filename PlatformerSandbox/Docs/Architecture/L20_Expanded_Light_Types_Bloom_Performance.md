# Phase 20: Expanded Lighting, Three-Scale Bloom & Validation

## Architecture

- `RadialLight` retains its name for backward compatibility, but carries a
  `LightShape` with bounded direction, cone and tube parameters.
- `RadialLighting.Attenuation` is the headless sample policy used by hero,
  legacy scenery and material eligibility. Point remains the default.
- `LdtkLightingImporter` reads optional shape properties; existing projects
  with no new fields behave identically.
- Production GPU `L_BASIC_SurfaceLighting.fx` evaluates the same shapes
  per pixel. Fullscreen legacy scene-light fields use `L20LightField`.
- Directional shadows use extruded world-plane hull quads; spotlight/tube
  shadows reuse existing bounded point mask visibility as approximations.
- Resource ownership: all new bloom targets are owned and disposed with
  the existing renderer; resized/HDR-toggle recreates the entire pyramid.
- UI/presets belong to the presentation host only. Authoritative WorldSession
  never depends on lighting shape, postprocess state or driver support.

## Limits & quality

- Quality uses existing bounded `LightingFrameBudget` (Low 4, Balanced 8,
  High 12 visible). New Light Types Lab has five authored lights.
- Directional blockers can affect the entire visible frame; do not radial
  prune them. Their current geometry cost is O(casters × polygon vertices).
- For tube shadows, midpoint sampling is a known approximation; do not claim
  true area-light penumbrae or RGB stained-glass transmission.
- Bloom: 10 passes total when active, vs old 4. Three pairs of HDR/SDR RTs
  at half, quarter, eighth scale; memory estimate and CPU submit timing remain
  tracked separately from actual GPU timings.
- Show HUD/DEV TOOLS separately from scene tone mapping and bloom to preserve
  text legibility. `BLOOM ONLY` is a view mode, not a second light source.

## Regression protocol

1. Verify old `MATERIAL_SHOWCASE_TEST` and `LIGHTING_INTEGRATION_TEST` still load.
2. In new lab isolate each shape and move the player through visible pools.
3. For sun/moon cast shadows behind solids and check screen edges on camera pan.
4. Confirm spot cone falls to zero outside angle and tube illuminates along
   its length; compare CPU hero tint and GPU wall response.
5. Check 3-scale glow at scatter .15 vs 1.00 with BLOOM ONLY enabled.
6. Run the headless suite, then manually inspect target memory/CPU stats in
   full HUD at Low/Balanced/High quality and across window resizes.
7. Device-loss/recreation and SDR fallback should remain graceful.

## Known follow-on work

- Physically inspired light units and exposure adaptation.
- True multi-sample area penumbrae and colored transmission.
- Dynamic light-type animation and live per-shape editing gizmos.
- GPU timing queries and mobile-class quality scaling.
