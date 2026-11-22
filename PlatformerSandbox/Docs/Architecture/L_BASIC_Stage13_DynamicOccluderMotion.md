# L-BASIC Stage 13 — Dynamic Occluder Motion

## Scope

Extend the **existing** headless Lighting Basics state and GPU compositor with
presentation-only, per-blocker, time-based translation. Preserve Stage 12
polygon-edge, independent per-light shadow visibility, and depth-aware rim rules.
Do not change the production LDtk renderer, collision solver, or shader in this
stage. Post-processing, soft shadows, rotation and extra GPU blocker capacity
remain deferred.

## Geometry ownership

Each of the same three authoring slots stores a motion mode, an anchor/home
rectangle, a phase `[0,1)`, travel and speed. `BasicShadowShape` remains the
canonical silhouette for both albedo and GPU shadow/rim data. The existing
`ActiveShadowShapes` list is regenerated from current positions each draw,
and compact GPU shadow-slot identity is preserved.

### Travel model

Static, horizontal ping-pong, and vertical ping-pong are available. Speed is
**complete return trips per second**, not linear speed in pixels per second;
travel is a normalized viewport distance. Position is computed from a
triangle wave (`0→1→0`) relative to an anchor rather than incremented each
frame. Elapsed presentation time is passed from `Game1.Update`. A paused
scene calls `BasicLightingLab.Update(... interactive:false)` and does not
advance the motion clock. Explicit inspector pause freezes all slots. Dragging
re-anchors the selected wall, preventing a snap to an old path after release.

Travel is constrained inside the viewport by the existing silhouette bounds.
Near a boundary the preferred direction reverses inward. The movement is
purely cosmetic; it does not query or mutate simulation state.

## Inspector

Shadows has three subpages: **Blockers**, **Motion**, **Light Rules**.
The Motion page exposes selected slot, pattern, speed, distance, global
pause/resume, selected stop and a three-shape moving demo. Old geometry
and light-rule controls remain accessible. All blockers default to Static.

## Regression obligations

- Original one/three/polygon/depth/rim presets do not move unless explicitly
  configured, and reloading a preset clears old motion configuration.
- One moving blocker cannot inadvertently move a different or inactive slot.
- Rendered blocker silhouette and GPU shadow/rim geometry use the same
  current `GetShadowShape` value each frame; self-shadow exclusion continues
  to use compact active index, not world/authoring slot or shape equality.
- Light-in-wall and lower-depth light rules continue to be evaluated for each
  light/blocker pair using its **current** position.
- Main-menu level selector and `--dev-lighting-basics` launch both provide a
  frame delta; pause/transition controls must not advance visual motion.

## Build / acceptance

```powershell

dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj

dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj
```

Select **LAB / LIGHTING BASICS → Shadows → Motion → LOAD MOVING SHADOW DEMO**.
Observe triangles/trapezoids, pause/resume, direct-drag re-anchor, depth-based
back-rim and per-light occlusion. Headless motion regression tests do not
validate GPU pixel output; capture baseline screenshots at known positions.
