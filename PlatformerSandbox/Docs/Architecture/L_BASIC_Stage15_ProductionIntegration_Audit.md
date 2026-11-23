# L-BASIC Stage 15 — Architecture Audit and First Production Integration Slice

## Scope and source baseline

This review compares the current **Stage 14 Lighting Basics** path with the existing
LDtk gameplay renderer and the original `LightingFlatTest.ldtk` diagnostic level.
Stage 15 deliberately preserves production rendering and keeps the existing
`LightingFlatTest.ldtk` unchanged. This package introduces a dedicated integration
fixture and optional presentation-only authored light motion. It is **not** a claim
of complete GPU material-shader parity.

## 1 — Audit findings (review before modifying)

| Area | Lighting Basics lab (Stage 14) | LDtk gameplay path | Status / risk |
|---|---|---|---|
| Renderer ownership | `BasicLightingLab` owns `GameplayLightingRenderer` and passes `L_BASIC_SurfaceLighting.fx` | `Game1` owns `GameplayLightingRenderer`, `LdtkLevelRenderer` calls layered composition | Shared render-target compositor, distinct shading paths |
| Light data | `BasicMaterialLight`, two editable source slots | `RadialLight`, authored `LightSource`, transient flash/particle sources | Different light contracts; needs an explicit production material-light binding |
| Shadows | Three normalized, convex `BasicShadowShape` slots with shader-based per-wall rim visibility | Authored world-space `LightingOccluder` polygons; `ApplyVisibilityMask` produces geometry shadow masks | Not yet the same GPU visibility implementation |
| Layer/depth rules | Back / World / Front, per-source overlap mode and directional rim policy | Explicit ordered `SceneLightingLayer` IDs, channel fallback, `AffectsLayers` and `OccluderLayers` | Production does **not** automatically inherit the lab's behind-wall face suppression/rim behavior |
| Materials | Flat/Normal/Rim/Hybrid GPU sprite pass | Per-layer albedo composition; individually drawn objects may use center-sampled lighting | Per-pixel material parity remains open |
| Motion | Moving/rotating preview polygons via presentation clock | Moving platforms from LDtk simulation, scene static lights, player-following transient pulse | New Stage 15 authored moving lights use presentation time; no simulation writes |
| Budgets | Bounded fixed shader slots (2 lights, 3 shapes) | `LightingFrameBudget`, visible light selection, per-layer shadow quota | Do not silently expand shader arrays or assume limits are identical |
| Validation | Headless lab geometry tests and manual GPU review | LDtk import / scene tests, production lighting policy tests | Needs cross-path screenshot regression on Windows/DesktopGL |

### High-priority unresolved gaps

1. **Draw-submission contract**: select lighting material, normal/edge/emissive maps,
   receiver channel, shadow participation, depth, and occluder identity for each
   real level surface. This must be an explicit presentation-only draw command,
   not a new collision/physics property.
2. **Per-light, per-wall depth and occlusion**: integrate `BasicPolygonBackRimReference`
   behavior into the real world material shader, translating LDtk layer order into
   front/behind decisions. Keep exact self-shadow exclusion and occlusion by other
   casters. Do not route behind-wall lights into face diffuse lighting.
3. **Production shader parity**: merge the lab's normal, flat, hybrid, and directional
   rim variants into the production material pass. Avoid replacing whole-layer
   lighting in one untested commit; opt in per material with a fallback.
4. **Performance**: avoid an unbounded number of per-object/per-light GPU passes.
   Measure shadow-mask reuse, draw calls, RT allocation, and geometry cap before
   raising 2-light / 3-blocker lab budgets.
5. **Screenshot and device coverage**: validate DesktopGL shader compilation,
   transparency, layer ordering, cast/receive policy, occluder transforms, resize,
   and GPU output on Windows and Ubuntu.

## 2 — Implementation in this package (first production slice)

**New pure, shared model:** `PresentationLightMotion` in Foundation takes an
immutable light anchor, axis, amplitude and cycles/second, returning a deterministic
world-space position. It makes no gameplay or GPU calls.

**LDtk authoring:** `LightSource` may now opt into `MotionAxis=Horizontal|Vertical`
with `MotionDistanceWorld` (0.01–10 world units) and
`MotionCyclesPerSecond` (0.01–2). Missing/Static motion remains unchanged.
Invalid field types/values fail import. The importer stores animation data
separately from the immutable authored `RadialLight` instances.

**Presenter bridge:** `GameplayLightingPresenter` advances a presentation clock,
clones only the animated light positions for `BuildVisible`, and passes those
same lights into the existing gameplay illumination and shadow composition.
Pausing freezes the clock and entering another world resets it. Gameplay physics,
entity coordinates, and `LdtkLightingScene.Lights` are untouched.

**New real LDtk fixture:** `Content/Levels/LightingIntegrationTest.ldtk` is a
copy of `LightingFlatTest.ldtk` under scene ID `LIGHTING_INTEGRATION_TEST`, with
its own moving platform and one blue authored moving light. The existing art
contains background, floor, silhouettes, lit/unlit surface samples and shadow
casters; the playable character can trigger its existing event lights.
Scene-specific dramatic/readable art presets and 11-layer routing are copied to
this independent scene; the original fixture remains untouched.

The dedicated scene uses the **production** LDtk discovery, import, gameplay,
`GameplayLightingPresenter`, and `GameplayLightingRenderer.ComposeLayer` path.
It does *not* use the Lighting Basics lab's per-pixel GPU material shader.

## 3 — How to verify

From the project root:

```powershell
 dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~L15ProductionLightingBridgeTests
 dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj
 dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj
```

Select **LIGHTING_INTEGRATION_TEST** from the level list (it is discovered from
`Content/Levels`; no hardcoded menu ID). Use normal movement controls to cross
the scene. Compare stationary amber/pink lights with the horizontally moving
blue source. Verify the platform moves independently and the blue light/shadows
remain stable while the game is paused. `Ctrl+Alt+2` swaps the scene's dramatic
and readable lighting presets; `Ctrl+Alt+1` toggles gameplay lighting.

**Acceptance criteria:** scene discovery/preparation succeeds; original flat level
still loads; animated light updates with pause/resume; static lights do not drift;
platform moves under physics without altering light animation; produced lighting
uses the same per-layer light routing and shadow budgets as existing LDtk gameplay;
there is no lab-only renderer instantiation in the new level.

**Known limitation:** behind-wall directional rim and per-pixel material maps
are **not** yet present in this production scene. That is Stage 15B work, not a
bug to conceal in the authoring test. Full-screen bloom/post-processing remains
on the separate deferred roadmap.

## 4 — Next ordered integration pass (Stage 15B)

1. Introduce `LightingSurfaceSubmission` data in presentation-only Foundation:
   material profile, maps, visibility policy, layer, source identity, and shadow
   receiver/caster ID; keep world simulation uninvolved.
2. Adapt `LdtkVisualSceneAssets` so production surfaces may opt in to the
   `L_BASIC` shader without changing the legacy capture path by default.
3. Bind light lists and occluder geometry from `LdtkLightingScene` to those
   surfaces; implement the Stage 12 light-visible directional back-rim rule.
4. Add side-by-side GPU image comparisons and budget/performance probes before
   replacing existing center-sampled sprite shading globally.

This order intentionally guards the working Stage 14 lab and existing LDtk
levels from a one-shot compositor rewrite.
