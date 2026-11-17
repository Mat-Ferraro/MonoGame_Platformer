# A09 — Character State Architecture (complete foundational pass)

Project: MonoGamePlatformer / PlatformerSandbox. Source baseline: committed World Events Phase 3 package, with A08/A11 and WorldSession/WorldDefinition layers already installed. **A09 state architecture is independent of the A10 ability system.**

## Objective and boundaries

Implement three **orthogonal, per-character** state lanes without replacing the existing motor:

- **Locomotion** (`GroundedIdle`, `GroundedMoving`, `Rising`, `Falling`) is derived from **accepted movement results**, not player button presses. When supported on a moving platform, idle/moving is classified relative to that platform's velocity, not world-space carriage speed.
- **Action** (`None`, `Acting`) is an explicit marker controlled through guarded requests. It is intentionally not an attack/dash implementation or an input lock. A10 will own concrete ability eligibility, execution, costs and cooldowns.
- **Condition** (`Normal`, `Stunned`) can suppress *new horizontal intent and jump presses*. It does **not** freeze velocity, halt the world clock, move hitboxes, bypass collisions or interrupt other players. It also cancels a current action.

## State request semantics

`WorldSession.Step(commands, stateRequests)` accepts typed `CharacterStateRequest` operations:

| Operation | Preconditions/result |
| --- | --- |
| `BeginAction` | Accepted only when not stunned and action is `None`; otherwise emits `CharacterStateRequestDenied` with a reason |
| `EndAction` | Accepted only when action is `Acting`; otherwise denied |
| `ApplyStun(N)` | Positive fixed-tick duration required; gates the current tick, extends existing stun by **max(current remaining, N)**, cancels action |
| `ClearStun` | Clears an active stun; denied if already normal |

**Timing:** `ApplyStun(90)` blocks 90 accepted ticks, including the application tick, and leaves 89 ticks visible at its end. State time does not advance while the world is paused. A stunned motor still runs its complete authoritative collision step (neutral steering, no new jump presses). A pending buffered jump is discarded while stunned, so a previously queued press cannot leak through on the first recovered tick. An actual jump already in progress is **not** deleted or teleported; release edges remain effective.

**Determinism:** all requests are validated before stepping any player; duplicate kinds and contradictory pairs (`Begin+End`, `Apply+Clear`) are rejected atomically. Independently supplied request order cannot alter outcomes: per-character operations run `ApplyStun → ClearStun → EndAction → BeginAction`. Multiple players still step in ascending stable `PlayerId`. Changes are staged on forked state machines; accepted state and event batches commit together with the motors and world tick.

**Event contract:** the preexisting `WorldGameplayEvent` envelope is extended (compatibly, with optional payload fields). `CharacterStateChanged` includes a typed lane and former/current state names; `CharacterStateRequestDenied` includes the rejected request and reason. Events retain `WorldInstanceId + WorldTick + Sequence`, are **not** published by `Snapshot()`, and never go out from a failed step. `PlayerRespawned` is the explicit indicator that player-local action/condition state has been reset.

## Playable gym — no new scene required

1. Press **F4** for the original shared-clock world or **F4 → F2** for the authored-world fixture (including Player 3 join/leave). The A09 panel appears at the right, below the A11 input panel.
2. Observe P1/P2 locomotion: standing, moving, rising and falling change after authoritative movement. A platform that carries an idle character does **not** count as walking.
3. Press **Z** for P1's action `None → Acting`; press **Z** again to end it. The character can still move: the action lane is not an implicit motor lock.
4. While `Acting`, press **X**. P1 is stunned for **90 ticks**, action returns to `None`, and `Z` attempts during stun are visibly denied. Hold A/D and try Space: no new steering/jump press is accepted. P2 should still move and jump normally.
5. Once stunned, press **C** to clear P1's condition immediately; move/jump and press Z again. Press **O** to stun P2 independently, then test P1 controls.
6. Press **P** while stunned. Tick and stun countdown must freeze. Press P again to resume. R respawns P1 (clears all P1 state, keeps P2/world); U similarly resets only P2. T fully restarts the world.
7. In **F4 → F2**, press **N** to add P3: it gets its own state; M removes it; repeated joins do not leak previous state. V changes P1's spawn for a subsequent R, unchanged from earlier package.
8. Run F3 to clear the visible event history and latest state feedback. Confirm the existing WORLD EVENTS panel shows `CharacterStateChanged` and `CharacterStateRequestDenied` on committed ticks. Press F4 to exit; original single-player crush gym remains unchanged.

## Automated acceptance

Run `CharacterStateWorldTests` in Visual Studio Test Explorer; then run `WorldGameplayEventTests`, `SharedWorldClockTests`, `WorldDefinitionPlayerSlotTests` and the **entire solution test suite**. The new tests cover action guards, condition interruption, exact stun duration, pause, buffered-jump gating, independent players, action-not-modifying-physics, locomotion, join/respawn reset, deterministic request ordering, validation-before-commit, and backward-compatible `Step(commands)`.

### Deliberate deferrals

This is the **complete A09 foundation** (not an ability milestone). There is no attack/dash/knockback/wind physics in this patch. A10 adds ability lifecycle and policy using the action-state request contract. Gameplay conditions other than stun must be introduced with explicit design, guards and tests rather than enumerated speculatively.
