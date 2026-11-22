# L_BASIC Stage 8 — Convex polygon shadows and a clearer lighting lab

## Scope

An **isolated, presentation-only** extension of the working Stage 7 Lighting Basics lab. It **does not** modify gameplay simulation, LDtk content, regular gameplay rendering, physics, post-processing, or the existing lighting-material workbench.

Stage 8 adds:

- Convex `Rectangle`, `Triangle`, and `Trapezoid` shadow silhouettes. Their editable position and size remain normalized rectangles; the GPU receives real convex vertices derived from them.
- A limited **Add Wall / Remove** workflow using three existing shader slots. A fourth caster is rejected by the headless state API; the inspector shows the active count against the 3-slot GPU budget. Removing a wall frees a slot for reuse.
- Mouse selection of visible silhouettes by **clicking the actual shape**, not the empty portions of its bounding rectangle. Hold the mouse and drag to move. Shift-drag from empty background moves the currently selected blocker as before. Clicking an empty area without Shift moves the selected light.
- Two pages inside the Shadows inspector: **Blockers** (geometry, selection, editing, presets) and **Light Rules** (selected light casting and selected material receiving). Previous Lights and Surfaces tabs remain.
- Four compact shadow presets: **ONE**, **THREE**, **POLY**, **CLEAR**. The first two reproduce Stage 6 and 7; POLY enables a rectangle, triangle, and trapezoid; CLEAR restores only shadow settings.
- Neutral-gray polygon faces with **no painted light strip** or seam. Selection outlines and numbers remain UI overlays, not emitted/rim illumination.
- In-world labels differentiating the four **Material Samples A–D** from the **Shadow Blockers**.

## GPU implementation

`L_BASIC_SurfaceLighting.fx` now accepts up to three convex silhouettes. Each silhouette is represented as up to four ordered vertices (2 float4 pairs) and one vertex count. Hard-shadow visibility clips the *open* light-to-pixel segment against inward polygon half-planes; shadow visibility is evaluated **per light** and combined per receiver. Ambient is unchanged. The existing single-/multi-rectangle renderer APIs remain available via adapters to this polygon representation.

The lab's silhouette textures are generated deterministically in C# from the same polygon definition. There are no imported normal maps, prepainted highlights, or arbitrary geometry dependencies.

## How to launch

1. Extract the patch over the Stage 7 installation, preserving paths and overwriting existing replacements.
2. `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj`
3. `dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj`
4. Select **LAB / LIGHTING BASICS** from the in-game level select. The old `--dev-lighting-basics` flag remains supported.
5. Open **Shadows > Blockers**. Choose **ONE**, **THREE**, or **POLY**, or use **ADD WALL** repeatedly.
6. Change SHAPE to TRIANGLE or TRAPEZOID, then move it by direct mouse drag or position/size sliders. Toggle enabled or REMOVE and watch other silhouettes retain their own shadows.
7. Switch to **Shadows > Light Rules** to test per-light shadow casting and per-sample receiving. The active light and sample are selected in Lights and Surfaces, respectively.
8. Compare FINAL IMAGE, SHADOW MASK, and SELECTED LIGHT. The latter two must use the **same polygon visibility path** as final lighting.

## Visual acceptance

- ONE reproduces Stage 6; THREE reproduces Stage 7; POLY shows a neutral rectangle, triangle, and trapezoid, each with a matching hard-shadow silhouette.
- Dragging the triangle or trapezoid updates silhouette and shadow together, without a temporary rectangular shadow.
- Dragging near a triangle's transparent bounding-box corner does not select that triangle.
- Disabling one wall preserves the other shadows; disabling one light's shadows does not block illumination from the other light or ambient light.
- The pre-painted silver left-edge strip is gone. A bright **green edge** may still appear as the selected-wall UI outline; it is *not* a rim-light effect.
- The material-sample squares remain separate from the walls, and unlit Sample C stays unchanged.
- Click selection and dragging cannot steal gestures from inspector dropdowns, sliders or toggles.
- `dotnet test` passes and the compiled DesktopGL material effect has no shader-semantic errors.

## Explicit limitations

- **Convex only, fixed templates**; no hand-edited vertex coordinates, concave polygons, holes, soft shadows, or artist-authored outlines in this phase.
- **Three simultaneous occluders**: inherited bounded GPU budget, not unlimited creation. The lab is not intended as a production light-count benchmark.
- Light/receiver inside the same occluder are treated as unshadowed in the flat 2D model. Depth-aware self-shadowing comes later.
- Shadows are hard edged. Shadow CPU model and shader coordinate conventions must still be visually verified on Windows DesktopGL.
- No renderer-performance claims until a device test. No build or GPU execution was possible in the patch authoring environment.
- Full-frame post-processing remains a separate TODO.
