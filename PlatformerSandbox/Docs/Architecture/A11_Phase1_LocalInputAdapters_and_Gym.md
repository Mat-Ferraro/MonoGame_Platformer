# A11 Phase 1 — Local Keyboard/Gamepad Input Abstraction

## Baseline and scope
Apply **on top of validated A08 Phase 2**. This introduces one explicitly selected local input source for the current player. It does NOT add a second character, player slot manager, rebinding UI, analog speed scaling, independent WorldSession clock, or network input. None of the physics/collision/assistance code is changed.

## New boundaries

- `Gameplay/Input/Local/LocalPlayerInputRouter.cs`: headless device-neutral input snapshots (`LocalDeviceInput`), normalized fixed-step input (`LocalPlayerInput`), deterministic threshold/left+right conflict policy, source switching, press/release/held state, disconnect and reconnect rearm.
- `Source/Input/MonoGameLocalInputBindings.cs`: ONLY device-facing mapping of keyboard and Gamepad 1. Keyboard A/D/arrows and Space; Gamepad D-pad or left thumbstick X (threshold 0.50) and A button for jump.
- Input `SourceId`s are 1=keyboard, 2=gamepad. They are NOT player identities or collider IDs.
- `LocalInputRequestAdapter.Populate` converts normalized inputs into typed A08 requests for ordinary play; `MovementRequestGymHarness.Populate` adds synthetic sources only while F10 is enabled and now accepts an optional primary source ID.
- `Game1` polls exactly once in a fixed update, normalizes before the existing A08 resolver, and displays observational diagnostics. Validated requests still go through the authoritative `MovementGymSession.Step`.

## Gym test procedure

1. Launch DesktopGL. Assistance remains ON; F10 request testing remains OFF. The **A11 INPUT TEST** panel appears at upper right. Default SOURCE = KEYBOARD.
2. With no controller, A/D/arrows move; Space jumps. Observe MOVE and JUMP HELD/PRESS/RELEASE. Press F8 to select GAMEPAD 1; panel shows DISCONNECTED, and keyboard can no longer control the character. F8 returns to keyboard.
3. Connect one standard controller to PlayerIndex.One. F8 selects Gamepad 1 (reset to the same spawn); panel shows CONNECTED. Use D-pad or left stick (past 50%) to move, A to jump. A short press shows one PRESS; holding A does NOT repeatedly trigger presses; release shows RELEASE.
4. While A is held, switch back and forth with F8. Switching must not fabricate a new jump press. Disconnect with A held; movement becomes Neutral and the release edge occurs once. Reconnect while holding A: jump remains suppressed until A is released and pressed again.
5. Press F10 with gamepad selected, and use F11/F12: Control Lock overrides; Force Left/Right override; source 30 wins ties even if order reversed. Return F10 OFF, F8 KEYBOARD. Run previous F9 step-up and four crush cases in F5 Strict.
6. Run all xUnit tests, especially `LocalPlayerInputRouterTests` and existing `MovementRequestGymHarnessTests`.

## Important limitations

- The explicit source switch is a test fixture, not a live multiplayer device-claim UI. A disconnected gamepad intentionally does not fall back to keyboard. This avoids implicit cross-source ownership.
- Resuming from P pause reseeds the selected input source so a held button cannot create a new press edge. Press/release are reliable for transitions observed by fixed Update polling. A press and release entirely between polls cannot be reconstructed without an event-driven capture source; that is a later refinement, not a guaranteed capability here.
- No persistent key rebinding or UI focus context yet. Host bindings are configurable in code. Source switching resets gym for A/B comparisons.
- An A08 synthetic high-priority request can move the character even when the selected physical gamepad is disconnected; this is correct for the arbitration demo.
- On loss of a held jump, the router emits ONE release to avoid a sticky hold, even though no new press is ever generated from a missing source.

## Verification

Archive/file consistency and static source checks were performed here; no .NET SDK is installed in this environment, so the solution MUST be compiled and all xUnit tests run in Visual Studio. The existing gym is not executable in this environment.
