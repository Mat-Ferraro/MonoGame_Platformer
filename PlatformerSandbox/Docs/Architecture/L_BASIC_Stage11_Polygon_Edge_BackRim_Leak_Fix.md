# Lighting Basics — Stage 11: correct polygon back-rims and eliminate shadow-side leakage

## Visual problem

Stage 10 estimated back-rim silhouette normals by sampling texture alpha at
horizontal and vertical offsets of a fixed number of **viewport** pixels. On
sloped triangles and trapezoids, this gave unevenly thin/broken rims, and the
inside-light path also applied a hardcoded `0.2` brightness floor to edges
facing away from a light. The visible broad amber/blue wedges in some scene
screenshots are **projected hard-shadow areas**, not the rim itself.

## Fix

- On each wall draw, bind the wall's **actual convex polygon vertices** in
  screen-pixel coordinates using `WallRimPolygonAB`, `WallRimPolygonCD`, and
  `WallRimPolygonCount`. Bind zeros on non-wall draws, so materials do not
  accidentally reuse the preceding wall's shape.
- Measure the shortest Euclidean distance from the receiver pixel to each
  segment. Use the **nearest segment of the complete polygon**, not the nearest
  eligible/illuminated segment: this prevents light from a nearby adjacent edge
  from wrapping around a corner onto a shadow-facing edge.
- Use `(edge.dy, -edge.dx)` as the outward normal for the existing positive
  cross-product winding in screen coordinates, then test the light direction
  against that normal. The facing response is zero below a small threshold;
  there is **no shadow-side minimum illumination**.
- Rim band width is fixed in **screen pixels**, perpendicular to each edge,
  so diagonal and axis-aligned contours receive comparable widths.
- When a light is inside a wall, the virtual direction comes from the polygon
  vertex centroid to the light. Exactly centered light origins retain a
  symmetric rim because there is no defined preferred direction.
- Preserve the Stage 9/10 per-light front/behind and virtual depth rules,
  object receiver behavior, materials, and existing one/three/polygon shadow
  presets. No change to the projected shadow visibility algorithm.
- The **Load Depth Rim Demo** preset now uses one triangle and one trapezoid
  and temporarily disables both lights' projected shadow casting, solely to
  make the true back-rim visible without the large hard-shadow wedges. The
  Front/Behind comparison and other presets remain unchanged.

## Run and inspect

```
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj
```

Select **LAB / LIGHTING BASICS** from the game's main menu. Open **Shadows >
Light Rules** and click **LOAD DEPTH RIM DEMO**. Keep **SHOW WALL DEBUG
OUTLINES** off. Move the blue light horizontally between the triangle and the
trapezoid: its rim should track the contour that faces the light and vanish
on the opposite side, with comparable rim thickness on slopes and vertical
edges. Turn off the amber light or set its intensity to zero to isolate the
blue rim contribution. To test ordinary cast-shadow wedges, turn cast shadows
back on in the Light Rules tab or load ONE / THREE / POLY presets.

## Acceptance checklist

1. A diagonal triangle/trapezoid edge shows a continuous, approximately
   constant-width highlight (allowing pixel-art step aliasing).
2. Opposite, shadow-facing edges receive **zero** rim from the selected backlight.
3. A pixel near a corner but nearer the wrong-facing edge does not inherit
   an eligible edge's rim.
4. The rim follows the light as it passes around the wall; on one side it
   disappears while the opposite side becomes eligible.
5. The light cannot brighten the face when behind the wall; ambient and
   other lights still work independently.
6. Centered internal backlights retain a symmetric ring. Off-center internal
   backlights show only the intended directional side.
7. Stage 6–10 presets, shadows, materials, and level-select routing remain usable.

## Constraints

- The fixed four-vertex polygon rim is a laboratory feature for rectangles,
  triangles, and trapezoids. General/concave polygons require a different
  geometry contract.
- No bloom, soft shadows, subpixel anti-aliasing, or full depth-aware scene
  compositing has been added.
- Headless tests exercise a mathematical reference implementation; GPU shader
  compilation, blending correctness, and visual appearance still require
  validation on DesktopGL.
