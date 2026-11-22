# Lighting Basics — Stage 5: GPU normal maps and directional rim response

## Scope

- Extend Stage 4's **shared `GameplayLightingRenderer` material path** rather than making another standalone renderer.
- Keep existing Stage-4 defaults, receiver channels, opacity, unlit behavior, debug views, main-menu route and command-line route.
- One GPU shader evaluates the two shared radial lights on each lit material, including flat, normal-mapped, rim, and hybrid material responses.
- The lab generates a 64×64 albedo, normal map and authored edge-direction map in memory. No new source artwork or NuGet packages required.
- Shadows, per-pixel specular, HDR, bloom, post-processing and production gameplay integration are still **not** part of the lab.

## Build and launch

1. Install Stage 1/2, Stage 3, Stage 4 and the earlier L03 shader fix, then extract this update into the same project root.
2. Close the game and run `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj`.
3. Run `dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj`.
4. Go to **Select Level → LAB / LIGHTING BASICS**, or pass `--dev-lighting-basics` from the command line.

The updated `Content/Builder/Builder.cs` compiles the new `Effects/L_BASIC_SurfaceLighting.fx` alongside the existing L03 material shader. Shader input uses COLOR0/TEXCOORD0 to avoid the `ps_3_0 POSITION` compile error previously encountered.

## Inspector

The existing collapsible mouse inspector has the same **LIGHTS** tab. On **SURFACES**:

- `SELECT SAMPLE`: A/B/C/D.
- `LIGHTING MODE`: LIT or UNLIT; separate from the material shading algorithm.
- `RECEIVES LIGHT`: BOTH, L1, L2, ambient only.
- `SURFACE SHADING`: FLAT, NORMAL, RIM, HYBRID.
- `OPACITY`: composited once, after lighting.
- `NORMAL`: strength of the per-pixel normal-map response (0.00–1.00).
- `RIM`: authored contour response strength (0.00–2.00).
- `RESET SELECTED SAMPLE`: restore that sample's Stage-4 material.
- `LOAD SHADING COMPARISON`: A flat, B normal, C unlit, D rim; enables second blue light and colors the first amber.

The normal response uses one explicitly authored XY tangent-space normal per texture pixel, combined with an approximate virtual light height (25% of the light radius). Rim is an **edge-facing directional highlight** from the authored RG outward-direction map and A edge-eligibility map; it is not a physically correct 3D silhouette or Fresnel term.

## Visual acceptance

1. **Baseline:** without the comparison preset, all Stage-4 samples should look as before. Ambient = 0 and both lights disabled makes lit objects black, while the unlit reference stays colored.
2. **Normal:** choose sample B, `SURFACE SHADING = NORMAL`, normal strength 1. Drag Light 1 from left to right and around its top/bottom. Pixel shading should change on the same unmoving albedo texture. At normal strength 0, its response should look flat.
3. **Rim:** choose sample D, `SURFACE SHADING = RIM`, rim strength 1 or 2. Enable Light 2 and move it around the sample. Only authored contours and cut-out edges should catch directional light. At rim strength 0 the extra edge contribution should disappear.
4. **Hybrid:** choose sample A, `HYBRID`. Verify diffuse shading and directional edge highlights cooperate. Set normal and rim strengths to zero to recover the corresponding flat response.
5. **Receiver routing:** select B light-1-only, D light-2-only; disable each light independently. Material shading must not allow light from a disallowed source to leak in.
6. **Alpha:** sample D at 55% opacity must preserve the already-lit background without lighting it twice. Transparent holes remain transparent.
7. **Debug:** the albedo view remains unaffected by material shading; the raw illumination buffer and per-light views continue to show the undirected light energy.
8. **Menu:** exit to title and reopen the lab. The existing pause/return-to-title flow should still work.

## Correctness notes and deferrals

The Stage-5 shader uses the same squared radial falloff as the Stage-4 procedural light texture. This lab is intentionally SDR and may clamp bright overlap after shading; that is visible and expected until an HDR evaluation stage. Production lighting remains on its previous path; there is no automatic conversion of LDtk objects or existing gameplay materials to the new shader.

The per-surface full-screen passes prioritize debuggability over performance; production integration requires batching and profiling. A single global screen position should not be used to infer actual virtual-depth occlusion; shadows still come later. Post-processing of the entire rendered image remains a deferred TODO.

**Local validation limitation:** package structure and C# API/shader-parameter wiring were inspected, but the .NET SDK and MonoGame shader compiler/GPU runtime were unavailable in the packaging environment. Run the actual game and GPU visual acceptance on the Windows machine before marking Stage 5 complete.
