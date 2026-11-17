# A10 — Ability Coordinator (one-pass architecture and gym)

**Baseline:** A09 Character States one-pass ZIP over the approved World Events / World Definitions / Shared World Clock / A11 / A08 implementations. This update does **not** alter the CharacterMovementController, collision solvers, platform math, WorldDefinition or existing gym scene layouts.

## Purpose

Introduce a deterministic, per-player ability coordinator with typed, device-independent requests, activation guards, shared energy, fixed-world-tick execution and cooldowns, cancellation, stun interruption, explicit concurrency, immutable snapshots, and post-commit semantic events. Two **reference abilities** exercise the contracts without inventing a combat or dash motor:

| Reference ability | Slot | Energy cost | Active ticks | Cooldown ticks from activation | Demonstration |
|---|---|---:|---:|---:|---|
| **Pulse** | Primary / A09 `Acting` | 45 | 16 | 25 | Emits `AbilityActivated` and `AbilityCompleted`, owns the action lane while active; no damage, motion, or effect rendering |
| **Focus** | Utility | 20 | 60 | 100 | Energy regeneration rises from +1 to +2 per successful world tick while active; can run concurrently with Pulse |

Maximum energy is **100** per player. Regeneration happens **after** the accepted simulation tick, including the activation tick. Active duration and cooldown decrement at the end of each committed tick, **including activation tick**. Thus activating Pulse at world tick 1 yields `ActiveTicksRemaining=15`, `CooldownTicksRemaining=24`, and `Energy=56`. A world without authoritative ticks (paused) does not advance either timer or energy.

**A10 is an ability-coordination milestone, NOT yet the external-force/impulse milestone.** The user's planned **dash** must eventually provide real collision-safe boosted motion through a motor-facing impulse/motion contract. Pulse is not labeled a dash, and neither reference ability teleports or overrides physics. Wind velocity addition and damage knockback are likewise deferred to the common motion-contribution milestone.

## Contract and integration

Add `PlatformerSandbox.Gameplay.Abilities.CharacterAbilityCoordinator` (internal player-local implementation) and publicly typed:

- `CharacterAbilityId` (`Pulse`, `Focus`), `CharacterAbilityRequestKind` (`Activate`, `Cancel`), `CharacterAbilityRequest(PlayerId, AbilityId, Kind)`.
- `CharacterAbilityRejection` (`Stunned`, `AlreadyActive`, `OnCooldown`, `NotEnoughEnergy`, `ActionBusy`, `NotActive`), and `CharacterAbilityEndReason` (`Completed`, `Cancelled`, `Interrupted`).
- `CharacterAbilitySnapshot` containing energy and both immutable ability statuses. `WorldSession.GetPlayerAbilities(PlayerId)` returns a value snapshot.

The new `WorldSession.Step(playerCommands, stateRequests, abilityRequests)` preserves both old overloads (`Step(commands)` and `Step(commands, stateRequests)`). The world validates unknown players, invalid enum values, and duplicate or contradictory ability requests **before** stepping anything. It processes players by stable PlayerId and requests by **cancellation before activation, then ability ID**. All state machines, ability runtime, buffered inputs and motors are forked and committed only when **every** motor step succeeds. Rejected ability requests produce a committed `AbilityRejected` event but do not throw or spend energy. Invalid request lists throw before commit. `Snapshot()` never replays event batches.

**Interruption/priority:** A09 stun interrupts both abilities (no refund; cooldown continues). Direct A09 `EndAction` interrupts active Pulse; direct A09 `BeginAction` blocks a new Pulse with `ActionBusy` while Focus remains available. `ClearStun` followed by ability activation in the same tick works. Cancelling an active ability ends its duration but **does not** refund cost or clear cooldown. Ending Pulse returns the A09 action lane to `None`; Focus never occupies that lane. Both players have independent resource pools and timers. Respawn resets only the targeted player's abilities and energy; joining or rejoining receives fresh per-player state. Full world restart recreates the world.

The existing `WorldGameplayEvent` envelope gains optional `AbilityId`, `AbilityRejection` and `AbilityEndReason`, and event kinds `AbilityActivated`, `AbilityCompleted`, `AbilityCancelled`, `AbilityInterrupted`, `AbilityRejected`. This preserves existing constructor callers, immutable event metadata and `WorldInstanceId/WorldTick/Sequence` identity.

## Playable gym verification

1. **Enter the shared-world gym with F4.** The old scene loads first. F2 optionally enters the authored world; both include the new **A10 ABILITY COORDINATOR** panel on the lower right. **Do not** press F2 unless you want the authored world. Existing F4/F2, R/U/T, P, F8, Z/X/C/O, N/M/V controls are preserved.
2. **B — Player 1 Pulse:** the HUD displays `P1 ENERGY 56/100`, `PULSE A15 C24` after the first successful tick; A09 action becomes `Acting`. There is no dash or physical impulse. Let 16 world ticks elapse: action becomes `None`, Pulse active ticks reach zero and an `AbilityCompleted` event appears.
3. **B again while active**, or during its cooldown: see `AbilityRejected` (`AlreadyActive` or `OnCooldown`); no extra energy spent. Cooldown reaches zero 25 successful ticks after activation. Repeatedly activate when ready to eventually test `NotEnoughEnergy` (energy regenerates more slowly than repeated Pulse costs).
4. **G — Player 1 Focus:** independent utility ability. Its remaining timer counts down for 60 ticks and energy regenerates +2/tick. Press **B while Focus is active** to verify Pulse and Focus are concurrently active. The two ability timers remain independent.
5. **H** cancels Player 1 Pulse; **K** cancels Player 1 Focus. Both produce `AbilityCancelled`, cease activity, and retain their cooldown. Cancelling a non-active ability reports `NotActive`.
6. **X** stuns Player 1. Both active abilities should produce `AbilityInterrupted`, and Pulse's A09 action lane should return to `None`. Pressing B or G during stun reports `Stunned`. **C** clears stun and allows activation if resource/cooldown permits.
7. **Y** activates Player 2 Pulse independently. P1's resource and timers remain unchanged except for its own normal tick advancement. Use **P** to pause: timers, energy and world tick all freeze. Use **R/U** to respawn independently: only the targeted player's resources reset to 100. **T** fully restarts the world. In authored mode, **N/M** add/remove P3 with fresh abilities.
8. Existing event history panel shows committed A10 events. **F3** in F4 clears only the log display. Existing moving-platform/crush gym (exit F4) must remain unchanged.

**Controls:** `F4` shared-world gym; `F2` inside F4 authored-world alternate; `B` P1 Pulse; `G` P1 Focus; `H` cancel P1 Pulse; `K` cancel P1 Focus; `Y` P2 Pulse; `X` stun P1; `C` clear P1 stun; `O` stun P2; `R/U` respawn P1/P2; `P` pause; `T` restart; `F3` clear event history. The ability keys are keyboard-driven *gym test controls*, not yet configurable game input bindings. Core ability requests remain device-neutral.

## Automated acceptance

Run `CharacterAbilityWorldTests` (30 cases), then `CharacterStateWorldTests`, `WorldGameplayEventTests`, `WorldDefinitionPlayerSlotTests`, `SharedWorldClockTests`, and the **entire solution's xUnit suite**. The tests cover eligibility, explicit rejection reasons, activation cost, Focus regeneration, exact active/cooldown ticks, concurrency, cancel/interrupt, no refunds, action-lane ownership, stun rules, per-player ownership, late join, respawn reset, deterministic request order, invalid command atomicity, event identity, paused time, and motor equivalence.

**Build status:** Source/package inspected for structural consistency. The delivery environment does not contain a .NET SDK or MonoGame assemblies; it cannot compile or execute the C# tests. Local Visual Studio build and full xUnit run are required to accept the milestone.

## Next technical dependency

The ability system can now schedule a real dash, but **do not move the character by mutating `Bounds` in an ability callback**. Implement collision-safe external-velocity/impulse contributions in the authoritative motor, then make Dash one of the registered abilities. Add explicit gym fixtures for 100% walking left + 75% wind left = **175% left**, 100% walking right + 75% wind left = **25% right**, knockback, dash collision, and mixed-case interruption.
