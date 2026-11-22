# L_BASIC Stage 9 — Light origin overlapping a shadow wall

## Chosen artistic policy

When the center of a light falls **inside a wall's actual convex silhouette**, that
wall is exempt from casting its own geometric shadow for that *one light*. Two
configurable placements then determine the **same wall's visible-face response**:

| Light placement | This light illuminates that wall face | This light draws wall rim | That wall casts its shadow from this light |
|---|---|---|---|
| Front of wall | Yes | No special back rim | No |
| Behind wall + rim | No | Yes, narrow light-colored inner silhouette rim | No |

- **Other lights** keep their own independent contributions and casting rules.
- **Other walls** continue to cast shadows from this light normally.
- The wall still receives **ambient** illumination, even in the Behind mode.
- Mere **radius** overlap without origin containment is not special: shadows
  still work as before.
- This is an explicitly art-directed 2.5D placement convention, *not* a physical
  Z-buffer/depth solution. It changes no gameplay physics or visibility rules.

## Integration

`BasicLightingPreviewState` retains the selected light's `WallOverlapMode`, with
`FrontOfWall` as default. `GetWallLightOverlap(shape)` resolves each light's
origin against the wall's actual silhouette, independently of GPU shadow masks.
The lab passes this pair-specific override only when drawing that wall face.

`GameplayLightingRenderer.DrawBasicMaterial` now accepts an optional
`BasicWallLightOverlap`. All old call sites use the previous default (no mask).
An explicit behind-wall override fails fast if a caller attempts to render
without the material shader. `L_BASIC_SurfaceLighting.fx` suppresses only the behind-overlapping light's
front-face diffuse/regular rim contribution, and replaces it with a small
light-colored inner edge highlight from sampled silhouette alpha. Its intensity
follows that light's radius, color, and intensity. No extra art textures or
new shader semantics are needed. The rim is deliberately **inside** the wall;
outer bloom/halo remains a later full-frame post-processing TODO.

The existing GPU polygon-visibility code already exempts the containing wall
from blocking that light; it still evaluates all other blockers normally. We
intentionally do **not** change the shadow-ray algorithm for this stage.

## In-game demo

Main menu -> Select Level -> LAB / LIGHTING BASICS.
Open inspector -> Shadows -> LIGHT RULES.

- Select Light 1 or Light 2 via the inspector's LIGHTS tab.
- Select `FRONT OF WALL` or `BEHIND WALL + RIM` from `LIGHT ORIGIN INSIDE WALL`.
- Click `LOAD FRONT / BEHIND DEMO` for an amber light front-mounted on the
  left wall and a blue back-rim light behind the right wall. A third middle wall
  remains an ordinary occluder.
- Drag a light's center outside its wall and verify ordinary shadows resume.
- Change the selected light's color, intensity, radius and shadow toggle to
  check independent contributions. Try disabling one light and changing the
  second wall to a triangle; it should still react to the true silhouette.
- Switch to the ALBEDO view: the bright blue outline must disappear, because
  it is created by the lighting shader, not baked into the wall texture.

## Visual acceptance / tests

1. Original one/three/polygon presets unchanged and default to Front mode.
2. Front-mounted amber light illuminates the left wall, with no self-shadow.
3. Behind blue light leaves the right wall's front face ambient-only (plus
   contributions from any other eligible lights), with a narrow blue edge.
4. The neighboring wall still occludes the same light correctly.
5. Switching just one light's overlap mode does not alter the other light.
6. The edge highlight follows color/intensity/radius and disappears when its
   light is disabled or leaves the wall silhouette.
7. Flat, Normal, Rim and Hybrid material samples continue to work. The back
   rim works on flat walls and is independent of the surface `RimStrength`.
8. Run `dotnet test .\\PlatformerSandbox.Tests\\PlatformerSandbox.Tests.csproj` and visually
   inspect on DesktopGL. Headless tests cover the policy but **not GPU pixels**.

## Known limitations

- Art-directed overlap placement is per light, not an actual modeled Z plane.
- Only three authored convex blocker shapes exist in this bounded lab.
- The inner rim is a hard ~3.5-screen-pixel contour sampled from a wall silhouette;
  it has no blur, directional penumbra, volumetric transfer, or outward bloom.
- Debug light-buffer / shadow-mask views inspect raw screen-space light
  visibility, not wall-specific material response. The **Final** view is the
  authoritative preview for this feature.
- Production gameplay integration, HDR, and final-frame post-processing remain
  on the deferred roadmap.
