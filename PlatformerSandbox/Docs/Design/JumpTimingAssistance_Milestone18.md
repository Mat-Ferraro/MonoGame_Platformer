# Milestone 18 — Jump timing assistance

## Scope

Adds **coyote time** (one short opportunity to jump after walking off a
supported ledge) and **jump buffering** (remembering a jump-press edge just
before landing). Both are deterministic gameplay input policies, not forces or
adjustments to jump trajectories. The controller's running/jumping mathematical
profiles, collision detection, and tuning values are unchanged.

The playable gym uses **0.10 s** for both windows. These are illustrative
gameplay settings, not mandatory engine defaults. The gameplay-owned
`JumpTimingConstraints` accepts finite, nonnegative windows; zero disables
an assist. The `BufferedJumpInput` stores timestamped events using the
simulation clock, not wall-clock timestamps. Pauses do not age the windows.

## Ownership

- `PlatformerSandbox.Gameplay.Input.Buffering.BufferedJumpInput` plans and
  commits input decisions using fixed-step simulation time. It is independent
  of MonoGame and of collisions.
- `MovementGymSession` feeds actual pre-/post-step support state into that
  buffer. It commits a proposed input decision **only after** a successful
  authoritative movement step; a failed step does not consume a press.
- `CharacterMovementController` gets an optional `jumpGraceAuthorized`
  argument. A jump must still have a press request and no active jump. The
  game layer alone determines whether the limited grace period applies.
- `SteerableJumpMotor` continues to own the vertical analytical profile,
  air steering, early release, and collision responses. Nothing changes its
  trajectory mathematics or the previous validation tests.

## Exact timing semantics

- Inputs are observed at the **beginning** of a fixed update, using the
  authoritative `ElapsedSeconds` value. A timestamp stays valid while
  `now - timestamp <= configuredWindow` (inclusive).
- A coyote window begins at the first fixed-update boundary **after** the
  existing motor reports that ground support was lost without jumping. It
  expires naturally and is consumed on jump activation. A landing clears it.
- An airborne press is buffered even during an active jump; it may become a
  new grounded jump if the body lands within the buffer duration. A new press
  replaces the previous pending timestamp. Only an edge can create a press;
  holding Space does not repeatedly jump.
- If a buffered press is released **before** it activates, the eventual
  jump starts with the existing analytical early-release constraint. A press
  that remains held starts at full reference height.
- A buffered jump becomes eligible at the **next update** after a landing,
  not in the middle of the update that detected contact. This is at most one
  fixed tick of latency at 60 Hz. No hidden motion subdivision was added.
- Reset/respawn clears pending input and grace status. A zero-duration
  observation does not capture button edges.

## Known limitations / next work

- Ground support loss is still reported at movement boundaries by the
  Milestone 15 controller. Coyote timing consequently begins at the
  detected boundary, not the exact geometric edge-crossing instant.
- Landing and buffered jump are not performed in the same analytical
  substep; this could be improved later when the controller reports
  authoritative mid-step ledge and landing events.
- No variable coyote window by ability, animation or surface yet; future
  gameplay coordinators can use the same timestamped decision pattern.
- Input and movement tuning remain unchanged: 0.18 s ground acceleration,
  0.13 s stopping, 0.38 s reversal, with distinct air values in the gym.

## Verification

Build the entire existing Visual Studio solution and run all xUnit tests.
New tests include timestamp windows, late-press rejection, single consumption,
held vs tapped buffered jumps, coyote authorization, reset, and the real
playable-gym gap/landing scenarios. Then verify with F5: run off the edge
and quickly press Space; tap Space shortly before touching a floor; confirm
holding Space does not auto-jump on each landing.
