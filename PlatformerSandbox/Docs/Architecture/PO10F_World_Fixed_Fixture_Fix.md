# PO10F — world-fixed lighting fixtures

## Defect
The adaptive, static, and static_shadow developer fixtures called BenchmarkLightingFixtures.Create on every draw using the live camera viewport. This put their point/tube light positions at the live viewport center and changed light spacing with viewport size. The previous PO10F moving tests had 78/120 and 75/120 camera-change frames respectively; light geometry changed on exactly the same number of frames. Previous pixel equivalence tests compared two renderers using the same incorrect fixture and could not detect the camera coupling.

## Fix
The two lighting labs own a BenchmarkLightingFixtureSet initialized once at scene activation using the initial player center and a canonical 1280 x 720 reference layout with the level's pixel density. It stores immutable light records in read-only collections. Rendering only retrieves the chosen fixture; camera movement, resizing, changing optimization switches and switching between fixtures no longer change the emitter positions. This deliberately changes the moving-fixture workload. The Normal fixture and production lighting, shadow algorithms and exact cache invalidation are unchanged. Animated occluders and moving player shadows may still legitimately change shadows.

## Validation
- Three parameterized C# tests cover all fixture layouts, camera projection, resize, fixture switching, and fresh scene initialization.
- The PO10F performance runner requires world_fixed_v1 metadata and rejects any captured static-shadow fixture geometry changes, even with camera motion enabled.
- Visual captures record actual world light positions. The hard-cache suite compares stationary and moving camera captures for identical emitter positions and distinct cameras. The moving capture is now at tick 30 instead of 120, to target an earlier part of the sweep; matching-position optimized/reference comparison remains exact RGB. This is not continuous-motion video testing.
- Manual: select Static Shadow, freeze light/scene animation and turn wall animation off. Enable camera sweep with the player stationary. Shadows must remain world-aligned; then jump and inspect normal dynamic player shadow changes. Check again with static hard-shadow caching toggled off/on and test fixture switching. Confirm the panel displays WORLD FIXED.
- Run `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj -c Release`, `py .\Tools\po_run_benchmarks.py --po10f --tier quick`, then `py .\Tools\po_visual_regression.py --hard-cache --no-build` if the manual issue is resolved. New moving results supersede previous PO10F moving fixture comparisons. C# compilation and runtime acceptance pending.
