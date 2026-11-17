# L01 — Lighting, Materials, and Shadows: Phases 1–3

Status: **implementation candidate — developer build and visual validation required**. This is a lab-only prototype, not production LDtk/particle integration.

This package follows the approved `MonoGame_Platformer_2D_Lighting_and_Rendering_Architecture` implementation plan:

- **Phase 1 — Foundation and light playground:** authored ambient and radial lights; priority-based visible-light culling; scene ownership and cleanup; scene-color and illumination render targets; mixed-light accumulation and diagnostics.
- **Phase 2 — Materials and normals:** authorable lit/unlit material modes, influence channels, optional normal maps (neutral fallback), emission maps (not the same as actual light sources), influence masks, horizontal mirroring and rotation. The lab uses an intentionally modest **CPU reference material evaluator**, then uploads its result to a GPU texture. It is *not* a replacement for the eventual normal-map GPU shader.
- **Phase 3 — Shadows and occluders:** independently authored polygon shadow hulls; validated concave polygons, GPU-drawn extruded hard-shadow quads in a **per-light intermediate** so one light's shadow cannot erase another light; light/occluder filtering; opt-in shadows and separate contact/blob shadows. Stencil approaches remain an alternative to compare after profiling; this pass chooses the working intermediate-mask path.

Approved architecture references: LT-01–LT-11, LT-12–LT-20, LT-21–LT-29, LT-30–LT-43 where applicable. Lighting is **presentation-only**; the authoritative motor, collisions, level physics, audio, diagnostics, and particle system are untouched.

## Install

Copy the ZIP's directories over the project root. The only existing files changed are:

- `Source/Game1.cs` (optional `--dev-lights` host path and disposal)
- `Platforms/DesktopGL/PlatformerSandbox.DesktopGL.csproj` (deploy authored JSON and PNG maps)

Everything else is new. The package is incremental on top of the validated **P05/P06** project and all prior audio/camera/LDtk changes.

## Build

```powershell
dotnet build --configuration Debug -m:1
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~L123LightingFoundationMaterialsShadowTests
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj
```

Run the isolated lighting lab:

```powershell
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-lights
```

The normal game, `--dev-particles` lab, and `--no-particles` comparison are unchanged.

## Lighting lab controls

| Input | Result |
|---|---|
| F1 / F2 | Cavern (three lights) / Atrium (two lights, concave blocker) |
| Mouse left-drag | Move the selected light to the cursor |
| Tab | Select the next light |
| WASD | Pan the camera |
| Q / E | Zoom out / in |
| 1 | Enable/disable the illumination pass |
| 2 | Enable/disable authored normal mapping (neutral fallback) |
| 3 | Enable/disable hard projected shadows |
| 4 | Cycle combined / raw scene / raw illumination buffer |
| R | Reset the scene and its lights |
| H | Show/hide diagnostics |
| Esc | Exit the lab |

## Visual acceptance matrix

1. **Colored lights:** In Cavern, the amber, blue and violet lights overlap. A nearby region becomes brighter and tinted; areas far from all lights retain ambient illumination.
2. **Scene isolation:** F1 → F2 → F1 swaps authored lights and occluders without retaining the previous scene's lights. R restores initial light positions.
3. **Camera:** WASD and Q/E change composition without world-light drift. Compare zoom levels around 30 and 90 pixels per world unit.
4. **Normal maps:** The second material tile has changing orientation-dependent highlights; the first uses the neutral/no-normal fallback. Toggle 2 and compare the tile previews.
5. **Rotation/mirroring:** The third material tile uses a rotated, mirrored normal map. Lighting changes consistently as lights move. Texture UVs remain bounded to the authored 64×64 test maps.
6. **Emission and light mask:** Fourth tile has a visible warm glowing marking even with no nearby light. The mask gates dynamic light response independently of emission. Glowing appearance does not itself create another scene light.
7. **Hard shadows:** Drag the amber light across the rectangular column or teal/gold light around the concave Atrium hull; shadow direction changes. Toggle 3 to compare. Another light can fill the shadow because shadow removal is per-light.
8. **Decorative shadows:** Soft oval contact shadows under preview tiles remain present with geometric shadows turned off.
9. **Buffer views:** Toggle 4 to inspect raw scene color and accumulated illumination. HUD stays legible with a dark scene because it is drawn after lighting.
10. **Stability:** Repeated F1/F2 and window resize for several minutes. No graphics exceptions, growing old-scene lights, or prolonged performance stalls. Compare with normal game and `--dev-particles`.

Record observed `FRAME MS`, `MATERIAL MS`, visible light count, and projected quad count in the HUD at native resolution and at two zoom levels. `FRAME MS` measures host CPU submission/update duration **not GPU wall time**; actual GPU profiling remains a later milestone.

## Asset authoring

`Content/Lighting/L123_LightingLab.json` holds two independent environments with ambient RGB, radial light arrays, occluder polygons and material placements. Particle effects are not yet lighting authoring sources.

`Content/Lighting/Textures/*.png` contains original 64×64 reference albedo, normal, emission and mask textures.

Each material may omit `normal`, `emission`, or `mask`. Missing normal uses `(0.5, 0.5, 1)`; missing emission is black; missing mask is 1. Present maps must have matching dimensions. Asset paths must be simple local `.png` filenames (no parent traversal).

World coordinates are Y-up. Each occluder polygon must have nonzero area, no crossing edges, and three to 64 vertices; both winding orders are permitted. Shadow projection safely skips light-inside-hull cases rather than producing invalid geometry. Occluder outlines and shadow hulls are presentation data, not collision sources.

## Important limitations before Phase 4

- **No live gameplay integration yet.** The lab does not darken real LDtk cavern layers, read authored LDtk light entities, or light live sprites and particles. This belongs in **Phase 4 — Engine integration** and preserves the validated game.
- **No particle light emission yet.** Planned Phase 4: one effect-level or emitter-level light per VFX by default; individually moving particle lights strictly budgeted, owned by effect handles, and never requested from the deterministic simulation. Visual emission remains distinct from scene illumination.
- **Material preview uses CPU reference shading, not a production GPU normal shader.** This verifies authored maps/normal orientation/cross-channel semantics before introducing MonoGame `Effect` compilation and platform-specific shader build integration.
- **No production-quality shadow guarantee.** GPU-projected hard silhouettes are a prototype, especially for concave overlap, light-inside-blocker and extremely large occluders; geometry and stencil/mask comparisons still require visual reference images and profiling before shipping.
- **One playground world illumination channel for backdrop.** Material preview channel filtering works headlessly; real LDtk parallax-group light channels, transparent forward particles and gameplay HUD boundaries will be integrated in Phase 4.
- **No post-processing, bloom, penumbra, height-map ray marching, physically-based light transport or automatic per-particle dynamic lights.** These are later optional features by architecture decision.

## Automated coverage

`L123LightingFoundationMaterialsShadowTests` tests camera projection and edge culling; light priority/capacity/ownership; radial attenuation; strict invalid settings; neutral versus normal-mapped shading; rotated/mirrored tangent normals; unlit materials; emission; dynamic mask and channels; polygon validation/concavity; polygon ray occlusion; actual authored JSON and PNG references; and long-running bounded headless updates.

Automated tests cannot validate GPU blend states or shadow appearance. Those require the in-game lab visual checklist above. Report any errors with the full compile output or a screenshot of the raw illumination buffer (key 4).
