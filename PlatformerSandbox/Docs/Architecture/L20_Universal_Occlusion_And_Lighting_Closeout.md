# Phase 20B–G — Universal production occlusion and lighting (implementation candidate)

## Baseline and installation

Apply to the uploaded Phase 20 Shadow Fidelity & Brightness baseline, **without** the earlier Phase16E polygon launch hotfix. This archive contains full replacement source files and new files only. The older `.fx` shaders, project files, existing binary art and existing tests remain installed. The old Phase 16E hotfix is superseded; its extra tests should not be installed. If it was installed by mistake, remove `PlatformerSandbox.Tests/Presentation/Lighting/L16EPolygonGpuCasterRegressionTests.cs` because it intentionally asserts the obsolete quadrilateral restriction.

The source bundle excluded existing `.fx`, project files and binary artwork. This candidate was written against the visible C# interface and adds a separate effect rather than recreating the original shader. No .NET SDK, MGFX compiler or GL test runtime was available here. Build and visual validation are required before closeout.

## Geometry and ownership

- Rectangle, triangle, trapezoid and pentagon remain convenience presets. The actual render geometry is a simple ordered polygon, with no shape-specific production shadow/rim branch.
- `ShadowShape = Custom`, `ShadowVertices = 0,0;1,0;1,1;0,1` author a rectangle. Values are normalized art-local X/Y coordinates (Y down), transformed into Y-up world coordinates using artwork dimensions, pivot, flip and rotation. Set `ShadowHullInsetPx = 0` for custom geometry. A concave U/arch is one outline. `0,0;.4,0;.4,1;0,1|.6,0;1,0;1,1;.6,1` creates two separated contours with a shared owner. Do not repeat the first point.
- Each contour has an ID; the shared owner is the artwork IID. Material self-exclusion excludes all of that owner's contours but does not exclude identical geometry owned by another object. Compound motion uses one shared pose and updates the artwork center once.
- Current explicit budgets: 3–64 vertices per contour, 128 total edges per artwork, 96 contours per scene. Simple concave outlines are supported; disjoint compound contours are supported. Self-intersections, degenerate/reversing edges, overlapping/touching or nested compound contours are rejected. Fully enclosed holes need a future polygon-with-holes model; no automatic sprite alpha tracing or arbitrary overlapping union is implemented.
- Directional projection uses cached ear-clipped solid triangles plus swept edge quads, in a bounded reach. This preserves legitimate concave openings when the selected direction/reach does not physically cover them. CPU directional sampling tests the same finite segment. Swept AABB culling keeps offscreen casters if their shadows can reach the camera. Static triangulation is cached by array identity/content; moving arrays are fresh. Point/spot visibility uses existing nearest-ray system; tube shadows still approximate the emitter midpoint.
- The lab has the new authoring fields on TerrainArtwork, ScenerySprite and LightBlocker definitions. Other LDtk project files are unchanged; use the schema migration tool to opt them in. Standalone LightBlocker custom contours are supported. Legacy default standalone rectangles retain original bounds behavior.

## Production rim implementation

The existing GPU material shader continues to evaluate front illumination, normal/edge maps, ambient, emission and post-processing. Production behind-light contributions now use a separate `L20_PolygonRim.fx` pixel effect. The C# adapter uploads ordered screen-space boundary segments and contour identity to a small floating-point texture (128x2), then evaluates nearest edge distance, facing, dark-corner veto, art alpha and per-light visibility inside the existing bounded artwork region. One upload is reused per material; an additional pass is required for each behind-light contribution. This is a variable-length edge-data shader, not a fully batched edge mesh. Its worst-case cost depends on shaded pixels and edge count. The 128-edge budget is synchronized between C# and shader.

Behind-light energy is omitted from the old front-face light slots. Per-light masks continue to exclude the correct owner and include unrelated casters. Additive material passes preserve the ambient pass alpha. A directional source marker is not used for point-overlap classification. Original Lighting Basics lab retains its previous shader behavior. Moving/rotating unmapped polygon artwork is supported; rotating normal/edge-map tangent transforms are still unsupported. The new effect requires DesktopGL shader compilation and float texture sampling verification. Do not treat Phase 19D visual parity as proven until tested locally.

## Brightness

Light Types Lab starts with its own neutral preset (ambient .23/.25/.29, unit light multiplier and art exposure), moderate sun/moon (.53/.45), point/spot/tube (.85/.90/.80), HDR on with .85 post-exposure and bloom/emission off. Global presets and Material Showcase fixture are unchanged. Production material front/rim gains now use authored intensity times the same art multiplier as world lighting, instead of pre-clamping effective gain to four. Existing legacy art exposure/post-processing and old shader internals are not rewritten; full GPU energy parity and display brightness must be validated visually. Multiple lights may intentionally accumulate above 1 under HDR.

## Validation

Run from the project root:

```
py Tools\phase20_universal_preflight.py
py Tools\phase20_preflight.py
py Tools\phase20_shadow_fidelity_preflight.py
dotnet build .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj
```

Open LIGHT TYPES LAB. Confirm the obelisk launches and casts its pointed bounded shadow. The seven new witnesses (triangle, irregular convex, 16-sided circle, concave L, arch, two-contour compound, moving/rotating concave) occupy a row above the original signs. The compound is translucent; convex/compound/dynamic samples have a depth-backlight rim. Cycle the five light types and ALL mode; compare hard/soft/contact modes, pause/resume/reload and bloom/emission toggles. Verify the green-rimmed pillar in LIGHTING_INTEGRATION_TEST and transparency/emission in MATERIAL_SHOWCASE_TEST. Check HUD lighting P95, material pass count, shade pixels, mask counts and GPU frame rate at normal and stress settings. If the new effect fails to build or lighting is unavailable, retain the full compiler diagnostics. Performance and GPU visual approval remain outstanding.
