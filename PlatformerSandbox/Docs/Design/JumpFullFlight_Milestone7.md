# Milestone 7 — Complete analytical vertical jump reference flight

Architecture: A06 (designer constraints), A07 (analytical motion), A14/A20 (headless, engine-independent tests).

## New APIs (additive)

- `JumpFlightSolver.ForHeldJump(JumpTrajectory)` wraps the Milestone 5 held reference curve without changing it.
- `JumpFlightSolver.ForEarlyRelease(JumpTrajectory, JumpReleaseConstraints, releaseTimeSinceTakeoffSeconds, JumpDescentConstraints)` composes:
  1. held ascent from takeoff until release;
  2. Milestone 6's analytically cut ascent from release until the cut apex;
  3. analytical fall from the cut apex to **original takeoff height**.
- `JumpFlightTrajectory.Evaluate(t)` returns a `JumpSample` of displacement, velocity and acceleration. `t` is **seconds since takeoff** for both held and cut flights; position is upward-positive relative to takeoff.
- Properties expose release time, apex time, peak height, descent duration, and full reference flight duration.

## How cut descent is defined

Let the held jump's height be H, fall time Tf, and exponent p>=2. Let the cut jump's peak be Hc.

For elapsed descent time s after the **cut apex**, define:

    y(s) = Hc * [1 - (s / Tc)^p]
    v(s) = -Hc * p / Tc * (s / Tc)^(p-1)
    a(s) = -Hc * p * (p-1) / Tc^2 * (s / Tc)^(p-2)

`Tc` is selected by a designer-facing `JumpDescentConstraints` policy:

1. **PreserveReferenceFallLaw** (default): `Tc = Tf * (Hc/H)^(1/p)`.
   Consequently `Hc/Tc^p = H/Tf^p`: the entire displacement-versus-time falling law, and its derivatives, are preserved across different peak heights. For p=2, the exact downward gravitational acceleration is unchanged.
2. **SpecifyDescentDuration**: designer supplies a positive, finite `Tc` directly. The same power-curve family is used but its descent dynamics change to achieve that duration.

Neither setting promises a landing, floor contact, or a jump cut that ignores geometry. `y(Tc)=0` means only that the *unobstructed reference curve* reaches launch height. The returned velocity is downward, not zero. Actual runtime motion can diverge after collisions or other abilities.

## Event continuity and deliberate discontinuities

- At release: vertical displacement and velocity are exactly preserved from the held curve to the cut-ascent curve. Acceleration changes intentionally.
- At the cut apex: displacement matches the solved cut peak and velocity equals zero. Fall begins from the same position/velocity; acceleration may change intentionally.
- Time since takeoff is the public evaluation axis, unlike Milestone 6's `JumpReleaseTrajectory.Evaluate(s)` which uses *time since release*.
- The profile is defined only for `0 <= t <= TotalDurationSeconds`. It does not extrapolate an implicit ground collision beyond the reference return time.
- For a held jump, `JumpFlightTrajectory` delegates directly to the Milestone 5 trajectory and therefore preserves its exact existing phase-boundary behavior.

## Scope limitations

This is a **reference-trajectory composition**, not a live jump controller or full game motor. It does not implement:

- key/button sampling, input buffering, coyote time or air-jump resources;
- arbitrary midair external accelerations, knockback, dashes or moving platforms;
- collision detection/response, ledge assistance or landing;
- camera/animation, runtime profile replacement, terminal fall speed, or horizontal travel distance.

Releasing at or after the full-jump apex is rejected by the Milestone 6 solver. Runtime input handling may treat that event as a no-op instead of constructing a cut flight.

## Installation and verification

Extract this ZIP at the **existing PlatformerSandbox solution root**. These are new C# files and documentation only. No existing file, NuGet package, solution or `.csproj` is changed. Rebuild Solution, then run all xUnit tests. The existing Milestone 4–6 tests should remain green. Save a Git commit after verifying.
