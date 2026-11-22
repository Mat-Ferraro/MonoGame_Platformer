# L03.0 — Dash light inside an occluder: shadow consistency fix

## Root cause
`ShadowVisibility.BuildPolygon()` and `CanIlluminate()` skipped an entire shadow-casting polygon when the light source was inside/on it. `ShadowGeometry.IsOccluded()` made the same choice for separately shaded sprites. This caused a discontinuity when a player-created dash flash crossed a decorative crate's 2D outline: the shadow caster disappeared until the light was outside again.

## Change
A light inside a solid now traces to the first **exit** edge, rather than ignoring the polygon. Both the GPU visibility mask and CPU forward sprite sampler use this rule. A sprite continues to shade its own visible face separately (its own occluder is skipped by `LightingWorldSampler`), so Pulse front-face reveal remains intact. Other scene lights are unchanged. No gameplay or authored LDtk data is altered.

## Limitations
These are still 2D planar shadows. A light genuinely embedded in a solid is physically trapped behind its edges; in a production 2.5D renderer a player-attached light on a different depth plane may illuminate a front-facing crate while casting a shadow onto a more distant plane. That will require explicit light depth/receiver depth and is not inferred from 2D overlap. A rapid move into a solid can thus reduce illumination on the backdrop sharply, but will **not** make the occluder disappear or leak light behind it.

## Tests
1. `dotnet build --configuration Debug -m:1`
2. `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~L030EmbeddedLightOcclusionTests`
3. `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj`
4. Run `--dev-level=LIGHTING_FLAT_TEST`, dash beside and inside each crate. Confirm the crate continues to block the backdrop and that no sudden unmasked full light circle appears. Compare ordinary lamp shadows, lit crate, silhouette crate, and the `--no-lighting` baseline.
