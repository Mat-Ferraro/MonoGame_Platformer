# L01 Lighting Lab — Shadow / Surface Composition Correction

## Reported symptoms

- Large triangular shadows seemed to cut through the wrong surfaces as the draggable light moved.
- Short bright or dark seams appeared around occluder corners.
- Textured material demonstration panels looked illuminated, but wall shapes appeared almost completely black.

## What was wrong

The original lab drew the wall and column occluders only as thin outlines within the **backdrop** render target. Its per-light GPU shadow geometry erased illumination from that entire backdrop (including the blockers' own outlines). In contrast, its four texture panels were forward-shaded separately with a CPU material evaluator; the evaluator ray-tested all world-space occluders even though those demonstration panels visually sit **in front of** the occluders. The two rendering paths did not share a clear surface/receiver ordering policy.

This was not a MonoGame DepthStencilState or ordinary SpriteBatch sort-depth error; there was no depth information for these test surfaces. It was a **shadow receiver and composition** error in a purposely isolated prototype.

## Changes

1. Polygonal occluders now produce a full solid mesh, including concave polygons. The complete silhouette is also blacked out in the temporary per-light shadow mask, reducing leaks/pinholes where projected edge wedges join.
2. Solid wall/column surfaces are drawn AFTER the background light/shadow composite, so they can receive direct light without being erased by shadows they themselves cast. The outlines also respond to light.
3. The material contract supports `receivesOccluderShadows` (default true). All eight foreground *lab* material panels explicitly set this to false: they continue receiving colored direct light, but no longer incorrectly receive back-wall occlusion. Other real receivers can opt in.
4. The existing three buffers (`4`), shadow toggle (`3`), normal-map toggle (`2`), and all other playground controls remain unchanged.

## Test procedure

1. Launch with `--dev-lights`, select Cavern with F1, turn shadows ON with `3` if necessary.
2. Drag the selected amber light around stone column corners and observe its shadow cast **behind** the now-solid column, while the column surface remains visible and lit. Mild single-pixel jaggies can remain: this is still the hard-shadow prototype, not an anti-aliased soft-shadow production renderer.
3. Observe the foreground material panels: they illuminate with radial light and normals but are **not** darkened by occluder geometry behind them.
4. Toggle `3` OFF: only the cast shadow should disappear. The wall surfaces and material panels remain lit.
5. Use F2 for the concave atrium test and repeat. In particular, the notch in the L-shaped wall must **not** be filled in.
6. Toggle `4` through scene, light map and combined outputs; use `1` to verify lighting-off visibility.
7. Verify the standard game, particle playground, and previous L123 test suite are unchanged.

## Scope / deferred work

This corrects the lighting **laboratory**. It is not a general 2.5D light-depth renderer: a real level requires explicit per-parallax-layer receiver and shadow-plane policies, surface materials (including non-textured geometry), and GPU normal/material shading. Soft shadows, proper antialiasing, and production light integration remain later lighting milestones.
