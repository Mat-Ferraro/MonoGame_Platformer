# L21B2 — Lit wall receiver overlap correction

Baseline: Phase21A_B1_Spatial_Lighting plus Dedicated_Emissive_Lab (no previous rollback).

The existing ray fan stopped at the first boundary intersection even for an authored lit wall whose front face is intentionally shaded independently of its own hull. A player overlapping that visible face therefore lost direct light before reaching its far edge.

## Policy

- For each light separately, a lit artwork face targeted by that light at or in front of the artwork's authored virtual depth permits the first solid ray interval to receive light. The first downstream exit edge starts the receiver shadow.
- Back-depth lights, non-lit artwork, and geometry-only blockers keep strict entry-edge occlusion for external sources. Another blocker can still occlude the face; no white overlay is painted over other shadows.
- The minimum eligible intersection across all contours/blockers wins. The first exit, not the farthest exit, prevents leaks across concave gaps or disjoint shapes. Winding-independent edge normals work after rotation/mirroring.
- The existing light-origin-inside-solid behavior remains unchanged: rays may reach the first exit. This update targets receiver overlap with an externally illuminated wall; it is not a replacement for the separate embedded-source overlap policy.
- Lit-face metadata is assigned when importing lit shadow-casting artwork, using authored material depth and layer targeting. Bare LightBlocker entities remain solid occluders. Motion copies preserve metadata.
- Normal production receiver and shared scene-layer masks opt in. Back-rim masks retain strict occlusion. The legacy standalone geometry API defaults to strict behavior; CPU world sampling uses the face policy. Directional masks sweep exit edges only for lit artwork, with the existing finite reach; dark casters retain full sweeps.
- No physics, material opacity, shader, brightness, emitter position, or new level artwork changes. Tube shadows still use the existing midpoint approximation. This is a 2D artistic receiver rule, not a general volumetric depth solver; a shared layer mask does not distinguish receivers with different depths within that layer.

## Visual acceptance

Open EMISSIVE COLOR LAB. Mode 3 (default) selects world lights only with both emitters at wall depth. Move through the low wall in either direction. The cyan light should persist through the wall's visible footprint until the far edge (and conversely for magenta). Outside the wall the cast shadow remains. Use 5 for a comparison with both emitters placed behind the wall in virtual Z; the wall face is dark from direct lighting and shadows start at its near edge. Press 3 to restore. Keys 1, 2, 4 retain existing behavior. Ambient and other light contributions remain independent; a shadow does not force black output.

Repeat with a character partially crossing the boundary and verify the neutral receiving strip/ground as well. Recheck Light Types Lab, Lighting Integration Test, and Material Showcase (including back-rim leakage and moving geometry). The new mode only changes selected light depth in the dedicated lab.

## Verification

Existing Phase 21 emission and spatial Python fixture preflights passed on the overlay. Independent Python reference checked 12,000 randomized rays and the actual fixture's overlap and behind-wall points. New xUnit cases cover imported metadata, hard mask fan, strict fallback, other blockers, winding, concavity, reversed light direction, transformed wall, bounded directional projection and CPU sampling. .NET/MonoGame tools unavailable here: compilation, xUnit and GPU results require local verification. No shader source changes.
