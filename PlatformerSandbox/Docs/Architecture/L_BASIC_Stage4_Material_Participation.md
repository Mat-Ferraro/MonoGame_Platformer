# L-BASIC Stage 4 — Per-surface material participation

## Scope

This update extends the existing Stage-3 isolated Lighting Basics lab. It preserves
`--dev-lighting-basics`, the main-menu **LAB / LIGHTING BASICS** entry, the
collapsible inspector, the two independent colored lights, and the production
lighting path. No physics, LDtk world data, shader assets, post-processing,
normal maps, rim lighting, or shadows are changed.

## New controls

The inspector now has two tabs: **LIGHTS** (the prior Stage-3 controls) and
**SURFACES**. Surfaces has a sample selector (A–D), Lit/Unlit material mode,
receiver channel (both lights, light 1 only, light 2 only, ambient only), opacity,
and reset-selected-sample. The whole panel still collapses by clicking its header.
UI mouse gestures cannot accidentally move a light. Press **R** to reset all.

## The samples

* A: lit, receives light 1 + 2, fully opaque.
* B: lit, receives light 1 only, fully opaque.
* C: unlit, receives neither local nor ambient, fully opaque.
* D: lit, receives light 2 only, 55% opacity.

All four use the SAME generated checker/stripe texture with two transparent cutouts.
The texture is single-draw, not a stack of individually transparent primitives.
Background and floor are lit surfaces that receive both lights.

## Architecture

`BasicSurfaceMaterial` and `BasicMaterialLight` belong to the reusable
Foundation presentation layer. The explicit `BasicLightReceiverChannels` values
are associated with the light *identity*, not the index within an enabled-light
list. When light 1 is off, light 2 is still routed only to light 2 receivers.

The Stage-4 material path is part of `GameplayLightingRenderer`, and uses its
albedo, illumination, lit-layer and layered-canvas render targets. For each
submitted surface in back-to-front order, it:

1. Draws the full surface color with straight-alpha input into an intermediate
   **premultiplied** RGBA texture. Transparent cutouts remain transparent.
2. Accumulates ambient and only authorized lights, unless the surface is Unlit.
3. Multiplies lit colors by the per-pixel illumination map **once**, preserving
   alpha, or bypasses light entirely for Unlit surfaces.
4. Composites the result with premultiplied alpha, applying the whole surface's
   opacity **once**, so translucent samples reveal the previously lit world.

This is an intentionally explicit per-surface reference path, not yet the
optimized production multi-material compositor. It adds one render target for
the correctly ordered albedo debug view. The existing `ComposeBasic` methods
and production layer paths are retained for compatibility.

**Diagnostic views:** Albedo and Final show material depth/alpha; Light Buffer
shows raw scene illumination before material receiver filtering; Selected Light
shows one source's raw energy. Do not mistake an unmasked debug buffer for the
per-surface receiver mask.

## Manual acceptance (GPU required)

1. Start in the main menu > Select Level > LAB / LIGHTING BASICS, or pass
   `--dev-lighting-basics` on the command line.
2. In LIGHTS: disable both lights, ambient=0. In FINAL, A/B/D and the wall
   must go black, but C must retain its original colors. Transparent holes in
   all four samples reveal the dark wall.
3. Turn ambient up. A/B/D and the wall brighten evenly, while C remains
   unchanged. Change C to Lit and it should now respond to ambient.
4. Light 1 red; light 2 blue; both enabled and overlap. A receives both, B
   receives red only, D receives blue only, C remains unchanged. Check
   that the underlying wall remains lit through both cutouts and through D.
5. Swap B between None, Light1, Light2 and Both. None still receives ambient.
6. Reduce D opacity to 0.25 then zero. It must reveal the already-lit wall
   correctly (not tint the wall a second time). At opacity zero it disappears.
7. Compare FINAL and ALBEDO. ALBEDO is not affected by changing lighting.
   Inspect raw light/selected light views separately.
8. Collapse the panel, drag a light, expand the panel, and confirm dropdown
   clicks do not reposition the light. Restart and return to the main menu.

## Deferred

* GPU screenshot/reference comparisons, performance budgets and batching
* Authored pixel-level receiver masks (distinct from transparency alpha)
* Normal maps, directional rim, geometric shadow/depth, HDR and bloom
* Full rendered-frame post-processing (still a separate future TODO)
* Applying the validated material path to production LDtk scenes

## Regression

Run `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj`
and launch the lab. Automated tests validate the material state and stable
light-to-channel routing, but **cannot** certify GPU blending.
