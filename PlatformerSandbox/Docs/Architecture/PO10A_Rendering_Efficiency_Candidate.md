# PO10A — Rendering efficiency candidate

## Baseline and scope

Apply after PO01–PO04, the PO04B candidate, and its automated runner retry fix. This package changes only the listed files. No shader, collision, world-simulation, authored-level, menu or asset changes. The 48-run personal-laptop PO04B benchmark measured approximately 24.82 FPS / 40.30 ms at 720p High, 12.83 FPS at 1080p High, and 29 mask capacity fallbacks at 720p. Those are historical measurements, not results for this patch.

## Implementation and limitations

- PO10A-01/02: independent 100/75/50/25% illumination and shadow-mask target dimensions for production layered rendering. Full-size artwork, normal/edge maps, material composition, HDR final canvas, and HUD. The legacy/basic paths remain full resolution. Projection keeps logical full-screen coordinates; point-light sprite positions are scaled at submission, visibility geometry rasterizes through the logical projection to the actual target viewport. Reduced-resolution masks are experimental quality settings. Gaussian UV steps remain logical-screen based; no shader source was available to confirm all sampling assumptions. Contact-hardening with nonzero softness falls back to full-size lighting and masks. No reduced-resolution setting is enabled by default.
- PO10A-03: padded, scissored scratch material masks (hard and uniform-soft); only on non-projected front-facing receivers. Full-domain masks are still required for shared cache entries, folded/other active receivers, contact-hardening and back rims. Regional masks are excluded from the hard-slot reuse key. Soft filter scissor covers the padded region for both axes; the material reads only the original region. Regional clear uses an opaque solid quad, not GraphicsDevice.Clear with scissor. Needs live visual testing.
- PO10A-04: point-light (including expanded area samples) bounded clear, final mask multiplication and light accumulation. Full-domain shadow builds and the first blur axis for layer masks remain full-domain for sharing. Directional/spot light fields remain full-domain. No light/shadow sample counts changed. The region is conservative, padded and clipped to the actual illumination target; full logical UVs retained for visibility multiplication.
- PO10A-05: retain at most three compatible illumination fields within one frame (existing target plus two extra, 16 MiB additional cap). Exact matching of viewport, clamped ambient, art, budget, sample count, immutable input list identity, per-light routing and shadow reception. Point/tube paths only. No cross-frame field reuse or different-ambient field sharing: this deliberately avoids assuming additive clamping/rounding equivalence across HDR/SDR. Recycle the oldest spare if necessary; no texture copies. Reset keys on frame/resize/reset/abort; dispose owned resources on rebuild/disposal.
- PO10A-06: optional redundant full-layer clear removal before opaque full-target albedo copy, plus regional clear/composite above. Material ambient clears retained (shader discard behavior unknown). No new consolidated composition shaders or wholesale render-target switch rewrite.
- PO10A-07: retain 128 MiB shared RGBA8 mask budget, count actual allocated dimensions. Optional maximum 96 targets instead of 48; at 720p full size the byte budget still limits to 36, at 50% up to 96 instead of 48. No current-frame mask eviction or aliasing of a pair's active textures. Field pool has its own separately capped budget, shown in total target estimate. Existing exact key matching retained. No new atlas or texture format.
- PO10A-08: actual light/mask sizes, selected clear pixels, light composite pixels, mask raster-bound pixels, regional mask builds, compatible field hits, existing filter pixels and actual-size target memory estimate. Counters are workload estimates, not GPU execution timestamps or exact shaded fragment counts. Selected clear counts cover PO10A paths only. Mask raster-bound pixels represent bounding rectangle area rather than triangle coverage. Soft filter counters now use destination dimensions and scissor area. The prior High lab uses Hard by default; explicit soft cases are included.
- PO10A-09: 26-case benchmark plan with reference, independently scaled light/mask targets, five optimization switches, cache capacity at half masks, combined 100/75/50/25%, dynamic lights, paired 1080p, explicit Soft8Px, and lighting disabled. All cases retain PO04B switches and original mask 127 unless specified by the existing isolation. Metadata and actual target dimensions are checked. Focus retry logic retained. Three repetitions recommended; alternate forward/reverse order. Existing suites retained.
- PO10A-10: headless pure policy regression tests and Python runner tests. Windows rendering/visual acceptance pending. No claims of measured performance gains, true GPU timestamps, pixel parity, or compiled C#.

## Install / run

Extract into solution root over the PO04B + retry baseline. Run:

```powershell
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj -c Release
py .\Tools\po_run_benchmarks.py --po10a --smoke
py .\Tools\po_run_benchmarks.py --po10a --repeat 3 --no-build
```

Smoke runs reference, combined 50% and soft combined 50%. The runner builds Release unless --no-build is supplied. Omit --no-build if code changed after smoke. Use --no-wide if display cannot accommodate 1080p; skips two configurations. Upload one results ZIP from Benchmarks. Keep focus, power connected, and power mode consistent; do not touch the game during capture.

## Manual visual acceptance

Dot-source the profile script and launch the game from the same PowerShell session. For strict full-resolution reference use:

```powershell
. .\Tools\po_profile.ps1 -Profile Candidate -Renderer Reference -LightingProfile OptimizedCandidate
```

Full-resolution optimized candidate, then separate lower-resolution experiment:

```powershell
. .\Tools\po_profile.ps1 -Profile Candidate -Renderer Candidate -LightingProfile OptimizedCandidate -LightScale 100 -MaskScale 100
. .\Tools\po_profile.ps1 -Profile Candidate -Renderer Candidate -LightingProfile OptimizedCandidate -LightScale 50 -MaskScale 50
```

Restart the game between each profile. Keep the same quality, camera, light motion and player pose for comparison. The production mask 127 is explicitly opt-in and not assumed validated. The performance lab's original flags can be toggled as usual. Compare:

1. Engine Performance Lab: hard shadows, uniform Gaussian Soft8Px, stationary and dynamic lights, camera motion, material edges, contact-hardening (full-size fallback), and 720p/1080p resize.
2. Light Types Showcase: spot/directional/point/tube, folded/flat/clipped receivers, projection alignment, moving and rotating casters.
3. Material Showcase: transparent/glass surfaces, normals, front/back rims, emissive materials, HDR/bloom and clipping near viewport edges.
4. Connected Cavern: parallax, moving player, room transitions, reset and resize. Return to Lighting Basics to confirm legacy target sizing.

At 100% require no visible regression relative to the reference. At reduced resolutions compare intentional shadow edge detail vs performance, check for stale masks, seams, missing or displaced lighting, incorrect tint/alpha or flicker. Preserve reference mode if any mismatch. A lower-resolution improvement is a quality/performance tradeoff; it is not a proof of identical pixels. Manual visual comparison is not automated pixel-diff validation. Do not promote defaults until validated.

## Completion gates

All existing unit tests pass; smoke and complete benchmark run; compare same machine and same quality; verify all size/flag metadata; investigate any zero/unused counters; confirm image parity at full resolution and acceptable lower resolution; measure P95/P99, allocation, capacity fallbacks, actual target sizes, and memory use. If no meaningful gain, leave options off. Targets remain mean and P95 <= 16.67 ms at selected production quality, not guaranteed. Direct GPU timestamps, full shader audit, additional cross-ambient field sharing, atlas work, and broader renderer consolidation remain future tasks.
