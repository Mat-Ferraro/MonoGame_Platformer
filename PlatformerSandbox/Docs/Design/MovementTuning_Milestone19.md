# Milestone 19 — Movement feel tuning and observational diagnostics

## Purpose

Compare and edit designer-facing **outcome constraints** rather than introducing
arbitrary instantaneous velocity changes. The default gym tuning is identical
to Milestone 18; existing gameplay remains unchanged until a preset or edit is
selected. No changes are made to the jump trajectory, collisions, buffering,
coyote time, or simulation's single authoritative motion owner.

## Controls

| Input | Behavior |
|---|---|
| F1 | Baseline (original Milestone 18 values) |
| F2 | Responsive comparison preset |
| F3 | Snappy comparison preset |
| Tab | Select the next editable parameter (cycles eight) |
| Minus / Plus (`-`, `=` with shift, numpad keys where available) | Decrease / increase selected parameter |
| R | Reset character but retain the active tuning |
| A/D, arrows, Space, P, Esc | Existing movement/jump/pause/exit behavior |

The window title reports the active preset, movement mode, position and
velocity, free-braking distance and time, and the selected exact value. A title
labeled `Custom` means at least one manual edit was applied. When the requested
edit would be invalid, it is ignored and the current tuning is preserved.

Tab order: ground max speed, ground acceleration time, ground stopping time,
ground reversal time, air max speed, air acceleration time, air stopping time,
air reversal time. The initial selection is **ground stopping time** so it is
easy to address perceived excess momentum.

Presets are comparison examples, not endorsed defaults. All preserve the same
9 world-units/second maximum speed; only durations change.

| Constraint | Baseline ground | Responsive ground | Snappy ground |
|---|---:|---:|---:|
| Acceleration time (s) | 0.18 | 0.14 | 0.10 |
| Stopping time (s) | 0.13 | 0.09 | 0.065 |
| Reversal time (s) | 0.38 | 0.30 | 0.23 |

| Constraint | Baseline air | Responsive air | Snappy air |
|---|---:|---:|---:|
| Acceleration time (s) | 0.22 | 0.16 | 0.12 |
| Stopping time (s) | 0.16 | 0.12 | 0.085 |
| Reversal time (s) | 0.43 | 0.34 | 0.27 |

## Applying a tuning change

1. Validate both ground and air constraints with the existing
   `HorizontalMotionSolver`.
2. Construct a replacement motor at the fixed spawn location with the newly
   solved immutable movement profiles.
3. Commit the profiles and motor atomically and clear pending jump inputs.

This deliberate reset avoids a hidden discontinuity in acceleration when
editing the active motor. The existing `R` reset retains the chosen tuning.
Jump constraints and collision geometry remain unchanged.

## Diagnostic interpretation

- `free-stop`: theoretical **unobstructed horizontal stopping distance** for
  the currently active ground/air motion profile, if horizontal input becomes
  neutral immediately.
- `free-stop` time: corresponding duration to reach horizontal velocity zero.
- These quantities do not account for walls, platform edges, vertical motion,
  or subsequent input changes. They are read-only diagnostics, not controls.

## Architectural ownership

- `MovementGymTuning` is gameplay-owned test-gym configuration.
- `HorizontalMotionSolver` continues deriving physical acceleration rates.
- `MovementGymSession` owns applying settings and authoritative reset.
- `Game1` only maps device keys to tuning edits, controls, and presentation.
- No MonoGame dependencies enter Gameplay or Simulation.

## Tests

Tests verify original default values, valid presets, responsive braking,
invalid edit rejection, atomic invalid-configuration behavior, reset behavior,
input-buffer reset, deterministic same-configuration playback, and read-only
braking diagnostics.

## Deferred

Persisting tuning to versioned JSON, an in-game text/slider editor, controller
support, and a camera/trajectory graph overlay. These can build on the data
interface after choosing which motion-feel behavior is preferred.
