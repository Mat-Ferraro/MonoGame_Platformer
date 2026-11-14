# Milestone 6 — Mathematically controlled early-release jump ascent

This implementation extends A06/A07 **without altering** the Milestone 5 held-jump trajectory or adding gameplay/motor dependencies.

## Designer control

`JumpReleaseConstraints.MinimumHeight` expresses the exact reference **peak height** for a button released immediately upon takeoff (t=0), not a gravity multiplier or arbitrary velocity-cut ratio. Require `0 < MinimumHeight < full jump Height`.

For a held-jump trajectory with initial upward speed `v0`, the release policy derives a constant deceleration magnitude:

`a_cut = v0^2 / (2 * MinimumHeight)`.

If release occurs at t=r while still rising, sample held-jump position `y_r` and velocity `v_r`. The release trajectory preserves both. From this point, with `s` seconds since release:

- `y(s) = y_r + v_r*s - (a_cut/2)*s^2`
- `v(s) = v_r - a_cut*s`
- `a(s) = -a_cut`
- `s_to_cut_apex = v_r/a_cut`
- `peak_height = y_r + v_r^2/(2*a_cut)`

The implementation evaluates the peak height using `y_r + v_r*s_to_cut_apex/2` for finite intermediate arithmetic. This is exact for the defined constant-deceleration segment apart from floating-point rounding.

At t=0, the cut apex is exactly `MinimumHeight`. A later release yields an intermediate peak, never above the held-jump peak for the existing p>=2 family. The cut segment is **position- and velocity-continuous** at release; the acceleration intentionally changes at that gameplay event.

## Important limits

- This package implements **only the early-release ascent**, ending at its new apex. It does not specify descent after the cut apex; that remains a gameplay rule to design. It does not change the nominal falling profile of a held jump.
- Release at or after the held apex is rejected by this solver. Gameplay may ignore such requests instead of calling the solver.
- It does not yet define input buffering, variable button-release latency, collision interruptions, coyote-time interactions, or terminal fall behavior.
- An actual game processes release at a simulation time, possibly later than t=0. The measured minimum with discrete input timing can therefore differ from the ideal mathematical minimum.
- `MinimumHeight` is not an independent guarantee when collisions, moving platforms, or other abilities intervene.
- The policy is one supported, mathematically explicit jump-cut model; other models may be added without editing the held-jump solver.

## Installation

Extract the ZIP into the existing `PlatformerSandbox` root. All files are additive and live under existing Simulation, Tests, and Docs project directories. No project references or NuGet changes required. Run Rebuild Solution and Test Explorer -> Run All Tests.
