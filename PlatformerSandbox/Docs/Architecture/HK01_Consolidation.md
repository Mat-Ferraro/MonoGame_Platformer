# HK01 — UI and level housekeeping candidate

Scope: consolidate current lighting-lab UI, retire duplicate menu entries and obsolete in-game multi-case sweeps, and give manual lighting validation a horizontally and vertically scrollable level. This is not a renderer optimization phase.

## UI ownership

Lighting Quality Lab and Engine Performance Lab use the shared interactive panel for input and drawing. The generic lighting tools panel, legacy phase-21 shortcut/status overlay and generic HUD/footer no longer stack over these labs. Keep the visible collapse/expand header, live FPS/TPS, master optimization controls, graphics preset, independent quality controls, and individual flags. Unique lighting controls have been migrated into the shared panel. The performance lab retains current-configuration capture, with visible capture lengths and cancellation; multi-case sweeps belong to the existing Python runner. The generic developer tools still serve non-lab scenes. Graphics Settings separates quality from the master optimization controls. Legacy serialized profile fields remain compatible.

## Level catalog

Active menu: Connected Cavern, Engine Performance Lab, Lighting Quality Lab, Movement Gym, Material Showcase, Light Types Showcase, Back. The four superseded labs (Lighting Integration Test, Emissive Color Lab, Phase 21 Lab, Lighting Basics) are removed from the normal menu, as is their legacy submenu. Other auto-discovered developer fixtures are not advertised. No old content assets are deleted; direct developer launch and regression access remain. Specialized material and light-type scenes remain because they cover cases not fully represented by the general lab.

## Manual lighting level

Content/Levels/LightingQualityLab.ldtk is now an independent 3840 x 1280 authored project with three copies of the established showcase layout, a continuous ground, 15 authored lights, and one center spawn. Public destination LIGHTING_QUALITY_LAB maps to its internal LIGHTING_PERFORMANCE_LAB identifier so existing layer policies are reused. Separate files and unique instance identifiers prevent sharing benchmark content. The original LightingPerformanceLab.ldtk is unchanged (1280 x 1280). The new level is more demanding: do not compare its FPS directly with the old benchmark. If explicit build content inclusion is used, ensure the new LDtk file is copied to the same Content/Levels directory as the other levels.

The shared panel exposes Camera Mode: Follow Player, Horizontal, Vertical, Both Axes. Non-travel axes are not offered. Manual sweep is presentation-only and independent of the existing automated sweep. Camera center and available travel are displayed. Normal fixture is the full wider scene; the special static/adaptive fixtures are world-fixed near the center spawn. Moving far from their light sources can naturally leave them out of view. For a static-shadow inspection, freeze scene and wall animation, and use camera sweep or jump/move near the central fixture.

## Validation

- Run `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj -c Release`.
- Run `dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj -c Release`.
- Confirm the six active levels and Back; open each, return to menu, verify no duplicate panel in the two lighting labs. Test mouse, keyboard, controller, collapse, selection, graphics settings and persistence.
- In Lighting Quality Lab select Normal, Enable All Optimizations, then walk in both directions and jump. Try each camera mode, freeze scene/wall animation, and inspect shadow alignment. Test graphics presets and independent controls. Confirm the performance lab still offers current capture.
- Optional quick regression: `py .\Tools\po_run_benchmarks.py --po10d --tier quick --no-build` (after building Release). Automated runs retain the original authored level and case configuration but use a consolidated compact HUD (metadata HUD layout: compact_v2). Establish a new comparison baseline rather than assuming HUD timing is identical to prior captures.
- Python structural checks: `py -m unittest discover -s Tools -p 'test_po*.py'`. The old phase21_performance_ui_preflight entry point now runs HK01 checks rather than asserting deleted panel internals. Other historical phase preflight scripts are archival, not authoritative current acceptance checks.

Candidate reconstructed from the earlier full text export and delivered patches through the world-fixed fixture fix; local edits outside those patches were not available. All 24 Python tests passed, including 4 new structural checks. PO10D and PO10F quick plans generated successfully. New C# regression tests have not been executed: .NET/MonoGame build environment and project files are not present here. LDtk runtime import, actual content deployment, live UI and camera movement require local verification. No shader, graphics preset values, cache algorithms, or saved defaults intentionally changed. No physical removal of historical levels or wholesale migration of non-lab tools in this pass.
