# L-BASIC Stage 6: hard rectangular shadows in the isolated lighting lab

## Scope and non-goals

This stage adds **one authored screen-space axis-aligned rectangle** that can
occlude either of two independent lab lights. It deliberately does **not**
replace the production `GameplayLightingRenderer` polygon visibility system,
implement soft shadows, add pseudo-3D height or true depth planes, integrate with
LDtk, or implement postprocessing. The existing renderer's non-lab paths are
unchanged. The lab still uses its GPU material shader for all lit surfaces.

The test geometry is `BasicShadowRectangle` in normalized [0,1] viewport
coordinates. CPU reference `Blocks(light,receiver)` and the shader's
`RectangleVisibility` both evaluate open-segment ray/axis-aligned-rectangle
intersection. Rays grazing the border should not darken. Light-inside and
receiver-inside the blocker are skipped; this is a defined limitation, not a
physically accurate 2.5D self-shadow result. The rectangle's visible *face*
is drawn last as a lit object with `ReceivesShadows=false` to avoid self-darkening.

## Ownership and composition rules

- Shadow blockers are presentation-only geometry (not physics colliders).
- The blocker itself is optional, and both lights default to `CastShadows=false`.
- A light casts only when **blocker enabled AND that light's cast switch enabled**.
- Each lit surface can independently set `ReceivesShadows`; its receiver light
  channels and surface shading mode are not changed by this flag.
- Ambient illumination is **never** shadowed in this lab.
- Shadow visibility is applied to **each source separately before summing**.
  Light 1's blocker cannot erase Light 2's unoccluded contribution.
- The final, global illumination buffer, selected-light view and shadow-mask
  view use the same GPU light visibility calculation. Debug views omit
  material-specific normals/rim highlights, as noted in the inspector.
- Unlit materials remain unlit regardless of shadow participation flags.
- All legacy Stage-3/4/5 lighting configuration is preserved when shadows
  are disabled; no new game content or menu routes are required.

## Interactive acceptance

1. Run `dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj`,
   then **SELECT LEVEL → LAB / LIGHTING BASICS**. CLI `--dev-lighting-basics`
   continues to work.
2. With shadows left **OFF**, verify the entire Stage-5 material lab is
   unchanged, including Flat/Normal/Rim/Hybrid, both colored lights, source
   alpha and selected receiver channels.
3. Open **SHADOWS** tab → **LOAD SHADOW COMPARISON**. A vertical rectangular
   blocker appears. The amber light on the RIGHT shadows Sample B
   (Light-1-only), and the blue light on the LEFT shadows Sample D
   (Light-2-only). Each projects its own hard shadow behind the rectangle.
4. Go to **LIGHTS** tab → **RENDER VIEW → SELECTED LIGHT**; select Light 1
   and Light 2 in turn. Confirm the *other* light contributes independently.
5. In the same view dropdown select **SHADOW MASK**. Black is occluded by
   the selected light, white is unobstructed. Toggle the selected light's
   **CASTS SHADOWS** in the Shadows tab and observe the mask become all white.
6. Change blocker X/Y and width/height in the **SHADOWS** tab, or hold SHIFT
   and drag on empty scene. Shadow direction/width should update continuously.
7. In **SURFACES** select one sample, then in **SHADOWS** toggle **RECEIVES
   SHADOWS**. The sample should still receive its assigned colored light,
   but stop losing light to the blocker. Background remains shadowed.
8. Place a light inside the rectangle: no explosive shadow triangles or
   invalid appearance; the blocker is skipped for that particular light.
9. Select **RESET SHADOWS ONLY**: this clears blocker and casting/receiving
   overrides without altering lights or material color settings. Full lab
   reset (`R` or Lights → Reset All) restores the Stage-5 initial state.
10. Repeat with only one light enabled and with zero ambient. White/unlit
    surfaces, transparency and contours should remain stable.

## Automated checks and limitations

`LBasicStage6HardShadowTests` covers the pure reference geometry, open segment
and grazing behavior, invalid coordinates, default backward compatibility,
per-light casting, independent sample receiving, and reset behavior.

**Still requires actual target validation:** build with MonoGame's DesktopGL
content pipeline, view the new GPU shader effect, visually compare final and
selected-light buffers, and profile GPU performance. Unit tests cannot prove
shader runtime correctness. The GPU uses pixel-space floating-point epsilon
while the reference uses normalized double coordinates, so a 1px boundary
variation is possible at silhouettes; full pixel-perfect parity is not claimed.

## Deferred TODO

- Multiple arbitrary static/dynamic polygons; concave and mobile blockers.
- Correct depth-aware caster/receiver planes and independent light height.
- Soft shadows, penumbrae, stencil/GPU extrusion backend comparison.
- Production LDtk integration, GPU screenshot and regression tests.
- HDR, emission/bloom; final full-frame post-processing, separately planned.
