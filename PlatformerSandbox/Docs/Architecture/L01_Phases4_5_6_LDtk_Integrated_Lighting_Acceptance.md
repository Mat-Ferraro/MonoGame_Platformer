# L01 — Lighting phases 4–6: LDtk integration, authoring, performance

## Scope and invariants

This is a combined integration pass against the previously validated lighting laboratory and P01–P06 particle system.

* **Phase 4 — engine integration:** world/background receiver composition, independently shaded sprite/hero surfaces, LDtk lights and optional shadow blockers, committed gameplay event flashes, budgeted representative lights for particle effects, scene ownership.
* **Phase 5 — artistic authoring:** independent receives-light / receives-shadows / casts-shadows / contact-shadow controls for LDtk free-placed artwork, four JSON mood presets, a simple light halo / posterized falloff option, reduced-flash preference, editor placement examples.
* **Phase 6 — production controls:** fixed per-frame quality budgets for light count / shadowed lights / geometry samples / particle lights, 120-candidate diagnostic stress toggle, CPU command-submission measurements in Full debug HUD, scene lifecycle and regression tests.

This module is *presentation-only*: no simulation, movement motor, collisions, RNG, damage, or game state may depend on lighting. Existing `--dev-lights` (Phase 1–3 lighting laboratory) and `--dev-particles` (particles laboratory) remain available.

## Install, build, tests

Extract the ZIP at the project root, overwriting files at their original relative paths.

```powershell
dotnet build --configuration Debug -m:1
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~L456GameplayLightingIntegrationTests
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj
```

Launch each authored cavern, then use the **Full** debug HUD:

```powershell
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=CAVERN_A
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=CAVERN_B
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=CAVERN_C
```

`--no-lighting` runs the same authored level with lighting disabled, for gameplay and visibility comparison. `--no-particles` independently disables gameplay particles and their particle-light requests.

### In-game lighting controls (while in an LDtk level)

| Chord | Behavior |
|---|---|
| `Ctrl+Alt+1` | Toggle integrated lighting |
| `Ctrl+Alt+2` | Cycle `Illustrated`, `Moonlit`, `Graphic`, `Readable` art presets |
| `Ctrl+Alt+3` | Cycle `Low`, `Balanced`, `High` quality |
| `Ctrl+Alt+4` | Toggle reduced-intensity gameplay flashes |
| `Ctrl+Alt+5` | Toggle 120 **candidate** stress lights; only budgeted lights are submitted |
| `F11` / tilde | Existing debug HUD mode selection |
| `T` | Existing LDtk reload/restart workflow |

The 120-candidate toggle tests admission/culling/prioritization under load, **not** the rendering of 120 full lights at once. The HUD shows selected light/shadow counts, shadow fan triangles, CPU command submission mean/peak, current preset, quality and stress status. The timing value is **not a GPU timer**, and hardware frame/GPU benchmarks must be evaluated on the target machine before performance claims.

## LDtk: authoring a new light

Open `Content/Levels/ConnectedWorldDemo.ldtk` in LDtk and select the main `Entities` layer. Caverns A, B, C each have two sample `LightSource` entities. Place, duplicate, or move one, then edit:

| Field | Meaning | Range |
|---|---|---|
| `Color` | Light RGB color | `#RRGGBB` |
| `RadiusWorld` | Approximate influence radius in world units | >0 to 60 |
| `Intensity` | Nonnegative illumination multiplier | 0 to 4 |
| `Priority` | Selection importance when over budget | −100 to 100 |
| `CastShadows` | Whether this light uses occlusion | true / false |

Save the LDtk file; reload the level with `T`. There is no requirement to add collision data, light components to the movement motor, or hardcode a level ID in the renderer. Light-source positions use the LDtk entity center and the existing Y-up, 32 px/world-unit convention.

Optional `LightBlocker` entities define rectangular 2D occluders; size the entity's rectangle in LDtk. These cast shadows but are not visible surfaces and do **not** alter player collision. A polygon occluder may be added by presentation APIs, but the LDtk authoring pass currently exposes rectangles, not an arbitrary vertex editor.

## LDtk: independent textured-surface shadow controls

`ScenerySprite`, `PaintedSprite`, and `TerrainArtwork` have four independently authored boolean fields:

* `ReceivesLight` — texture participates in local light and ambient shading.
* `ReceivesShadows` — occluders may interrupt local lighting on this texture. A false value draws it in an independent forward-shaded pass, not through the backdrop shadow mask.
* `CastsShadows` — this object's rotated, rectangular image bounds are used as a shadow hull, independently of collision geometry. The actual texture alpha does **not** yet define the shadow silhouette.
* `ContactShadow` — opt-in decorative soft ellipse under the artwork. This is **not** a dynamically cast shadow. Default is false.

Enabling `CastsShadows` moves the visible artwork to a subsequent forward receiver pass, preventing an object from blacking out its own face. Some preauthored textured sprites in Caverns A/B/C demonstrate casting; non-casting textures stay in the ordinary backdrop receiver pass. The `Plane` / foreground/background artwork assignment still governs overall draw order.

**Explicit limitation:** this is an illustrator-friendly 2D planar renderer, not full scene depth or normal-mapped forward/deferred 2.5D. Background and terrain tile layers share the world light buffer, selected free-placed sprites can use their own forward light-tint; HUD/menu remains unlit. The current receiver shader uses texture color × illumination (and an approximate point-sampled tint on independent sprites/hero). It does not apply the lighting lab's CPU normal-map reference shader to every real gameplay sprite. Full GPU normal-map materials, accurate 2.5D shadow planes, penumbra, robust transparent-material shadows, multi-pass tone mapping, and HDR convolution bloom are follow-up possibilities, not claims of this pass.

## Particle- and gameplay-linked light

`Content/Lighting/L456_GameplayLighting.json` binds optional light proxies to particle effect IDs (`fx.fire`, `fx.zoneEmbers`, `fx.spores`, `fx.respawn`). The presenter requests at most one light per *world-space effect instance* (plus the quality budget), **not one light per particle**. No particle simulation code needs to know about GPU lights. Unbound effects remain purely emissive-looking artwork.

Confirmed gameplay events emit brief cosmetic light flashes: `AbilityActivated` for Pulse, `PlayerRespawned`, and `Landed`. They are post-commit, world-ID-scoped, deduplicated and time-limited. World flashes pause with gameplay. They do not change movement or collision. `Ctrl+Alt+4` reduces flash brightness; there is no mandatory strobing.

Level reload, transition to title, replacement of world instance, and normal shutdown clear scene-owned light state. Old scene's light nodes must not leak into the next cavern or title.

## Art and performance presets

Edit `Content/Lighting/L456_GameplayLighting.json` to define mood: ambient RGB, global light multiplier, atmospheric darkness, `bloom` (a **cheap additive halo**), `bloomStrength`, and `posterize` (discrete falloff). Values are validated on loading; malformed config enters the game's optional-lighting fallback, with a visible debug warning rather than a gameplay failure.

| Quality | Maximum visible lights | Shadowed lights | Shadow radial samples | Particle light proxies |
|---|---:|---:|---:|---:|
| Low | 4 | 0 | 64 | 1 |
| Balanced | 8 | 2 | 128 | 3 |
| High | 12 | 4 | 192 | 6 |

This is a bounded CPU/SpriteBatch rendering implementation, using per-light visibility masks and render targets. It degrades optional shadow work before high-priority scene illumination. User-facing framerate and GPU performance must be assessed on the actual target laptops / drivers.

## P4–P6 acceptance checklist

1. **Build + all headless tests** succeed; `L456GameplayLightingIntegrationTests` validates authored world import, independent flags, shadow geometry, budgets, manifest validation, deduplication, pause and scene ownership.
2. **CAVERN_A/B/C** each show two authored colored lights; moving and reloading one in LDtk changes what you see.
3. **Caster vs receiver:** the preauthored casting sprite blocks the background light yet has an illuminated front face; a non-casting textured sprite still receives light. Toggle `ReceivesShadows=false`, `CastsShadows=false`, `ReceivesLight=false`, and `ContactShadow=true` individually, reload, and compare.
4. **Particles and game events:** fire/embers/spores (when visible) add bounded local light; jump/land/dash/respawn movement and particle visuals stay unaffected; accepted Pulse triggers a brief flash and rejected actions don't.
5. **Layering:** player remains readable, HUD/menu stay unlit, parallax and foreground art still draw in the proper order, and `--dev-lights` lab behavior remains unchanged.
6. **Lifecycle:** visit A→B→C→title→A, respawn and `T` reload; no accumulated old scene lights or crashes; pause prevents world flash expiry.
7. **Quality/art:** toggles cycle correctly and the number of selected/shadowed lights stays <= advertised budgets; stress still runs while full debug HUD shows timings.
8. **Gameplay isolation:** repeat the same movement route with and without `--no-lighting` and confirm collision, acceleration, wind, exits and camera remain identical; full regression suite passes.

## Follow-up if this pass succeeds

Once measured on real GPU hardware, production enhancements may include a GPU normal-map material shader, more precise per-illustration occluder geometry and light planes, anti-aliased/soft penumbra shadows, optimized per-layer compositing, and actual tone mapping/bloom as needed. These are deliberately **not** treated as solved or already tested by the present implementation.
