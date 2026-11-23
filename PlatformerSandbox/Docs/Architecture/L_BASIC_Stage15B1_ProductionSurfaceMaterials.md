# Stage 15B1 — Production GPU surface opt-in

## Status

This is the FIRST bounded production GPU material slice, not completion of
Stage 15B parity. `LightingIntegrationTest.ldtk` includes two demonstrators.
The legacy `LightingFlatTest.ldtk` is unchanged.

## Ownership and draw order

`LdtkLightingImporter` remains the authoring source for the surface policy.
`ProductionSurfaceSubmission` in Foundation carries one presentation-only draw
contract without MonoGame, simulation, or collision coupling.

`LdtkVisualSceneAssets` draws legacy tiles/scenery/art in their existing paths.
For opt-in unshadowed TerrainArtwork, it suspends its caller-owned SpriteBatch
(after ComposeLayer) and supplies screen-space albedo/normal/edge draws to
`GameplayLightingRenderer.DrawProductionMaterialSurface`. The renderer captures
only that surface's work buffers, binds `L_BASIC_SurfaceLighting.fx` with the
same `BindBasicSurfaceShader` method used by the lab, then alpha-composites
onto the existing `_layeredCanvas`. The scene compositor and pre-existing
normal rendering paths remain untouched.

Content loads before world commit, and authored map errors fail scene loading
without leaving partial GPU assets. Effect lifetime remains ContentManager-owned.

## Deliberate boundaries

- `Legacy` is the default, with no change to existing artwork, tiles or hero.
- Explicit opt-in is only allowed for lit, noncasting, non-shadow-receiving
  TerrainArtwork with no contact-shadow decoration. Incompatible settings are
  rejected by the importer instead of shading a wall without its shadows.
- `Normal`, `Rim`, `Hybrid` maps on rotated/mirrored artwork are rejected until
  normal-space transforms are implemented in the production GPU shader.
- The shared GPU shader supports 2 lights in a material pass. Active lights are
  selected per material at its authored center, ranked by actual attenuation,
  stable by light ID. More lights stay in the scene and legacy lighting passes;
  the GPU material drop count appears as `MAT-LIGHT-LIMIT` in the full HUD.
- Normal and edge PNG maps may use a different source resolution but must be
  authored in the same UV space. Missing maps get the lab's neutral fallback.
- Color is SDR and clamps at the surface shader, unlike the production layer's
  optional exposure copies. Brightness parity remains a next-stage item.
- No arbitrary occluders are fed into the lab shader: the shader's bounded
  three-shape test path is not equivalent to the production shadow polygon set.

## Stage 15B2 sequence

1. Build reusable per-light visibility textures for any LDtk occluder shape,
   with explicit per-object self-caster exclusion. Keep the existing layer
   shadow budget and routing.
2. Extend the production material shader to use these masks without reducing
   the number of production occluders. Perform per-light per-wall depth routing
   and directional *back-rim* in that visibility-aware pass.
3. Support transformed normal/edge maps, more lights, and material batching.
4. Add GPU screenshot baselines/metrics (lighting on/off, camera resize,
   occlusion, alpha, moving source, rotating wall), then migrate more surfaces.

## Acceptance

- Full .NET test suite and DesktopGL shader compile succeed.
- Existing Stage 14 Lighting Basics shadows/rims unchanged.
- `LIGHTING_INTEGRATION_TEST` playable; normal and hybrid samples update with
  moving blue light, do not erase backgrounds, do not alter physics.
- `LIGHTING_FLAT_TEST` remains legacy; opt-in casting/shadow-receiving surface
  authoring produces an explicit validation failure.
- MAT count reads 2; dropped per-surface light count is visible for profiling.
