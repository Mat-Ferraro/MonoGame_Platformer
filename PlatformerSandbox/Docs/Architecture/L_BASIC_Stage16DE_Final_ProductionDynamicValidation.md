# Lighting Phase 16D–16E: Production Dynamic Occluders + Validation

## Scope / prerequisite

This package **extends**, rather than replaces, the already installed 16A/16B/16C optimizations. Install it on a working 16C tree and keep Stage 15D's DEV TOOLS UI. It does not change the GPU lighting shader, expand the two-light-per-material budget, or create physical moving walls.

### Stage 16D: production visuals & dynamic shadows

- `PresentationOccluderMotion` is a pure Foundation contract. It derives a world-space displacement and screen-clockwise angle from an immutable authored anchor and a bounded presentation clock.
- LDtk `TerrainArtwork` and `LightBlocker` may supply independent translation/rotation settings; old content has no motion by default.
- The gameplay presenter creates a frame-consistent `LdtkLightingScene` **only if at least one motion exists**, leaving the imported geometry intact. Static hull objects are reused by reference; only animated hull vertices are transformed.
- The visual draw path receives exactly that scene snapshot. Artwork position/rotation is updated with the same motion pose and clock as its shadow hull. Lighting sampling, shadow fan generation, broad-phase pruning, material visibility masks, and self-shadow exclusions all see the **same current occluders**.
- The `CurrentScene` cache prevents repeated hull transforms across the layered material/geometry draws in a single frame. The independent wall animation clock freezes on gameplay pause or the visible DEV TOOLS **MOVING WALLS** toggle and resets when a scene is activated. Artwork continues to animate when the **LIGHTING** toggle is off; turning lights back on uses the same current-frame geometry. Authored moving lights remain independent.
- Animated scenery remains cosmetic and does not create/change authoritative simulation colliders, contact edges, or movement trajectories. Do not use this for platforms or doors that must physically block the player; those require world-owned motion and an explicit graphics adapter.
- Dynamic mapped tangent normals/edge maps are rejected for now rather than drawn with incorrect rotated normals. Legacy and Flat material modes are supported. Animated art with non-world parallax is rejected to prevent shadow/artwork divergence.

### Stage 16D demonstration in `LIGHTING_INTEGRATION_TEST`

The existing crate at approximately X=1264 translates horizontally and swings. A new triangular panel at X=1650 translates vertically and swings; a new trapezoid at X=2025 rotates continuously. They retain their own IID, artwork, shadow silhouette, and depth layer. The light sources, original normal-mapped panel, green-rim hybrid pillar, moving platform, and existing level/collision assets remain intact.

LDtk authored optional fields on `TerrainArtwork` and `LightBlocker`:

| Field | Allowed / meaning | Default |
|---|---|---|
| `BlockerMotionAxis` | `Static`, `Horizontal`, `Vertical` | Static |
| `BlockerTravelWorld` | Max displacement in world units, 0–10 | 0 |
| `BlockerCyclesPerSecond` | 0–2, required for translation / swing | 0 |
| `BlockerRotationMode` | `Static`, `Clockwise`, `Counterclockwise`, `Swing` | Static |
| `BlockerAngularSpeedDegPerSecond` | 0–360, required for continuous rotation | 0 |
| `BlockerSwingDegrees` | 0–180, required for swing | 0 |
| `ShadowShape` | `Rectangle`, `Triangle`, `Trapezoid` (TerrainArtwork) | Rectangle |

All geometry is computed from the original authoring anchor and current presentation time, not integrated from last frame, avoiding drift. `ShadowShape` only affects the shadow hull; PNG transparency should still match the selected shape. `ShadowHullInsetPx` still works. The `LightBlocker` entity has no visible art; animated art/hull synchronization is supported only for `TerrainArtwork` with world parallax.

## Stage 16E: acceptance and performance comparison

Run `dotnet build .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj` and `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj` in the project root.

In **LIGHTING_INTEGRATION_TEST**, open **DEV TOOLS**, set `STRESS LIGHTS: OFF`, `MOVING WALLS: PLAY`, `QUALITY: BALANCED`, and enable `HUD: FULL`. Walk rightward to the existing blue/pink lights and the new animated casters. Inspect the three material/shadow paths. The old moving platform and both original GPU material samples must still work.

Observe that:

1. The crate's painted rectangle translates/tilts together with its moving shadow. Pause walls in DEV TOOLS, then resume; neither graphic nor shadow should jump.
2. The triangle's narrow point and sloped edges stay aligned with its cast shadow during vertical swing. The trapezoid's edge lighting/shadow changes continuously under rotation.
3. As any caster enters/leaves a light radius, the shadow disappears/returns without staleness. The `L16A PRUNED` counter can change, but never prune a visible intersecting caster.
4. The full HUD displays `L16D DYNAMIC HULLS 3` and `STATIC REUSED [positive]`; reset/re-enter restores authored angles and positions.
5. Occlusion, behind-wall rim/shadow behavior, self-shadow exclusion, and the normal/hybrid material samples are unchanged. `MAT 2` is expected; new legacy animated art does **not** consume extra GPU material slots.
6. Run **LAB / LIGHTING BASICS** and **LIGHTING_FLAT_TEST** for regression; dynamic motion is opt-in and those old fixtures remain unchanged.

### Repeatable baseline comparison (no invented measurements)

1. Leave resolution and camera area fixed; turn `STRESS LIGHTS: OFF`. Warm up at least 120 frames, then observe or log another 300 frames.
2. Record lighting CPU `LAST`, `AVG`, `P95`, `MAT-GEO`, `LEG-GEO`, `MAT-VIS`, `L16A PRUNED/MASKS SKIPPED`, `L16B NEUTRAL MAPS/WHITE MASKS`, `L16C SHADED/AVOIDED PX`, `RT-RESIZE`, `L16D DYNAMIC/STATIC`.
3. Compare **MOVING WALLS: PAUSE** to **PLAY** (not a GPU timing benchmark; it isolates animation work). Repeat for LOW/BALANCED/HIGH quality.
4. Repeat with `STRESS LIGHTS: ON`; compare quality/budget behavior. Stress is for load testing, not appearance validation.
5. Ensure no steadily growing target allocations at fixed window size, no crashes, no missing or detached shadows, and stable bounded CPU timing rather than a numerical threshold invented without target hardware.
6. If possible, capture before/after Phase 16C and Phase 16DE on the same device, scene, resolution, camera, and warmup. Use identical scripted capture methodology.

### Remaining architectural boundaries (deliberate, not silent omissions)

- This package cannot claim improved FPS or GPU time: the current counters are mostly CPU-side and no desktop GPU benchmark was run here.
- Dynamic shadow-casting **gameplay-solid** moving platforms/doors are not included because they require a world-bound collider transform and explicit ownership. The demonstration is cosmetic scenery, not physics.
- Production rotating **Normal/Hybrid** surface maps remain unsupported until tangent-space maps are transformed consistently; same applies to more complex concave free-form shapes beyond the current authoring set.
- The production material pass retains its two-light-per-surface selection limit; that belongs to the multi-light accumulation phase.
- Full soft-shadow, HDR, bloom, and full-frame post-processing are later phases.

**Exit criterion:** compilation/tests pass on Windows, the three dynamic visual/hull pairs visually track throughout their cycles, no Phase 16A–C regression appears, and a recorded Stage 16E baseline is available. Until those are done, call Phase 16 implementation-complete but not GPU-performance-validated.
