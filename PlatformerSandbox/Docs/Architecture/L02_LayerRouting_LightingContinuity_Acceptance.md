# L02 — Scene-defined layers, per-light routing, and lighting continuity

## Why this exists
The L01 lighting lab validated ray-visibility shadows, but the first LDtk integration multiplied one backdrop buffer then drew foreground props and the hero separately. The flat level exposed black regions, flatly lit foreground art, and illumination that appeared to disappear near some lamps. L02 replaces that one-buffer assumption with **scene layer compositing**.

## Install
Copy the ZIP contents over the existing PlatformerSandbox project. No installer/patch script. Close the running game before compiling.

## Architecture and authoring

`Content/Lighting/L02_SceneLayers.json` defines `defaultLayers` (11) and optional **complete replacement** layer stacks by `sceneId`. Definitions are ordered by integer `order`, back to front; names are identifiers rather than hardcoded enum slots. One gameplay-plane layer is required. A custom scene can have fewer or more than 11 layers (currently bounded to 1–32 for validation/performance).

Default order: `BG5, BG4, BG3, BG2, BG1, PLAYER, FG1, FG2, FG3, FG4, FG5`. The `FG3..FG5` layers default to `Silhouette` with low ambient; `FG1..FG2` remain `Lit`. Layer options:

- `mode`: `Lit`, `Unlit`, `Silhouette`. Unlit is displayed with original colors and doesn't receive dynamic light; Silhouette is a dark light receiver, and only explicitly targeted lights can reveal it.
- `ambientScale`: multiplier for the current global lighting preset's ambient on that particular layer; do not mistake this for additive brightness.
- `parallaxMultiplier`: multiplies existing LDtk/art parallax response (1 means no change).
- `visible`, `receivesShadows`, `castsShadowsByDefault`: independent default policies. Free-placed authored props inherit the cast/receive defaults when their instance fields are absent, and can override them individually. Tile collision does **not** automatically generate occluder geometry. This prevents all terrain tiles from unexpectedly casting shadows.

**LDtk authored fields** (added to ConnectedWorldDemo.ldtk and LightingFlatTest.ldtk entity definitions):

- `LightSource.AffectsLayers`: comma-separated IDs, such as `BG1,PLAYER,FG1`. Blank = previous `LightChannel` behavior.
- `LightSource.OccluderLayers`: comma-separated IDs, such as `PLAYER`. Blank = all authored blocker layers.
- `LightBlocker.RenderLayer`: layer to which the blocker belongs. Blank defaults to gameplay.
- `TerrainArtwork.RenderLayer`, `ScenerySprite.RenderLayer`, `PaintedSprite.RenderLayer`: object-specific depth/lighting layer ID. Blank falls back to the object's existing `Plane` (Background→BG1, Terrain→PLAYER, Foreground→FG1). The depth *group* must match its plane; a foreground object cannot be silently moved behind the player without changing Plane too.
- Existing `ReceivesLight`, `ReceivesShadows`, `CastsShadows`, `ContactShadow` remain independent and are not replaced by the layer settings.

**Legacy LDtk tile layer mapping:** `BG_Far→BG5`, `BG_Mid→BG3`, `BG_Near→BG1`, `FG_Near→FG1`, `FG_Close→FG3`, `Terrain_Art/Collisions→PLAYER`. Explicitly named scene-layer tile layers can use the same ID. Collision is never derived from these visual layer settings.

Light rules: a light illuminates only selected receiver layers; its independent occluder-layer list determines which authored shadows may block that light. With no explicit list, the old channel/shadow behavior remains. A surface marked `CastsShadows=false` does not become a blocker just because its layer receives shadows.

## Rendering and continuity fix

- Each nonempty depth plane gets its own transparent color capture and lighting composition, drawn in scene order into a preserved scene canvas.
- Light buffers, individual light contributions, per-light visibility masks, and the completed layer now all use `PreserveContents`, because they are sampled after switching render targets.
- Layer output preserves its alpha when multiplied by the illumination buffer, avoiding unintended darkening of previously composed layers.
- Scene canvas is presented once before HUD/UI; UI never receives world lighting. Gameplay state/physics are unchanged.
- Independently drawn textured props and hero use the same layer illumination/occluder routing as the background layers, with existing sprite self-shadow protection.
- The updated flat test uses four authored lights: the first three illuminate BG1/PLAYER (one also FG1), while the fourth additionally reveals FG3 silhouette vines. Shadow-casting and non-shadow-casting props are side by side.

## Build and tests

```powershell
 dotnet build --configuration Debug -m:1
 dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~L02LayerRoutingAndLightContinuityTests
 dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj
 dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=LIGHTING_FLAT_TEST
```

## Visual acceptance (important: cannot be tested headlessly)

1. Approach each light from both directions. Its illuminated *backdrop* must remain illuminated smoothly as the camera begins following the player. There must be no frame where the contribution abruptly disappears until the light really exits the viewport.
2. Press **Ctrl+Alt+1**. Lighting toggles without switching levels. Re-enable it.
3. Press **Ctrl+Alt+6** for the mouse-follow inspection light. Drag it behind/across the pillar and crates. Observe actual blocker behavior and continuous lighting while panning.
4. The left crate and pillar cast shadows; the otherwise identical second crate does not.
5. The **unlit reference panel** retains its normal appearance. FG3 vines appear dark without direct targeted light and are revealed near the right-hand green lamp.
6. Character lighting follows the scene's gameplay-plane configuration without becoming invisible in dark regions.
7. Caverns A/B/C still load. All graphics/audio/particle/scene regression tests pass. Compare the same level with `--no-lighting`.
8. In LDtk change one light's `AffectsLayers` from `BG1,PLAYER` to `PLAYER`, save and press **T** to reload. The backdrop should stop responding to that light while gameplay-plane objects continue to respond. Restore settings afterward.
9. Change the right green light's `AffectsLayers` to `BG1,PLAYER` and reload: FG3 vines should no longer be illuminated. Restore `BG1,PLAYER,FG3` afterward.

## Boundaries

This remains a CPU-geometry/MonoGame SpriteBatch 2D lighting pipeline. Light masks and authored depth planes are not true 3D depth. Transparent sprites are forward-lit with a single representative tint rather than a production GPU per-pixel normal-map shader. Parallax geometry can be visually independent of physical occluder geometry; authors must select appropriate occlusion planes. The code does not implement physically based HDR/bloom or accurate volumetric shadows.

Performance budgets still cap lights and shadow-casting lights per frame. More nonempty lit layers mean more per-layer draw/lighting work. Compare Full HUD render timing and frame rate for high and low quality on the target GPU; further optimization should be based on those measurements.
