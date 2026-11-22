# L03A — GPU Surface Materials / Advanced Lighting Foundation

## Intent and architectural boundary

The previous L01/L02 renderer can illuminate world layers, but some independently drawn
sprites are still tinted using a single center sample. L03A is a deliberately isolated,
real GPU-pixel-shader acceptance pass for a reusable material contract. It builds a
single shader with five authorable response modes, demonstrates two overlapping
screen-space point lights and GPU-normal/rim/emissive/mask maps, and provides a
stable headless configuration model. It **does not** replace the existing L02 world
renderer yet. In particular the `--dev-materials` lab has no geometric shadows;
that omission is intentional until the material/occlusion pass is unified.

All assets are original programmatically constructed edge masks or existing
project-owned L01 sample textures. No extra MonoGame tools are needed: the existing
`Content/Builder` compiles `Content/Assets/Effects/L03_SurfaceMaterial.fx` to an
XNB for `DesktopGL` on the first build.

## Configuring the material

`Content/Lighting/L03_SurfaceMaterials.json` provides five sample profiles:
`Unlit`, `Flat`, `NormalMapped`, `Rim`, and `Hybrid`. Optional `Normal`,
`Emission`, `Mask`, and `Edge` maps are PNGs in `Content/Lighting/Textures`.
Missing maps receive respectively neutral front-facing normal (128,128,255),
black emission, white receiving mask, and zero edge contribution.

* **Unlit** preserves authored albedo/emission regardless of dynamic lights.
* **Flat** receives normal broad surface light without normal-map modulation.
* **NormalMapped** responds per pixel to a tangent-space normal texture and
  can generate a controlled specular highlight.
* **Rim** keeps a dark front while selected authored edge pixels catch nearby light.
* **Hybrid** combines diffuse, normal, rim and emissive maps, for featured art.

This is *independent* of an object's `CastsShadows`, `ReceivesShadows`, render
layer, and parallax policy. Do not infer physical 2.5D depth from draw order.

## Launch and controls

```powershell
 dotnet build --configuration Debug -m:1
 dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~L03MaterialShaderArchitectureTests
 dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj
 dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj -- --dev-materials
```

The lab presents all five surface modes side by side, using the *same* input
albedo and light locations. Drag with the left mouse button to reposition the
cyan light. `C` toggles the second amber light. `N` disables normal strength,
`R` disables edge contribution, `F` flips a sprite/tangent normal, and `Q/E`
rotates normal orientation. `H` toggles debug text, `0` resets selection,
`1`–`5` highlight a material profile, `Esc` exits.

## Expected visual acceptance

1. Unlit remains unchanged as both lights move.
2. Flat brightens uniformly relative to the light falloff.
3. NormalMapped shows spatial shading of bevels as a light moves.
4. Rim responds primarily around an authored contour, with a dark face.
5. Hybrid combines multiple types of illumination and subtle emission.
6. Toggling `N` removes normal-map directionality, not basic illumination.
7. Toggling `R` removes rim contribution without turning off direct lighting.
8. Flipping and rotating alter tangent response consistently.
9. Existing `--dev-lights`, `--dev-particles`, and LDtk levels remain unchanged.

## Next engineering work — do not hide as 'done'

1. Integrate the material shader in the *production* per-layer compositor and
   replace center-sampled independently shaded props while retaining legacy
   per-surface fallback. This needs an explicit draw-submission material binding.
2. Unify authored render-depth and light/occluder depth. The shadow system must
   distinguish light-in-front of a sprite from light-inside-its-volume, and each
   caster and receiver must be resolved per light, not globally.
3. Preserve separate emissive, HDR/bloom, and shadow-mask stages, including
   visibility and per-light budgeting. One light's optional shadow allocation
   must not silently turn another into an unshadowed floodlight.
4. Add GPU screenshot/device tests, shader compilation on Windows + Ubuntu,
   per-layer/transparency regression, benchmarking, and an LDtk material picker.

This prototype intentionally limits itself to two local lights per sprite.
For production, prefer a shared light accumulation/material data pass over
expanding an unbounded array of lights in each SpriteBatch material shader.
Do not use lighting for gameplay decisions; it is cosmetic presentation only.
