# PO10F — Graphics quality presets

Apply to the PO10F Optimization Controls candidate. This package also includes the preceding Live FPS Diagnostics update, so installing the diagnostics separately is not necessary.

Both interactive labs and Graphics Settings expose GRAPHICS PRESET (LIGHT/SHADOW) near the top of the first page. Click the row or select it using the existing controller/keyboard UI navigation to open four named choices. The adjacent BUDGET / BLOOM / SAMPLES status row reports the other preset settings. Existing individual controls remain available.

| Preset | Lighting budget | Illumination scale | Shadow scale | Bloom scale | Area sampling |
|---|---|---|---|---|---|
| Ultra | High | 100% | 100% | 100% | Automatic |
| High | High | 75% | 100% | 100% | Automatic |
| Balanced | Balanced | 50% | 50% | 75% | Adaptive |
| Performance | Low | 25% | 50% | 50% | Adaptive |

These are proposed quality bundles, not measured performance guarantees. Scale percentages refer to intermediate targets, not backbuffer resolution. Automatic and adaptive sampling obey the current lighting budget and authored overrides. Lighting budget can affect other renderer quality decisions. The preset applies only these five settings and resets any forced sample count to automatic/adaptive. It leaves optimization masks, static caches, display resolution, fullscreen, VSync, FPS limit, light motion, wall motion, and explicit lab feature toggles unchanged. This preserves reference/optimized A/B testing. The existing Enable All Optimizations and Enable All Established Opts actions are independent. The existing PO10A profile selector remains available for legacy A/B comparisons; it may change optimization flags and quality settings, unlike the new quality-only selector.

The selected preset is derived from effective quality values rather than saved separately; nonmatching combinations show CUSTOM. Graphics Settings uses its existing draft/apply/save workflow and JSON schema, preserving existing files and defaults. Manual labs apply immediately using the existing profile reset and cancel-capture path. Automated benchmark and visual configurations are unchanged.

## Acceptance

- Run the Release unit tests and build. Added nine parameterized test cases (four preference cases, four lab cases, one ordering case).
- Open Lighting Quality Lab; select Enable All Optimizations, then switch among all four presets. Verify all 14 live switches remain enabled and displayed scales/budget/bloom/samples change. Check VSync and frame cap remain unchanged.
- Disable all live optimizations; change preset and verify disabled switches stay disabled. Re-enable optimizations and verify preset remains selected.
- Change one scale to a nonmatching value; verify CUSTOM appears. Reselect a preset to restore its values.
- Repeat in Performance Lab; selecting a preset cancels any active capture using the existing cancellation path.
- Open Graphics Settings, select preset and Apply and Save; restart and verify values persist. Verify temporary lab changes do not overwrite saved settings. Existing environment override read-only mode remains unchanged.
- Optional: run the existing PO10F Quick benchmark to verify no automatic configuration changes. Presets are not a new benchmark suite and exact preset performance/visual acceptance remains outstanding.

No shader changes or new benchmark cases. C# build and runtime testing unavailable in the authoring environment.
