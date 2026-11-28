# PB01F–P consolidated candidate — partial implementation

Baseline: user-validated PB01D–F. The candidate does not close PB01. The existing analytical kinematic character movement model remains authoritative; dynamic bodies still solve against static terrain only. The new contact and compression APIs are reusable foundations, not an integrated coupled collision solver.

## Implemented scope
- F: canonical typed collision pairs, exclusions, relative linear sweeps, terrain material assignments; existing runtime terrain-only filtering retained.
- G: runtime copies of authored platform trajectories; pause/resume/reverse through WorldSession.ControlPlatform at a committed tick. Historical trajectory sampling remains available. No automatic obstruction response yet. Use the world API rather than modifying a trajectory directly; history retention is currently unbounded.
- H/J: deterministic contact islands from supplied contacts; directed axis compression constraint propagation across mixed chains. No automatic continuous contact generation, island motion solver or runtime chain crush dispatch yet.
- K/L: world-level classified crush death policy, independent respawn mode (none/immediate/delayed/manual), optional maximum respawns, queued non-crush death, safe spawn retries. Dead players are not simulated, rendered or spatially registered. Legacy throw and named-spawn recovery remain available. Death captures policy at that time. Existing administrative reset APIs are separate from gameplay lives. Safe spawn tests currently include terrain and moving platforms, not coupled dynamic bodies.
- M/N: existing committed spatial views updated on lifecycle and platform commands. JSON authoring for global/whole-level crush and respawn settings, and static collider material assignments. Full LDtk fields and general entity lifecycle are outstanding.
- O: existing Physics Interaction Lab and Engine Integration Lab receive visible controls for platform playback, death/respawn policies, terrain materials and geometric contact inspection. No new geometry or fully integrated multi-body fixtures in this candidate.
- P: new C# tests and revised deterministic benchmark workload. Visual captures retain the previous D–F coverage; new gameplay control sequences are exercised by headless tests and benchmark, not new graphical scenario assertions.

## Configuration
`Content/Physics/CrushLifecycle.json` configures authored LDtk worlds. Global defaults Death + Immediate, unlimited respawns; Levels entries are complete override objects, not property merges. DelayTicks is simulation time; MaximumRespawns null means unlimited. Raw/test worlds preserve their existing explicit policies. `DynamicBodies.json` supports TerrainMaterials (static collider ID to material name) and additional body fields. Runtime collision masks remain Terrain or None until the coupled solver is implemented.

## Validation procedure
1. Extract overlay at solution root after PB01D–F. Previous manifests identify their original releases; validate this overlay against PB01F_P_Overlay_Manifest.json.
2. `dotnet clean -c Debug` then `dotnet build -c Debug --no-incremental`.
3. `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj -c Release --filter "FullyQualifiedName~PB01ContactFoundationTests|FullyQualifiedName~WorldCrushRespawnTests|FullyQualifiedName~PB01DynamicBodyTests|FullyQualifiedName~PB01FoundationTests"`
4. `py .\Tools\pb01_validate.py --tier quick` and retain consolidated archive. Full tier only after quick and manual acceptance.
5. In both labs, test pause/resume/reverse while riding; verify no jumps and consistent carried motion. Test each respawn policy, including no respawn and exhausted budget, safe spawn retry, reset by level reload, material editing and existing character movement. Contact inspection is geometric, not solved contacts. Crates still do not push or support each other or characters.

Benchmark workload version 2: same absolute simulation tick boundaries for each comparison, scripted impulses, platform commands and deaths/respawns. Quick 180–480, Full 420–2220. Exact tick windows and executed workloads are checked. Not a controlled comparison with D–F workload version 1. Track new version as fresh baseline.

## Remaining critical path
Coupled continuous-time world scheduler, motion proposals for kinematic characters, deterministic multi-body pushing/stacking/carrying, actual chain crush classification and transactional ghost/stop/reverse responses, general entity destruction and authoring, richer integrated fixtures and graphical acceptance. Rotation remains PB02. Do not enable an approximation or mark these items complete merely because the pure contact graph tests pass.

19 Python tooling/fixture tests passed in the packaging environment. No .NET SDK is available there; C# compilation and all runtime claims are candidates pending local validation.
