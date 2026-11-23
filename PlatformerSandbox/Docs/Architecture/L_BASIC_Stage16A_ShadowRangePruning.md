# Stage 16A — Conservative shadow-caster range pruning

**Baseline:** Stage 15D UI/lighting cleanup and its compile/test corrections, following Stage 15C CPU instrumentation.

## Why

Production lights currently build full radial visibility masks even when all routed shadow blockers are far outside the light's influence. The exact visibility builder already rejects out-of-range AABBs internally, but the renderer still incurs polygon creation, GPU draw calls, and full-screen render-target work. Stage 16A adds an earlier **world-space broad phase** to skip that work entirely when appropriate.

## Contract

- Preserve authored light/occluder layer routing and exact self-caster-ID exclusion. Those decisions happen **before** range pruning.
- Prune only when the polygon's **entire axis-aligned bounding box** is outside the visibility disk. Do **not** use polygon center or vertex-distance tests: long edges can cross the disk with both endpoints outside.
- Use **1.021 × effective light radius**, conservatively larger than the exact fan's 1.02 × radius extension. Apply any art radius scaling **first**.
- Never cache geometry across frames: moving, rotating, or dynamically authored occluders are tested from current vertices each frame.
- Keep using `ShadowVisibility.BuildPolygon` unchanged for all retained candidates; the same hard-shadow geometry is computed as before.
- Preserve error behavior: invalid occluders are still validated and throw, including ones that would otherwise be discarded.
- When no routed blocker can reach a material light, clear its visibility mask to **white** (fully visible). When no routed blocker can reach a layered light, skip multiplying its illumination by a visibility mask.

## New F11 full-HUD counters

`L16A SHADOW BROADPHASE: PRUNED N MASKS SKIPPED M`

- `PRUNED` is the number of routed caster candidates discarded by the conservative AABB/disk test across legacy and production-material paths, *per frame*. A caster can be counted more than once when considered by different lights/surfaces.
- `MASKS SKIPPED` is the number of otherwise eligible per-light masks not built because **all** routed blockers were out of range. It does not include masks bypassed by authored shadow rules or disabled shadow lights.

## Windows acceptance

1. Verify `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj` and DesktopGL build.
2. Open `LIGHTING_INTEGRATION_TEST`; use **DEV TOOLS** buttons to select Balanced quality, toggle stress, and enable the full HUD. No hidden key combinations are required.
3. Capture CPU AVG/P95, MAT-GEO, LEG-GEO, MAT-VIS, LEG-VIS, `PRUNED`, `MASKS SKIPPED` with the camera held in the same position for ~120 rendered frames.
4. Compare with a Stage 15D build on the same machine/settings. Reduction is workload-dependent; **no speedup is claimed without measured timings**.
5. Move the red/blue lights and the camera, exercise the moving and rotating blockers in `LAB / LIGHTING BASICS`, and verify exact shadow/rim silhouettes and no new leaks. The isolated lab remains unchanged.
6. Check `LIGHTING_FLAT_TEST` and behind-wall green rim at the integration pillar, including another blocker cutting the rim. Zero blockers within range should not cause a shadow.
7. Inspect quality Low: no shadow fans are built when shadow budget is zero; new counters should not claim work was skipped for unselected masks.

## Remaining optimization scope

This stage intentionally **does not** cache masks, batch material surfaces, change shader budgets, introduce GPU-timer claims, or implement soft shadows. After the measurements, the next likely step is reducing per-surface full-frame material map passes or safely reusing identical per-light masks when their exact geometry and self-caster rules match.
