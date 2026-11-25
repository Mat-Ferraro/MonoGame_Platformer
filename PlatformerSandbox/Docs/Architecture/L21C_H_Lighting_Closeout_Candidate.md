# Phase 21C-H: lighting closeout candidate

## Baseline and install
Incremental replacement files for the repository export PlatformerSandbox(1).txt plus all subsequent Phase 21 updates through the user-validated Phase21B2b particle lighting package. Do not reinstall earlier Phase 21 packages on top of this one. Keep the existing shader sources and all installed content. This package does not replace previous lab levels or the wall-overlap and particle policies.

Run from the PlatformerSandbox root in PowerShell:

```powershell
py -m pip install pillow shapely
py Tools\phase21_closeout_preflight.py
py Tools\phase21_spatial_preflight.py
py Tools\phase21_emission_fixture_preflight.py
dotnet build .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj
```
Python dependencies are for the offline authoring/preflight tools only. No new .NET package is required.

## C: finite emitters
Production rendering selects logical lights before expanding extended sources. Existing Tube becomes a line emitter; the authoring `EmitterFootprint` selects Line, Disc, Rectangle. `TubeLengthWorld` specifies length or disc diameter; `EmitterWidthWorld` specifies rectangle width. Each sample retains the logical ID, routing, depth, shadow owner, and a normalized share of emitted intensity. The existing point-light falloff, per-light visibility and material accumulation paths evaluate samples individually. This is bounded quadrature, not exact analytic area lighting. Low/Balanced/High use 3/5/9 samples respectively; low quality still deliberately disables shadows under the old budget. The lower-quality rectangular footprint is a coarse approximation. Large sources can be dimmer at their center than the previous capsule approximation because energy is spread across the footprint. The CPU world sampler uses high-quality samples for fallback; the legacy standalone RadialLighting.Attenuation API retains its prior capsule formula. Soft filtering remains independently controllable; the extended source itself generates a sampled penumbra even with hard masks. Logical light/shadow quotas remain unchanged. Maximum material sample workload is 12 x 9; measure this before choosing large numbers of shadowed area sources. No claim of measured GPU cost.

## D: mapped motion and receiver coverage
New L21_TangentTransform.fx transforms map XY directions during the existing map capture; flip before clockwise screen-space rotation, preserve encoded Z and edge eligibility alpha. The actual transformed artwork determines the map orientation, including animated poses. Static unrotated/unflipped art retains its old path. Authorable deterministic intensity/color pulses on light sources use LightPulseDepth (0..1), LightPulseHz, optional LightPulseTint. Existing presentation motion and attached emissive light center updates still operate. Explicit Lit overrides on unlit layers and independent inherited silhouette-layer artwork can enter the spatial receiver path; explicit silhouette reveal retains the old CPU reference behavior. Animated character-specific normal/emission atlas bindings, nonuniform tangent-scale correction, fully general dynamic 3D receiver orientation, and arbitrary particle emission/shadow caster support are not complete.

## E/F: authoring
`py Tools\phase21_shadow_hull.py <image.png> --alpha 128 --tolerance 1 --preview hull.png --json hull.json` outputs normalized ShadowVertices and a preview. Paste those fields into LDtk and set ShadowShape=Custom, ShadowHullInsetPx=0. Offline pixel-row union and topology-preserving simplification supports concave and disjoint solid contours. Enclosed holes, touching contours, empty masks and over-budget shapes are explicitly rejected, not silently filled. Output geometry is cached as authored LDtk fields; no per-frame alpha readback. Automatic animation-frame contour switching and closed-hole runtime support are deferred. `Tools/phase21_lighting_presets.json` and `phase21_apply_lighting_preset.py` provide reusable offline presets; target project must already declare the fields (see the new lab's LightSource definitions). `py Tools\phase21_apply_lighting_preset.py Content\Levels\Phase21LightingLab.ldtk <entity-iid> soft_line --output edited_lab.ldtk` writes a separate file. Do not leave two copies with the same level ID in the Content/Levels discovery directory. No visual LDtk plugin or automatic project-wide schema migration is supplied.

## G/H: dedicated validation level
Select LIGHTING / PHASE 21 LAB. It starts at key 2, with hard per-sample masks, bloom and self emission off, and high quality. Move A/D, jump Space, respawn R. Keys:
- 1 midpoint point comparison; 2 line; 3 disc; 4 rectangle, same logical source location and intensity.
- 5 combined test: rectangle, animated colored light, three attached emissive world lights, self emission and bloom; walk and jump to observe player and particle colors.
- 6 integrated stress: includes stress candidates (existing logical budget still applies).
- 7 hull/range/sample overlay (green lit-face, orange higher virtual-depth hulls, yellow sample positions; outline is a conservative range bound, not exact contribution shape).
- 8 cycle High/Balanced/Low. Low turns off shadows by existing design.
- 9 benchmark; 120 warm-up then 300 rendered frames, export to %LOCALAPPDATA%/PlatformerSandbox/LightingBenchmarks. CSV records CPU submission time, logical lights, repeated area sample evaluations, triangles, material/layer mask counts and estimated target memory. Do not move during repeatable comparisons unless following an identical motion script. Key 1-6 and quality changes cancel active capture. These are not GPU timestamps or total frame-time measurements.

The integrated lab deliberately uses small, neutral graybox witnesses rather than unrelated decorative artwork. Compare keys 1-4 behind center blocker; light should be visible from unobstructed portions of a finite source. Walk across the existing two wall depths and into colored shadows in the combined mode; dust must follow the same lighting. Check static reflected and rotating/moving mapped irregular rocks, generated hull alignment and attached emissive source, transparent receiver, and animation/pulse. Previous Emissive Color Lab remains available for isolated two-wall and hue-transfer regression tests. Previous Lighting Basics/Integration, Light Types, Material Showcase remain available. New dedicated lab is not a final illustrated art-production environment; that requires production artwork and local visual validation.

## Validation limitations and remaining work
The reconstructed source export excludes original effects, csproj and binary content not in the incremental ZIPs. No .NET SDK/MonoGame compiler or GPU is available in the preparation environment. All three Phase 21 Python preflights passed against the reconstructed tree. New xUnit tests were authored but not executed. Shader compilation, visual checks, benchmarking and final correctness audit must be run locally. The finite-source integration can increase mask work substantially; optimize only after collecting data. GPU timestamp profiling, measured optimization, enclosed-hole support, frame-specific silhouettes, a full illustrated environment and remaining explicit silhouette/character-map paths remain open. This is a combined implementation candidate across the remaining stages, not a claim of completed production acceptance.
