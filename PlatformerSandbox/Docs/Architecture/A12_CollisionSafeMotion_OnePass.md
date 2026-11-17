# A12 — Collision-safe motion sources and Pulse dash

**Status:** implementation candidate; requires local VS 2022 compilation, full xUnit suite, and playable gym validation. **Prerequisite:** accepted A10 one-pass ability coordinator.

## Purpose and semantics

A08 handles **intent**. A12 adds three independent **world-space horizontal velocity contributions** that the existing authoritative motor resolves **before** collision detection:

- **Environment:** persistent signed speed, such as wind, conveyor movement or a current. Updated through `WorldMotionRequestKind.SetEnvironmentVelocity`. It is explicitly *velocity transport*, not perpetual acceleration.
- **Impulse:** a one-shot signed change to horizontal velocity through `WorldMotionRequestKind.AddHorizontalImpulse`. It retains 82% of its component each completed world tick, going to zero below 0.001 world units/s. Useful as an initial damage-knockback reference.
- **Pulse dash:** the existing A10 Pulse now provides a signed +200%-of-normal-ground-walking-speed transport component for its existing 16 active ticks. On accepted activation, it captures the requested direction or last facing. It uses existing energy, action, cooldown, cancellation and stun-interruption rules. The *total* velocity with concurrent walking or wind may be higher than 200%.

Normal ground/air locomotion still uses the previous motion profile (acceleration, reversal, stopping, and speed cap). The external contributions are added **outside** that cap, but **inside** the swept collision solver. As a conceptual steady-state example, player 100% left + environment 75% left = 175% left; player 100% right + environment 75% left = 25% right.

## Architecture / ownership

`WorldSession.Step(commands, stateRequests, abilityRequests, motionRequests)` stages motor, buffered jump, A09 states, A10 abilities and `WorldPlayerMotion` simultaneously for each player. It validates typed requests before stepping. No mutation survives a rejected world tick. Ordered committed events include `EnvironmentMotionChanged` and `HorizontalImpulseApplied`. A read-only per-player `WorldMotionSnapshot` exposes the separate components for UI and tests. Source identity is distinct from player input; no keyboard/gamepad type lives in simulation.

The motor receives a signed external horizontal speed for the fixed interval. Existing analytical grounded/static/moving-platform and airborne/jump integration use the personal locomotion profile **relative to that external speed**, while sweeping the total horizontal velocity against obstacles and moving platforms. The airborne jump motor is forked with external speed accounting, and the parent normalizes blocked external components after side-wall collisions to avoid artificial reverse movement when a source ends.

Because an impinging wind or dash can be constrained by a wall, actual physical speed is not guaranteed to equal the sum of requested speed components at a collision. External sources influence the attempted motion; contacts determine the committed motion.

## Playable gym

Press **F4**, then **F1** to enter the isolated **Motion Lab**. F1 remains the ordinary baseline preset in the *original* gym. The F2 authored-world and F4 shared-clock fixtures are otherwise retained.

The lab has two independently controlled players, one continuous floor, and static side walls. Its bottom-left HUD shows Player 1's **actual** world X velocity, environment, decaying impulse, active dash, total requested external speed, facing and ordinary walk maximum. It displays semantic events in the existing WORLD EVENTS panel.

- **Q** cycles OFF → 75% left wind → 75% right wind → OFF. Walk with and against the wind (A/D); when away from walls, the steady-state speeds may exceed ordinary walking limits. The walls are intentionally there to enforce collision on overspeed movement.
- **E** produces a synthetic damage-like knockback impulse opposite Player 1's current facing direction. Repeat after changing facing with A/D. Impulse decays naturally and remains independent of jump input.
- **B** activates Pulse as an actual dash. It retains A10's 45 energy / 16 ticks / 25-tick cooldown and respects action guards. Press **H** to cancel or **X** to stun/interupt it. The dash travels in the captured direction even if you change walking direction afterward.
- **Space** jumps with wind, dash or knockback active. **J/L/I** independently controls Player 2, which receives no wind by default. **R/U** respawn each player, **T** restarts the lab, **P** pauses. **F1** leaves lab, **F2** goes to the earlier authored-world fixture, and **F4** returns to the single-player gym.

## Test / acceptance matrix

| Scenario | Expected observable outcome |
|---|---|
| Stand still in 75% left wind | Player travels left at roughly 75% normal speed; no walking command needed |
| Walk left with 75% left wind | Steady unobstructed speed approximately **175% left**, not capped at 100% |
| Walk right against 75% left wind | Steady unobstructed speed approximately **25% right** |
| Press Q until wind OFF | Previously applied wind no longer transports P1; locomotion component remains |
| E impulse | P1 pushed opposite facing direction, then impulse contribution decays |
| B Pulse dash | Clear physical burst in captured facing direction, energy/cooldown obey A10 |
| Cancel/stun | Removes dash without cancelling wind or mutating other players |
| Collision into side wall | Total attempted movement is swept; **no wall penetration or teleport** |
| Jump during transport | Preserves jump profile and collisions while environmental X is applied |
| F4/F2 and original gym | All existing fixtures, input routing and numerical fault modes still function |

Run `WorldMotionContributionTests`, `CharacterAbilityWorldTests`, and the entire suite. The old A10 assertion that **all** abilities leave movement unchanged is intentionally narrowed to **Focus**: Pulse has been promoted to a physical dash, so testing it as motionless would contradict the newly approved behavior.

## Boundaries / intentionally deferred

- No health, damage, attack contact, or automatic `OnDamaged` producer; E is a controlled synthetic impulse test.
- Q is a controlled persistent player-specific environment setting, not a spatial trigger that turns wind on/off at zone boundaries. Integration with authored zones comes later.
- No vertical impulse, arbitrary 2D force field, configurable motion curves or charged dash yet; the first implementation is horizontal on purpose.
- No physically integrated force acceleration; *velocity contributions* and *acceleration forces* have different contracts and should not be conflated.
- All numeric behavior is subject to the user's actual .NET build/tests and additional moving-platform/crush stress cases before treating it as a verified engine foundation.
