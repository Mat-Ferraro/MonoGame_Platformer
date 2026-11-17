# Shared World Clock / WorldSession — Phase 1

## Scope

This phase is the first explicit authoritative, device-neutral `WorldSession` implementation. It introduces a world-owned, integer `WorldTick`, a world-derived `WorldTimeSeconds`, ordered `PlayerId` instances, current-time spawning/respawn, and all-or-nothing fixed-step staging for two or more independent motors. **It does not implement full local-multiplayer UX, dynamic player-vs-player collisions, game levels, world events, or serialization.**

The original single-player `MovementGymSession` is kept as a separate, known-good legacy physics test fixture. Its normal respawn and tuning behavior are unchanged. WorldSession uses the proven `CharacterMovementController` rather than rewriting collision scheduling.

### Technical boundaries

- `WorldSession.WorldTick` is the single authoritative timebase; timestamps are derived from `WorldTick * FixedStepSeconds` rather than an arbitrary character's clock.
- `CharacterMovementController` accepts optional `initialWorldTimeSeconds` (default `0` for all existing code). Starting platform overlap, support, velocity and obstruction queries are evaluated at that exact starting time.
- `StepOnWorldTimeline(start,end,...)` rejects mismatched or nonrepresentable scheduling before advancing a motor. Each motor receives the same world interval, regardless of birth/respawn time.
- `WorldSession.Step(commands)` validates all player-attributed commands, then forks each motor and buffered jump input; it publishes the next tick and snapshots only after all updates succeed. Player IDs are processed in ascending order, not dictionary/input iteration order.
- `RespawnPlayer(id, boundsAtCurrentWorldTime)` replaces exactly one motor and one buffered input at the **current** world time. It does not reset other players, moving platforms, the world tick, or level state.
- No player-vs-player collision is introduced. Motor collision authority, strict numerical diagnostics, moving-platform contact classification, and existing assistance policies are unchanged.
- A11 devices are still source IDs separate from Player IDs; the F4 demo maps the selected A11 device to logical Player 1, and separate J/L/I test keys to logical Player 2.

## Playable F4 shared world test

1. Launch the existing gym. Press **F4** to enter the **SHARED WORLD CLOCK TEST** scene.
2. Two colored actors (P1 cyan, P2 purple) begin on the **same gold horizontal platform**. Without input, both should ride it through reversals. The HUD displays WORLD TICK, TIME, each player's X, support collider, and respawn count.
3. Move P1 with **A/D + Space** (or use **F8** to switch P1 to Gamepad 1); move P2 separately with **J/L + I**. The actors must respond independently. They currently pass through one another, deliberately.
4. While both actors are on the moving platform, press **R**. **Only P1** should reappear at the **current** platform position. World time and P2's position/platform phase must not jump. Press **U** to test P2 independently.
5. Press **P** to pause. The world tick and both actors must stop advancing, and resume without a time catch-up burst.
6. Press **T**. **Unlike R/U**, this is a full world reset: world tick returns to zero and both actors/platform restart at their initial locations.
7. Press **F4** to exit, then check the original F9 step bump, F10–F12 request cases, and all existing crush scenarios are unchanged.

`F1–F3`, `Tab`, `-/+`, `F9`, and the A08 request test are specific to the original movement gym, not the isolated F4 scene. F5–F7 remain fault-policy switches for both. F8 changes P1's selected source, as it did in A11.

### Headless regression tests

Run `SharedWorldClockTests`, then the **full** solution test suite. Cases cover synced moving-platform time through 240 steps, mid-trajectory respawn isolation, late join, independent input, command-order invariance, duplicate/invalid command rejection with no clock commit, invalid respawn atomicity, and removal of one player.

### Completion gate

- Full solution builds and all existing plus new xUnit tests pass.
- F4 gym two-player support and both respawns behave as documented; P pauses the world and T explicitly restarts it.
- No modifications to the existing M24–M27 physics solver, assistance math, or the original crush gym are required.
- Once validated, the following step is general player-slot/device ownership and reusable world definitions; the F4 fixture is not intended to be the final game scene.
