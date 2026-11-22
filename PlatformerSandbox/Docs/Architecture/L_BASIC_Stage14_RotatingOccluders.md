# Lighting Basics — Stage 14: Rotating Occluders

## Purpose

Extend the verified Stage-13 translational motion laboratory with independent blocker rotation, without changing gameplay physics, LDtk importer, the shader, or the existing three-blocker GPU budget.

## One shape, one transform

- `BasicShadowShape` owns `RotationDegrees` and `ViewportAspectRatio`. `Vertices()` rotates the base convex polygon around its *bounding rectangle center* in screen-pixel coordinates. The previous zero-angle implementation is preserved exactly.
- The visible wall-face SpriteBatch draw uses the same center, angle, and pixel-scale. The GPU shadow pass and the back-rim pass both receive the shape's rotated vertices from the existing renderer; there is no additional shader path.
- Hit testing (`SelectShadowBlockerAt`), `Contains`, `Blocks`, light-inside-wall checks, wall rim polygon, and per-light visibility are all derived from that same rotating shape.
- Rotation of the visible face can yield tiny rasterization differences on silhouette edges; device screenshots remain required. At extreme angles near the viewport edges, rotated walls may clip outside the viewport. This is intentional in the isolated lab and does not change authoring bounds.

## Inspector

Open **Shadows → Rotate**. Select a blocker, choose `Static Angle`, `Clockwise`, `Counterclockwise` or `Swing / Oscillate`. Edit its angle, rotation speed (degrees per second) and swing range (degrees each side of its anchor). Use `Reset Selected Rotation` to restore just that blocker's rotation. `Load Rotating Shadow Demo` configures a static tilted rectangle, a clockwise triangle, and a swinging trapezoid; some also translate.

The shared `Pause All Motion` setting freezes both rotation and Stage-13 translation. Manual wall dragging reanchors translation without resetting rotation. All old shadow presets reset rotation to zero and remain regression fixtures.

## Determinism and boundaries

- Rotations are calculated from a fixed anchor and angular phase, never by accumulating polygon vertices.
- Angular speed is clamped 15–180 deg/s; oscillation sweep 5–90 degrees; angles are wrapped to [0, 360).
- Oscillation traverses 0 → +sweep → 0 → −sweep → 0 with constant magnitude angular speed and instantaneous endpoint reversal.
- Rotating the sprite and occlusion shape is only guaranteed for the lab's convex polygon kinds (rectangle, triangle, trapezoid). Concave/self-intersecting shapes and gameplay collision shapes are not in scope.
- Full-frame post-processing, HDR/bloom, soft shadows, and production LDtk integration remain separate TODOs.

## Acceptance

1. Compile solution and run the complete test suite, including `LBasicStage14RotatingBlockerTests`.
2. From the main menu, open `LAB / LIGHTING BASICS` → `Shadows` → `Rotate` → `LOAD ROTATING SHADOW DEMO`.
3. Verify that the visible face, projected shadow and directional rim follow rotation on the triangle and trapezoid, including on non-square window sizes.
4. Check that changing the wall angle changes polygon hit-testing and that selected-wall dragging does not snap its translational motion.
5. Verify the `Pause All Motion` control freezes both translations and rotations and that resume proceeds smoothly.
6. Return to One/Three/Poly and Stage-13 moving presets: none should unexpectedly rotate.
7. Exercise light overlap and Back/World/Front depth with a rotating wall; no backside-rim leaks or illuminated regions behind a different enabled blocker.

## Verification status

Static source checks and mathematical reference tests can be run in the authoring environment. Actual C# compilation, MonoGame shader loading, tests, and GPU visual acceptance must be performed on the user's Windows development machine when the .NET SDK is not available here.
