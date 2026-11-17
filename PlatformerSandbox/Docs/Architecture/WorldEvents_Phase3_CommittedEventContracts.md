# World Events Phase 3 — Committed gameplay events and authored trigger gym

## Purpose and ownership

`WorldSession.Step()` is the single authoritative world simulation commit point. It already clones/stages every player motor and jump buffer so that a failed step publishes no partial state. Phase 3 creates **immutable, read-only `WorldGameplayEvent` batches** at that same commit point. It does **not** modify motor formulas, collision detection, platform paths, simulation fault handling, or the A08/A11 input pipeline.

## New API

- `WorldGameplayEventKind`: `PlayerJoined`, `PlayerRemoved`, `PlayerRespawned`, `JumpStarted`, `LeftGround`, `Landed`, `WallHit`, `TriggerEntered`, `TriggerExited`.
- `WorldGameplayEvent`: `WorldInstanceId`, `WorldTick`, `Sequence`, `Kind`, `PlayerId`, end-of-tick `Position`, optional `ColliderId` / `TriggerId`, nonnegative `Intensity` for contacts where available. `(WorldInstanceId, WorldTick, Sequence)` is the stable consumer deduplication key. Use a new `WorldInstanceId` whenever replacing or restarting a world; the MonoGame gym allocates one monotonically increasing run ID. Deterministic replays can inject the same ID deliberately.
- `WorldStepResult.Events` contains **only events from that successful world tick**. `Snapshot().Events` is always empty, preventing accidental replay of a previous batch. Event lists are defensive read-only copies; consumers cannot mutate the world through them.
- Player join/remove/respawn operations remain synchronous for backwards compatibility, but queue a semantic event for the **next successfully committed** `Step()`. Rejected ticks do not consume queued events. Moving-platform timeline and other players are not reset.
- `WorldTriggerDefinition`: validated, nonblocking AABB with stable trigger ID and tag. `WorldDefinition(..., triggers: ...)` adds an optional collection; collider IDs and trigger IDs occupy different namespaces, and trigger bounds may overlap solid geometry by design.
- Trigger events are **per player**, emitted only when **end-of-tick occupied state changes**. `TriggerEntered`/`TriggerExited` are not emitted every frame inside or outside the zone. Occupancy is reset when a player respawns or leaves; entering the new spawn trigger is reported on the next successful tick.
- Wall-hit events are contact-entry edges per player/collider to avoid every-frame audio spam. Landings are grounded transitions, not every-frame grounded status. Multiple events within a tick have deterministic sequence order independent of input-command enumeration order.

## PLAYABLE TEST — IMPORTANT: F4 then F2

1. Start `PlatformerSandbox`, press **F4**, then **F2**. The authored-world dashboard says `GYM_TWO_PLATFORMS`. The original F4 two-player clock gym is still available by pressing F2 again.
2. The new **WORLD EVENTS** panel is at the left, directly below the authored-world panel. `F3` clears its **presentation history** without changing the world or authoritative trigger occupancy. The HUD lists the 8 most recent committed events, newest first, with tick, sequence, player, kind and collider/trigger ID.
3. Press **Space** (or controller A if F8 selected). The panel should show **JumpStarted P1** once. Holding the button should not repeat that event; actual takeoff is required. On return to a floor/platform, **Landed P1** appears once.
4. Press **I** to jump P2. Verify the event has **P2**, not P1.
5. Press **R** or **U**. On the next successful world tick the log shows **PlayerRespawned** for the selected player; the other player's timeline does not reset. Pause via **P** to verify no new steps or events are emitted while paused.
6. **TRIGGER TEST:** Press **V** to select the `GROUND` spawn for P1, then **R** to respawn there. A yellow `ZONE 301` covers the center ground. After the next step, look for **TriggerEntered P1 ZONE:301**. Stand inside the region and watch: no repeated enter events. Hold **D** to walk outside: **TriggerExited P1 ZONE:301** occurs once. Walk back through the zone to see a new entry, then new exit. The trigger does not block motion or itself set a checkpoint.
7. Press **N** to join P3, **M** to remove P3; inspect `PlayerJoined P3`, `PlayerRemoved P3`. Press **T** to restart: tick resets, event history clears, and `RUN` changes to a different world-instance ID. Existing F5/F6/F7 fault modes, A08, A11, and legacy crush gyms remain available.

## Regression tests

Run `WorldGameplayEventTests` from Visual Studio Test Explorer and then all tests. Covers jump/landing edge events, per-player attribution, once-only lifecycle events, trigger entry/exit, failed-step atomicity, stable deterministic sequence and input order, immutable results, trigger schema validation, world identity after restart, and physics-state isolation.

## Deliberately deferred / known boundaries

- The current static zone detector uses **end-of-tick AABB intersection**. Extremely fast bodies can pass completely through a thin zone between ticks without an event. Future authored checkpoints, hazards or high-speed trigger volumes should add swept crossing and proper temporal contact ordering before relying on this as a safety-critical gameplay trigger.
- The trigger is a report-only data contract, **not** a checkpoint, damage volume, pickup state machine, or engine-wide event bus. Higher-level policies will subscribe to committed events and decide their own state transitions in later milestones.
- Contact `Position` is the authoritative **end-of-tick** location; exact time-of-contact position requires a later richer contact-event projection. `Intensity` is a diagnostic, not a damage calculation.
- Events are a short-lived batch for presentation/event sinks. No durable history or global dispatch service is installed in Gameplay. The gym retains a small presentation-only history; new scene/audio/VFX consumers must apply run/tick/sequence deduplication and owner lifecycle policies.
- User validation through local build/full test run remains required. Package authoring environment has no `dotnet` toolchain.
