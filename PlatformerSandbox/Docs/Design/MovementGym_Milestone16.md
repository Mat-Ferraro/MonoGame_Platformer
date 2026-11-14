# Milestone 16 — Playable Movement Gym

## Goal

Make Milestones 4–15 visible and playable without adding a graphics dependency to
Foundation, Simulation, Gameplay, or the headless xUnit tests.

## Installation

Unzip at the existing `PlatformerSandbox` solution root. **Replace** the
existing template `Source/Game1.cs`; the other four files are new.
No `.csproj`, `.sln`, `.mgcb`, NuGet, or asset changes are necessary. The
DesktopGL project already compiles `Source/**/*.cs` and already references
Foundation, Simulation, and Gameplay.

Run `PlatformerSandbox.DesktopGL` as the startup project (F5). This is a test gym,
not a complete game or level editor.

## Controls

| Key | Action |
|---|---|
| A / Left Arrow | Move left |
| D / Right Arrow | Move right |
| Space (new press) | Jump if grounded |
| R | Reset to spawn |
| P | Pause/unpause |
| Esc | Quit |

Holding both directions gives neutral input. Space is **edge triggered**;
release and press it again for the next jump. Current simulation state and
controls are shown in the **window title bar** so no SpriteFont asset is needed.

## What to try

1. Accelerate right; release movement and observe braking.
2. Press left while running right and observe reversal.
3. Jump in place, and jump while running to inherit horizontal velocity.
4. Jump across the gap from x=6 to x=8; falling well below the level respawns.
5. Land on the raised platform (x=11–16, top y=2.5).
6. Hit the wall at x=22 and ceiling at x=-5 to -2.5.
7. Repeat jumping after landing; use R to reset.

## Architecture

- `Gameplay/Movement/Gyms/MovementGymSession.cs` owns the static test stage,
  tuning values, reset/respawn rules, and authoritative movement controller.
- `Source/Game1.cs` is the DesktopGL host: maps keyboard state to discrete
  actions and calls `Step(1/60, ...)` once per MonoGame fixed update.
- `Source/Gyms/MovementGymRenderer.cs` converts positive-Y-up world AABBs into
  screen rectangles and draws them using a generated one-pixel texture.
- Tests run against Gameplay/Simulation alone; no MonoGame reference required.
- The camera follows the character *only for rendering*. It does not change
  the simulation.

This fixture currently uses a 9-units/sec running speed, 0.18s ground ramp,
0.13s ground stop, 0.38s ground reverse, 3.7-unit jump height, 0.38s rise,
0.34s fall, 0.32s in the top 10% of jump height, and -24 units/s² free gravity.
These values are test-fixture settings, not established engine defaults.

## Known limitations (intentional)

- **Midair changes of direction are not applied during an active jump.** The
  reference horizontal direction is selected at takeoff. That is the current
  Milestone 15 controller behavior, not an input/rendering problem.
- Early jump-button release is not yet integrated with the runtime controller.
- Ground support can be recognized one fixed update after leaving a ledge.
- The first gym uses static axis-aligned geometry and no moving platforms.
- No HUD font, texture assets, content registrations, or editor dependencies.

Next: gameplay input mapping, buffered actions, midair direction changes,
early-release integration, and event-accurate ground support transitions.
