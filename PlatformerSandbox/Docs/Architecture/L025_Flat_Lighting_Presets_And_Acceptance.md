# L02.5 — Flat Lighting: Readable and Dramatic Contrast

## Scope and safeguards

This is a **presentation-only** extension of L02 scene-layer lighting. It modifies no physics, simulation, collision, level art, light placements, scene geometry, or gameplay event mapping. The four existing lights and their editable LDtk `AffectsLayers`/`OccluderLayers` assignments are retained. Caverns A/B/C continue to default to the original `Illustrated` preset.

## Runtime controls

Launch `--dev-level=LIGHTING_FLAT_TEST`. The flat scene now defaults to **FlatDramatic**. Press **Ctrl+Alt+2** to toggle **FlatDramatic ↔ FlatReadable**. A four-second notification identifies the active preset. **Ctrl+Alt+1** toggles lighting to compare with the completely unlit artwork; **Ctrl+Alt+6** toggles the mouse-follow inspection light. The original lighting lab (`--dev-lights`) is independent and unchanged.

The setting is presentation-only; each entry/reload of the flat test resets to Dramatic. No persistent settings are modified. The two flat-specific presets are excluded from the normal cavern preset cycle.

## Editable data

`Content/Lighting/L456_GameplayLighting.json` now provides:

- `globalPresetIds`: unchanged four non-flat scene presets.
- `scenePresets`: explicit per-scene order and default, initially two for `LIGHTING_FLAT_TEST`.
- `FlatReadable`: relatively high cool ambient and broad gentle lights.
- `FlatDramatic`: significantly lower cool ambient; local lights gain intensity, and their radii shrink; player minimum remains above zero.
- `layerAmbientScales`: an optional *per-preset* multiplier keyed by the named L02 scene layer (`BG5`, `PLAYER`, `FG3`, etc.). The final ambient for a layer is the product of the original authored color, art ambient, (1 - atmospheric darkness), scene-layer `ambientScale`, and the current preset's `layerAmbientScales[layerId]`.
- `lightRadiusScale`: affects both layered SpriteBatch light pools / their shadow masks and independently lit props/characters. The authored `LightSource.RadiusWorld` in LDtk is never modified.
- `heroMinimumBrightness`: adjusts the presentation-only floor on the independently shaded player character.

Ranges are validated by `LightingArtPreset.Validate`. Unknown or duplicate scene preset IDs are rejected. Existing manifests without `globalPresetIds`/`scenePresets` retain their old behavior.

## Visual acceptance

1. Build and run the full test suite, including `L025DramaticContrastPresetsTests`.
2. Launch `--dev-level=LIGHTING_FLAT_TEST`; observe **FlatDramatic** notification after entry. The unlit reference remains bright, while the surrounding room is dim.
3. Walk past the existing warm, blue, pink and green lamps. Look for strong hotspots, shorter falloff, visible light-blocked regions, and no sudden disappearance when moving/scrolling.
4. Press Ctrl+Alt+2: **FlatReadable** should provide visibly higher ambient with broader, gentler light pools. Press again to restore dramatic.
5. Press Ctrl+Alt+1 twice to compare original artwork, then restore lighting. It must not change the level.
6. Press Ctrl+Alt+6 and move the mouse over both the pillar and the two crates. Check that shadows are continuous and respect each sprite's authored cast flag.
7. FG3 vines should stay silhouetted except where the right-hand green lamp specifically illuminates them; the unlit reference must be unchanged in both presets.
8. Confirm the player stays distinguishable in dark regions. The dramatic preset intentionally reduces, but does not remove, the player readability floor.
9. Confirm Caverns A, B and C and the particle playground retain their previous rendering behavior.
10. Compare with `--no-lighting`; gameplay movement and collisions must be unchanged.

## Limitations

This change only tunes and tests existing lighting. It does not add GPU per-pixel normal mapping, HDR bloom, physical soft shadows or additional geometry. Light culling uses authored radii as conservative bounds, so shrinking the visual pool does not accidentally eliminate a still-visible light. Scene layer participation rules and light shadow-caster masks remain unchanged.
