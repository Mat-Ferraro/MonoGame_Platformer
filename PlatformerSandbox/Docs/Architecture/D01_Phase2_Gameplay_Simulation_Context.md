# D01 Phase 2: Gameplay and Simulation Context Capture

## Scope and architecture

- `PlatformerSandbox.Diagnostics.DiagnosticContextRecorder` is a reusable, MonoGame-independent, thread-safe in-memory recorder. It stores at most 96 bounded recent activity entries and a replaceable snapshot of the last **committed** state. It performs no per-tick disk I/O.
- `Platforms/DesktopGL/Diagnostics/GameDiagnosticBridge` is a project-specific adapter. It maps existing WorldSession results, commands, scene destination and MovementGymSession state into generic diagnostic keys. Gameplay and Simulation have no dependency on the logging module.
- Before calling `WorldSession.Step`, the host records an explicit **in-flight attempt** and the commands/requests. After `Step` returns successfully, it updates committed state, marks the attempt complete, and records confirmed events. If `Step` throws, the prior committed snapshot remains intact and the attempted tick is explicitly marked uncommitted.
- On scene changes or UI suspend/resume, the adapter replaces scene context and clears obsolete pending attempts. The recorder is owned by Program, so a fatal error escaping Game1 can still access it.
- The crash writer appends this bounded context to the existing D01 report. The first-phase JSONL logger and existing crash hooks are retained.

## Recorded context

- Active scene, level ID, entrance ID, suspended/transition state, selected host fault mode.
- WorldInstanceId, last committed WorldTick/time and up to 4 player snapshots: position bounds, velocity, grounded/jumping, moving support, state, environmental/wind velocity, impulse/dash contributions.
- Most recent attempted per-player semantic movement commands, state/ability requests and environment motion requests.
- A bounded history of successfully committed tick summaries and confirmed semantic gameplay events (up to 12 per tick).
- For the standalone movement gym, elapsed simulation time, player bounds/velocity, platform support, and last step recovery count.

## Boundaries and limitations

- No raw keyboard text, arbitrary user files, personal names, network identifiers, passwords, or secrets are captured. Paths and other data already in exception stack traces may contain private information; reports remain **manual sharing only**.
- Logs are not full input replays, and the activity ring may omit older ticks. The recorder does not attempt native dumps, process hang detection, out-of-memory recovery, or every possible OS termination.
- Phase 2 attaches committed snapshots and exception messages. Low-level structured `SimulationFaultSnapshot` and per-recovery histories may be added later through a narrow adapter if explicitly exposed by gameplay; nothing is pulled from mutable live solver state during exception reporting.
- The selected fault mode is labeled `simulation.hostFaultMode` because it is drawn from the host's movement-gym policy, which is used to configure the world. No new public WorldSession fault-policy API is introduced.
- All diagnostic calls are one-way and best-effort, isolated from gameplay behavior. No simulation results are altered or recovered by diagnostics.

## Acceptance

1. `D01GameplayContextTests` and original `D01FileDiagnosticSessionTests` pass, followed by all xUnit tests.
2. Run the Debug-only command: `dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=MOTION_LAB --dev-context-crash-after-world-step`.
3. Confirm the report includes a level/world/player snapshot, a successfully committed tick in recent activity, and **no pending operation** after intentional post-commit failure.
4. Reproduce a protected failure during an attempted step (if one is available) and confirm the previous committed tick and uncommitted attempt are distinguished; do not reintroduce the previous wind-tunnel freeze intentionally.
5. Verify release builds contain no intentional crash switch, and normal gameplay, pause, scene transitions and diagnostics continue to work.

**Source packaging:** the APPLY script uses checked, minimal modifications to Program.cs and Game1.cs. It retains the user's latest physics fixes and other host modifications; it backs up affected files and aborts when expected integration anchors no longer match. No new project/solution references should be required because D01 Phase 1 already installed Diagnostics.
