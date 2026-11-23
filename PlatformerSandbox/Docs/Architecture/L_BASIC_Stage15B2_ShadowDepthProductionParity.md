# Stage 15B2 — production shadow-aware materials and directional back-rim

## Decisions and ownership

- `LdtkLightingImporter` creates authored `LightingOccluder` hulls from casting `TerrainArtwork`; the hull uses the artwork IID and retains its scene layer. The existing geometry importer is not replaced.
- `ProductionSurfaceSubmission` carries presentation-only material, caster IID, virtual depth, overlap policy and back-rim authoring. It does **not** participate in simulation/collision.
- `ProductionWallLightRouting` is a pure Foundation policy: exact-id self exclusion, per-light routed blocker selection, and behind-wall depth/overlap decisions.
- `GameplayLightingRenderer` reuses the already active layered canvas, `L_BASIC_SurfaceLighting.fx` material shading and the existing production `ShadowVisibility.BuildPolygon` ray-fan algorithm. It **does not** reduce gameplay to the lab's three blocker slots.
- `LightDepthZ` and `MaterialDepthZ` are scalar virtual mounting depth, distinct from `SceneLightingLayer.Order` (draw order) and `OccluderLayers` (light-specific shadow routing). Defaults of zero preserve same-depth/front behavior.

## Per-surface GPU execution

1. Select at most two effective lights with deterministic ID tie breaking (existing B1 rule).
2. Resolve the currently drawn surface's own hull by exact IID; require a single four-vertex hull if `CastsShadows=true`. Reject mismatches, don't silently fall back.
3. For each selected light, decide whether it is behind THIS wall: `light.VirtualZ < surface.MaterialDepthZ`, or equal Z with `BackRimOnOverlap` and origin inside its wall polygon. No wall means no implicit back-rim.
4. For shadows on a receiving front face or behind-rim, build the light's production radial visibility fan from OTHER occluders, including silhouettes overlapping this receiver but with a different ID. Respect `LightLayerRouting.Occludes`, `CastShadows`, and the frame's quality/shadow quota. Masks default to white where no blockers are eligible.
5. Draw the authored albedo/normal/edge maps to the reusable work surfaces. Bind the two full-viewport visibility textures in production shader mode; the lab still uses its own analytic bounded polygons.
6. If source is behind, suppress ONLY this source's front-facing diffuse and material edge rim. Evaluate its directional back-rim using the real wall polygon, shade only light-facing edges and multiply by that source's per-pixel visibility mask. Ambient and other eligible lights are unaffected.
7. Alpha-over the lit surface on the existing layered canvas, preserving previous draw order.

## Deliberate remaining limits

- The 2D radial visibility geometry does not yet use true 2.5D Z-intersections of **other** walls; Z currently controls the authored material receiver face/rim. Do not claim physically 3D interlayer occlusion.
- A production material currently selects at most two lights and only supports a four-vertex casting artwork hull for its own rim. Other occluders may be arbitrary validated 3–64-vertex shapes.
- MonoGame SurfaceMaterials shader still uses SDR clamp; it is not yet exposure/HDR parity with the legacy layered composer.
- Rotated/flipped authored tangent-space normal and edge maps remain blocked rather than silently misoriented. The lab's rotated polygon validations remain available separately.
- Runtime GPU screenshot goldens, render-target reuse profiling, viewport resize edge cases, and shadow/light stress tests on Windows are still necessary.

## Acceptance matrix

| Test | Expected |
|---|---|
| Flat baseline level | Legacy scene visually unchanged |
| Lighting Basics lab | Original Stage 14 hard shadows, depth overlap and motion still work |
| Normal-mapped panel in integration level | GPU surface retains normal response, now shadow-receiving |
| Casting hybrid pillar | Its own rim/front is not blocked by its own hull; nearby distinct objects still block it |
| Green light on lower Z | Does not illuminate pillar face, may highlight visible light-facing edge |
| Same-Z overlapping light with default mode | Normal front response; no forced back-rim |
| Same-Z overlapping light with BackRimOnOverlap | No front response, visible directional back-rim when unblocked |
| F11 HUD | MAT 2, MAT-LIGHT-LIMIT 0 expected in default setup; MAT-VIS and MAT-BACK reflect actual active source routing |
| Low quality | Shadow budget may disable masks; behavior matches the established quality policy |
| All .NET tests and DesktopGL shader build | Required local acceptance; not verified in the authoring environment |
