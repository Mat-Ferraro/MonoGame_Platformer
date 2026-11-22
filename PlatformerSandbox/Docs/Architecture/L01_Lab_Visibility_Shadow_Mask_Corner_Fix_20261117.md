# L01 — Shadow corner visibility mask correction

## Scope

Copy-over hotfix for the **L01 phases 1–3 lighting playground** only. No changes to simulation, input adapters, character animation, cameras, existing audio or particle systems, LDtk levels, or normal-game rendering.

## Cause

The first prototype darkened each light target by extruding black quads from several polygon edges. For concave blockers and lights dragged near corners, overlapping wedges could erase the illuminated side of a light, leave small discontinuities between the corner wedges, or give the impression that a movable light only illuminated the forward-shaded material panels.

The panels already use an independent CPU material-preview evaluator and opt out of back-wall occlusion. Thus a GPU backdrop shadow error and correctly lit panels could coexist.

## Correction

- **Visibility polygon**: `ShadowVisibility.BuildPolygon` casts bounded, angle-sorted rays through each relevant occluder corner (with a small pair of offset rays) and uniformly across the light circle. The nearest segment intersection provides the visible distance. This creates one continuous visibility region for convex and concave shapes, including moved light positions.
- **Per-light composition**: The lab renders that region as a white visibility mask, then multiplies **only the corresponding light target** by it. Ambient and unrelated lights are not erased by another light's shadow.
- **Preserved render target contents**: The individual light target uses `RenderTargetUsage.PreserveContents` because the mask is generated in a separate target before the multiply pass resumes.
- **Shadow debugging**: `5` toggles whether the selected light casts shadows; `6` temporarily isolates the selected light. The debug HUD identifies which light is selected and whether its shadow is enabled.
- **Bounded geometry**: 192 uniformly distributed rays with up to 768 extra angular samples near occluder vertices; nearest-hit segment intersection and AABB radius screening.
- **Material layering unchanged**: The previous hotfix's lit occluder fronts and shadow-independent foreground material panels remain in place.

## Build and test

From the project root:

```powershell
dotnet build --configuration Debug -m:1
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~L123VisibilityShadowMaskRegressionTests
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj

dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj -- --dev-lights
```

## GPU validation steps

1. Press `F2` to open **ATRIUM** and `Tab` to select `gold_sun` (if needed). Press `6` for **SOLO YES**, leaving its shadow **YES**. Drag the warm light around the L-shaped alcove and column. The floor/backdrop should illuminate on the light-facing side, and the shadow should follow the corner without stray triangular notches.
2. With the light still isolated, press `5` to turn its shadow **NO**. The background inside previously shadowed sectors should light normally. Press `5` again to restore shadows.
3. Select `teal_magic` with `Tab`; repeat the drag test. It should also light and shadow the backdrop (provided the light is visible and within radius).
4. Press `6` to restore both lights. Overlapping lights must illuminate independently: shadow from the gold light must not erase teal or the scene ambient.
5. Press `4` to cycle raw scene, light buffer, and combined composition. Check that the light buffer shows light contributions extending beyond the textured material panels.
6. Check `F1` CAVERN, camera pan/zoom, normal toggle, foreground panels, and existing particle playground/ordinary game regression.

## Known scope limits

- Hard shadow edges remain pixel-aliased at native resolution. Anti-aliased/soft shadows are later quality enhancements.
- Occluding wall fronts are a laboratory forward-shaded proxy. The lab does not implement arbitrary scene-wide 2.5D occluder/receiver depth ordering.
- The material panels intentionally opt out of shadows from the backdrop occluders. Production LDtk layer routing and actual particle-light attachment remain in later L01 phases.
- If a point light is placed *inside or directly on* a wall polygon, that particular wall is skipped for its visibility mask as a bounded debug fallback.
- The previous `ShadowGeometry.Project` API is retained for existing headless tests and backward compatibility, but is no longer used to paint the lab's light mask.
