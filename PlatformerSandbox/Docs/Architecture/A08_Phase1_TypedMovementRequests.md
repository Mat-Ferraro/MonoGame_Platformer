# A08 Phase 1 — Typed Movement Requests and Deterministic Resolution

## Purpose

Milestone 27 assistance and the collision/core motor are now validated in the
playable gym. A08 begins the **gameplay composition** phase without changing
jump physics, collision detection, buffering rules, platform trajectories, or
fault modes. Input adapters submit immutable requests; a single resolver makes
one directional choice and carries jump edges into the existing buffer.

## New headless gameplay API

- `MovementRequestKind`: Horizontal, JumpPress, JumpRelease.
- `MovementRequest`: immutable action proposal with stable `SourceId`,
  nonnegative `Priority`, and a direction for horizontal requests.
- `MovementRequestResolver.Resolve`: pure, deterministic one-pass arbitration.
- `MovementFrameIntent`: normalized direction + discrete jump press/release edges.
- `MovementGymSession.Step(duration, requests)`: typed input endpoint; the
  previous `Step(duration, direction, pressed, released)` remains source and
  behavior compatible for headless tests and replay.

### Arbitration rules

1. The highest-priority horizontal request wins. Exact priority ties use the
   smallest positive source ID, independently of input collection order.
2. An explicit Neutral request can override a lower-priority directional
   request (useful later for a control lock).
3. JumpPress and JumpRelease are independent OR-ed edge intents; existing
   `BufferedJumpInput` continues to decide coyote timing, buffering, eligibility,
   and tap/hold transitions after the resolver.
4. Reject invalid enum values, nonpositive source IDs, negative priority,
   duplicate `(SourceId,Kind)` pairs, or nonhorizontal direction carried by an
   action edge. Do not advance the motor on invalid input.
5. The resolver does not write position, velocity, collision state, or abilities.
   The existing authoritative motor still owns the single transactional step.

## Playable gym assistance default

The MonoGame host now explicitly constructs its gym session with
`MovementGymSession.PlayableAssistancePreset`. The same F9 control toggles that
preset OFF and back ON, including the 0.12-unit ground-step assistance.
`new MovementGymSession()` retains unassisted defaults for isolated regression
fixtures and deliberate baseline comparisons. No Simulation default changed.

## Intentional boundaries

- This is **A08 Phase 1**, not a complete ability system. There are no dashes,
  wall jumps, parallel ability effects, cooldowns, or cancellation semantics.
- Jump edges do not use priority to cancel one another; authorization remains
  the buffer/motor's existing policy. Ability request eligibility and typed
  interrupts belong in later A08/A09/A10 work.
- The resolver chooses a single direction, not a velocity or acceleration.
  It therefore preserves configured ground/air tuning and all Milestone 26/27
  collision/crush/assistance behavior.

## Validation

Build the six-project solution and run all xUnit tests. New tests exercise
priority, ties, ordering, bad requests, transactional rejection, keyboard/legacy
path equivalence, and assistance-preset persistence. In the playable gym check
that assistance shows ON immediately, F9 turns it off and on again, and walking
LEFT over the gold 0.10-unit bump succeeds without jumping. Check all four
crush scenarios and the existing moving-platform regressions.

The authoring environment did not have a .NET SDK; C# compilation and xUnit
execution remain required on the Windows host.
