# L02.6 — Dramatic light energy and contrast correction

## Why the previous Dramatic preset was too dim

The local-light SpriteBatch pass clamped each per-channel light tint to at most 1 **before** drawing its radial falloff texture. A light with an authored intensity of 1.6 and a preset multiplier of 2.15 therefore looked nearly identical to a light of intensity 1 when shading the backdrop. Independently shaded props used the full gain, making them visually inconsistent with the floor and wall.

There was a second limitation: multiplying an 8-bit color buffer by an SDR light buffer cannot produce a lit pixel brighter than the source artwork. The test backdrop is intentionally muted, so even a saturated light channel looked muted.

## Changes

1. `LightingEnergyPasses.Split()` decomposes light gain into bounded [0,1] RGB passes. These add in the per-light render target **before** the visibility mask is applied. Shadows therefore still block that light's entire contribution; other lights are unaffected.
2. `LightingArtPreset.LightExposure` defaults to 1. Values above 1 let a completed lit layer be displayed brighter than its source texture, **only in pixels occupied by that layer**. Exposure is implemented with destination-alpha-masked additive composition, retaining the compositing order and transparency of the 11 scene layers. The separately shaded sprite sampler applies the same exposure multiplier.
3. The `FlatDramatic` preset retains its tight `.78` light radius and dark ambient layer ratios. It now uses 2.65 local-light intensity gain, 2.30 layer exposure, and ambient RGB `(0.09, 0.115, 0.16)`. The intent is bright *pools* and dark *unlit/blocked* regions.
4. `FlatReadable`, default cavern presets, authored light positions/targets, LDtk surfaces, shadow flags, physics, collision and player movement are unchanged.

This is still an SDR approximation, not HDR bloom or radiometrically accurate tone mapping. Exposed light surfaces may clip at white, and additional draw passes add graphics cost. Normal-map production rendering and advanced exposure/tone mapping remain separate future concerns.

## Installation

Extract this ZIP into the existing `PlatformerSandbox` project directory and overwrite files. No scripts or project-file modifications are required; .NET SDK project default includes discover new `.cs` files.

## Automated checks (run locally)

```powershell
dotnet build --configuration Debug -m:1
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~L026DramaticLightEnergyTests
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj
```

## In-game visual checks

```powershell
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=LIGHTING_FLAT_TEST
```

- Default Dramatic: wall and floor should have much brighter pools close to the lamps, while the spaces outside the pools remain dark.
- The pillar and casting crate should block **bright** local light: the dark shape behind each obstruction should now be clearly visible.
- The non-shadow-casting crate should not block the scene's light.
- The unlit reference sign must not change.
- `Ctrl+Alt+2` swaps to the unchanged Readable preset; `Ctrl+Alt+6` toggles the inspection light. Move the inspection light across the pillar and crate to inspect dynamic shadows.
- World layers should keep their draw order and opaque/transparent pixels. Foreground silhouettes should not brighten unrelated background pixels.
- Verify Caverns A/B/C and `--dev-lights` still work. Since global gain-clamping is corrected, **some** bright parts of older caverns may appear slightly brighter than before; their authored values and presets were not changed.

## Validation boundaries

Static asset/configuration and source checks were performed, but no SDK or GPU is available in the packaging environment. A local C# build, xUnit run and visual acceptance are required before treating this as validated.
