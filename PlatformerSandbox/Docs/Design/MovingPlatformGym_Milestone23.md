# Milestone 23 — Moving platform in the playable gym

This milestone connects the existing analytical moving-floor solvers to the same authoritative `CharacterMovementController` used by the MonoGame movement gym. The renderer reads the platform sample and never affects simulation state.

## Play test

- Press F5 and go toward the **gold horizontal platform** above the gap (world X about 5–10).
- Jump **onto its top from the side**; it is solid, so hitting its underside is a ceiling collision.
- Release A/D while on top to ride the platform; observe its direction reversal.
- Walk along it, steer and jump off, then try landing on it again.
- R resets both the character and the platform clock. P pauses both. F1–F3 movement tuning still works.
- The fourth top-left status square turns gold when the platform supports the character.

## Scope and ownership

- Platform ID 7: analytical horizontal ping-pong trajectory, no transform parenting.
- The character motor retains exclusive authority over body bounds and velocity.
- A moving-floor landing is a relative collision; riding adds platform displacement exactly once.
- Takeoff inherits current platform world X velocity once, then ordinary air steering applies.
- A moving-platform reversal and relative X turning point split the analytical query.
- The existing static collider world and existing gameplay buffering remain separate.

**Scope limit:** This first playable integration supports exactly one horizontally moving, solid platform whose full path does not intersect static scenery. It does not yet support vertical lifts, crushing, multiple simultaneous moving contacts, or general arbitration between moving walls and static surfaces. The demo is for validating player-facing riding/landing and takeoff before generalizing the scheduler.

Build the solution, run all tests, then test in the actual game; xUnit cannot verify movement feel. No NuGet, csproj, or Content Pipeline edits are required.
