L02.9 — Shadow budget continuity and crate appearance

Problem: Balanced lighting had exactly two shadow slots for the two authored shadow-casting lamps. A high-priority Pulse flash competed for one slot. LightingFramePlanner removed CastShadows from the displaced lamp but still drew its full illumination, visibly filling its previously occluded regions until the flash ended.

Fix:
- Balanced allows 3 shadow masks, High allows 5, Low remains 0.
- Authored level shadow lights are reserved ahead of transient lights for both the visible light budget and shadow mask allocation.
- Shadow-dependent transient flashes that do not receive a mask are omitted instead of drawn as unshadowed floodlights. Other light budgets remain bounded.
- All active masked light contributions are still combined independently; an exposed area can legitimately be lit by more than one light.

Crate appearance is separate from this bug:
- Left test crate: LightingMode=Silhouette, CastsShadows=true. Dark visible surface is deliberate; only a special front-facing flash can briefly reveal it.
- Right test crate: LightingMode=Lit, CastsShadows=true. It receives the local lamps normally, and can look significantly brighter. No authoring settings were changed.

Validation:
1. Build and run tests: dotnet test .\\PlatformerSandbox.Tests\\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~L029ShadowBudgetContinuityTests
2. Launch: dotnet run --project .\\Platforms\\DesktopGL\\PlatformerSandbox.DesktopGL.csproj -- --dev-level=LIGHTING_FLAT_TEST
3. With Ctrl+Alt+2 select Dramatic, stand beside each crate and pulse/dash. The flash must not make an authored lamp's existing cast shadow disappear. The left silhouette may be briefly revealed, but remains dark under ordinary lamps.
4. Verify Ctrl+Alt+3 quality Low/Balanced/High. Low deliberately has no projected shadow masks; Balanced and High preserve both authored masks during a dash.
5. Check normal level lighting and other effects are unchanged.

No changes to physics, collision, movement, or LDtk content.
