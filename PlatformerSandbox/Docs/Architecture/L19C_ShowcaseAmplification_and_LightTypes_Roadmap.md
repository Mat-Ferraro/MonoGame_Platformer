# L19C — Showcase Amplification and Light-Type Roadmap

## Goal
Make the material/emissive lab *teach* the feature, not merely contain it.

## Design principles
- Prefer a single visible, menu-selectable teaching lab over hidden keyboard-only demos.
- Every station should answer both **what changed visually** and **why a designer would choose it**.
- Use exaggerated values in the teaching lab; production tuning can remain more restrained.
- Keep these changes presentation-only and independent from gameplay simulation.

## What this patch delivers
- Stronger emissive stations.
- A curated preset ladder that deliberately isolates self-emission, bloom, transparency,
  ghost opacity, hologram/HDR, and a final boosted comparison state.
- An in-panel teaching aid (`SHOWCASE FOCUS`) describing the purpose of each station.

## Near-term next steps (Phase 19D / 20 candidate)
1. **Explicit light classes**
   - Scene / directional (sun, moon, skylight wash)
   - Point lights
   - Spot lights / cones
2. **Dedicated light-type lanes**
   - Sun lane: same geometry under different global light directions/intensities
   - Point lane: local falloff and shadow footprint
   - Spot lane: cone angle, penumbra, theatrical reveal
3. **Bloom quality improvements**
   - Multi-resolution bloom for broader, softer halos
   - Soft-knee thresholding
   - Bloom-only debug view
4. **More dramatic authored examples**
   - Stained glass / colored panes
   - Ghost / force-field / hologram comparisons
   - Fluorescent sci-fi tubes with no nearby point light

## Rationale
Directional / sun lighting is a separate conceptual category from material emission.
This patch does not pretend otherwise. Instead, it improves the clarity of the existing
material showcase now, while explicitly reserving full light-type support for the next step.
