# L-BASIC — Minimal MonoGame Lighting Baseline (Stages 1–2)

## Scope and integration

This intentionally small developer laboratory (`--dev-lighting-basics`) uses **the existing** `GameplayLightingRenderer`: `BeginWorld()` captures neutral albedo, `ComposeBasic()` generates the GPU illumination buffer and multiplies it against the captured albedo. The demonstration and the regular gameplay/LDtk renderer share render targets, falloff texture creation, and the same blend-state conventions. It does **not** introduce a second lighting engine.

No new Content Builder assets, image files, effects, scene manifests, gameplay rules, physics, or dependencies are required. The environment is drawn procedurally from the game's existing one-pixel texture.

All existing gameplay levels and `--dev-lights` / `--dev-materials` / `--dev-particles` are unchanged. This is a separate optional developer boot mode. The production rendering path remains unchanged until visual acceptance.

## Run

From the original project root on Windows in PowerShell:

```powershell
dotnet build --configuration Debug -m:1
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~LBasicLightingPreviewTests
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj -- --dev-lighting-basics
```

If launching in Visual Studio, add `--dev-lighting-basics` to the DesktopGL debug command-line arguments.

## Controls

| Input | Behavior |
| --- | --- |
| Left mouse drag | Move the single white light to the mouse position |
| `L` | Enable/disable the local light; disabled is Stage 1 ambient-only |
| Up / Down | Adjust ambient brightness by 0.05 |
| `0` / `9` | Snap ambient to black / fully lit |
| `1` | Final lit composition |
| `2` | Original albedo capture |
| `3` | Raw GPU illumination buffer |
| `R` | Reset ambient, light, position and view |
| `H` | Hide/show unlit diagnostic labels and help overlay |
| `Esc` | Exit |

## Visual acceptance (must be verified on GPU)

1. Press `L` to turn off the white light. Set ambient to zero using `0`: both wall and the two lit sample blocks should be **black**. The independent UNLIT sample and HUD remain visible.
2. With the light still disabled, increase ambient with Up. The wall and both lit blocks brighten at the same rate. They never become tinted unexpectedly.
3. Set ambient to zero again and press `L` to enable the light. Drag the mouse: pixels at the light center reveal their authored color; pixels outside the light's radius remain black. Falloff should be smooth and circular.
4. Press `2`: the original wall and lit samples are completely visible without any lighting tint. Press `3`: the GPU light buffer is grayscale, showing the actual ambient and falloff. Press `1` to return to the composed result.
5. Check light movement at the screen edges and after resizing the window. No light should jump relative to the mouse, create trails, or disappear unexpectedly.
6. Disable the HUD with `H` when comparing the buffers without screen-space diagnostics; the unlit reference remains visible in final and albedo views.
7. Verify the old lighting lab, materials lab, a playable LDtk scene, and the test suite remain unaffected.

## Deliberately absent in this first pass

- Geometric shadows, contact shadows, occluder-depth logic, per-light layer routing
- Normal/rim/specular/emissive material shading or alpha-rich sprites
- Multilight accumulation, light colors other than white, dramatically boosted exposure
- Bloom, HDR buffers, color grading, and *full-screen post-processing* (recorded TODO)
- Automated GPU image comparisons or backend parity validation

These are not assumed correct merely because existing tests pass. Add them one by one after confirming the basic appearance.

## Implementation and future migration

1. `BasicLightingPreviewState` is an isolated headless representation of light-on/off, ambient, normalized mouse position and debug view. It creates an existing `RadialLight` with the same quadratic attenuation semantics used by the lighting library.
2. `BasicLightingLab` draws opaque albedo content and an unlit visual reference. It calls the existing renderer rather than duplicating the GPU calculations.
3. `GameplayLightingRenderer.ComposeBasic()` is the single-light/no-shadow acceptance path using the existing scene capture, light buffer, multiply and target presentation resources. It keeps the more complex production path intact for regression safety.
4. After Stage 1/2 visual acceptance, factor the validated common composition steps into a renderer helper used by **both** `ComposeBasic` and production `ComposeLayer`, then replace the remaining point-sampled independent-sprite shading in a later dedicated phase.
5. Keep **full-frame post-processing** as a separate final rendering stage TODO, with an explicit decision about whether UI should be affected. No post-processing feature is implemented by this change.

## Limitations

The code currently uses 8-bit SDR color and the existing quadratic radial texture. It does not claim linear-light color correctness, physically based lighting, or platform-parity validation. For this stage, white intensity is capped at 1 so the existing exposure compensation and repeated high-energy passes are unnecessary.

The UNLIT sample and HUD are intentionally drawn after the final render-target presentation; the GPU albedo and illumination targets themselves exclude these overlays. This is a visual demonstration, **not** an automated pixel-level proof.
