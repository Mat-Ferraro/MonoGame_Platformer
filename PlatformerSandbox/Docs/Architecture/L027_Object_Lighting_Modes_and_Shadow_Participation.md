# L02.7 — Object Lighting Modes & Shadow Participation

**Scope:** LDtk visual-only object authoring, independent object light/shadow participation, and avoiding double illumination in the layered renderer. No physics, collision, movement, or simulation changes.

## Authoring

`TerrainArtwork`, `PaintedSprite`, and `ScenerySprite` now expose a **LightingMode** string field in LDtk:

| Value | Meaning | Local light on visible face |
|---|---|---|
| `Inherit` (default or blank) | Keep existing `ReceivesLight` and scene-layer behavior | Per layer/receiver flag |
| `Lit` | Explicit forward-lit sprite (also available on an Unlit layer) | Yes |
| `Unlit` | Preserve source artwork color | No |
| `Silhouette` | Dark/backlit visible face (fixed cool-dark tint) | No |

The importer validates these strings and rejects invalid values. `LightingMode` is **not** the same as `CastsShadows`: any lighting mode may independently cast a bounds-based shadow. `ReceivesShadows` and `ContactShadow` are likewise separate. Do not confuse the decorative `ContactShadow` oval with a real light-occlusion shadow.

For **selectively revealable foreground vegetation**, use `LightingMode=Inherit` on an FG layer whose scene mode is `Silhouette` and target that layer with specific authored lights. This preserves the existing FG3 vines behavior. The explicitly authored **object silhouette** mode intentionally stays dark even next to a bright light, for a backlit occluder; it does not yet implement automatic rim lighting.

## Flat lighting test fixture

`Content/Levels/LightingFlatTest.ldtk` has these intentionally different objects:

- **Left crate:** `LightingMode=Silhouette`, `ReceivesLight=false`, `ReceivesShadows=false`, `CastsShadows=true`, `ContactShadow=false` — dark object in front of a bright background; occludes assigned light targets.
- **Right crate:** `LightingMode=Lit`, `ReceivesLight=true`, `ReceivesShadows=true`, `CastsShadows=true` — light-reactive prop, now also casts a projected shadow.
- **Receiver panel:** `LightingMode=Inherit`, receives lights, does **not** cast a shadow — participates in per-pixel world light composition.
- **UNLIT REFERENCE:** `LightingMode=Unlit`, does not cast or receive shadows — intentionally unaffected by lights.
- **FG3 vines:** `LightingMode=Inherit`, selectively illuminated according to FG3 light routing.

The original collision floor, character gameplay, level lights, and Caverns A/B/C remain unchanged. Existing connected-world art is explicitly assigned `Inherit` to preserve its previous look.

## Rendering changes

Ordinary world receivers are drawn to the albedo pass **without a pre-applied point-sampled lighting tint**, then shaded once when the layer light buffer is composited. Shadow-casting sprites and explicit object-mode overrides are drawn **after** the backdrop's lighting/shadow mask with their own forward shading. Forward shading passes the sprite ID to the headless sampler to avoid self-shadowing by that sprite's occlusion hull. Objects on otherwise `Unlit` render layers can still opt into independent `Lit` shading.

**Current limitation:** independent forward-lit props receive one center-sampled light tint over the full sprite, not pixel-by-pixel normal-mapped light. This is adequate for testing layer/occlusion participation, but precise per-pixel character/prop surface lighting will require a later GPU material pass. Bounds-based shadow silhouettes are still approximations of sprite artwork.

## Acceptance checks

1. Run `dotnet build --configuration Debug -m:1` and the full xUnit suite.
2. Start `LIGHTING_FLAT_TEST`. Check the two crates have matching artwork but different front-face lighting, and both can block a light.
3. In Dramatic mode, the left crate stays a dark **silhouette** even against bright blue or warm illumination; its *background* becomes illuminated behind it.
4. The right crate lights up and casts a shadow onto the backdrop/floor when its light's `OccluderLayers` and `AffectsLayers` include the relevant planes.
5. The receiver panel varies with light position, while the UNLIT REFERENCE stays unchanged.
6. Use `Ctrl+Alt+6` to move the inspection light past each crate and verify the light does not darken the caster's own visible front face.
7. Verify `Ctrl+Alt+2` still switches FlatReadable/FlatDramatic, and FG3 vegetation responds only to assigned lights.
8. Revisit Caverns A/B/C and `--dev-lights` to check regressions. Compare with `--no-lighting` to confirm the simulation is unchanged.

**Do not consider GPU appearance automatically validated:** graphical rendering and .NET compilation must be run on the development machine.
