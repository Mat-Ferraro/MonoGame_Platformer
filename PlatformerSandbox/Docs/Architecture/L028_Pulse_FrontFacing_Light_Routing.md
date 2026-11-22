# L02.8 — Pulse / Dash Front-Facing Lighting

## Why this change exists

The flat LDtk scene's two crates are both on the `PLAYER` depth plane. The left crate is intentionally `Silhouette` and `CastsShadows = true`, with `ReceivesLight = false`; the right crate is `Lit` and also casts shadows. In L02.7, the silhouette shading rule always returned a dark face, even for a light originating at the player. Moving a light's selected layer alone could not light that front face.

## Behavior

* A **committed** Pulse activation produces a blue/cyan `flash` at the player position, as before. It lasts 0.24 seconds; its world radius is 4.2 (was 3.7).
* Pulse's explicit `AffectsLayers` contains the scene's background planes, its **one** gameplay plane, and the nearest foreground plane. More distant foreground planes are excluded. No layer IDs are hard-coded; custom scene stacks work.
* Pulse's `OccluderLayers` includes the gameplay plane plus the nearest foreground plane. Background-only occluders do not block a player-front-facing flash.
* The Pulse flash is allowed to cast shadows and sets the separate `RevealsSilhouettes` flag. Only these explicitly marked front-facing lights can temporarily illuminate a `LightingMode = Silhouette` sprite. A regular authored light leaves the silhouette dark.
* Objects remain independently authored: silhouette-versus-lit and shadow casting/receiving flags are unchanged. The right crate continues receiving normal direct light on its visible front face, excluding its own occluder hull when calculating its shading.
* The reveal uses the same distance attenuation, scene exposure, per-light routing, and optional shadow visibility as the rest of the world-lighting sampler. It fades out when the transient flash expires.
* Land and respawn flashes retain their previous routing/shadow behavior. No simulation, collision, player controls, or particle-effect timing is altered.

## Test

1. `dotnet build --configuration Debug -m:1`
2. `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~L028PulseForegroundLightTests`
3. Run the full test suite.
4. `dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj -- --dev-level=LIGHTING_FLAT_TEST`
5. Position the player beside the left dark crate and activate Pulse (`B`). The crate's front face should briefly pick up the blue light, then return to its dark silhouette. The crate should continue to occlude light behind it.
6. Dash beside the right lit crate; its visible front should brighten, and its own shadow hull should not blacken its front.
7. Compare against the static authored lights: the left crate should remain a silhouette under ordinary background/environment lights. Check the foreground vines and scene transitions for regressions.

## Limitation

The crate's front uses the existing per-object sampled tint, rather than per-pixel normal-map shading. The flash is short, so it is easiest to inspect while repeatedly activating Pulse. The scene renderer is still a layered 2D light approximation, not a full physical 2.5D depth solver.
