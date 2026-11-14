# Milestone 24 — Phase 2B: Event Requery and Live Fault Sensitivity

## Scope

This is a targeted live integration of the verified Phase 1 `UnifiedAcceleratedKinematicStepper`.

* When the character is **airborne without an active reference jump** and the gym contains a moving platform, the controller now calls a single stepper. That stepper re-queries static and moving colliders after each contact, not just once at the start.
* When that stepper identifies a valid moving-floor landing, it stops at the exact event and hands the remainder to the already-tested platform-relative ride path.
* A resting moving-floor contact (zero relative vertical speed) is recognized as support if the contact has genuine horizontal support overlap. A zero-time hit that changes neither velocity nor acceleration is explicitly rejected, preserving the event-budget guard.
* A `SimulationFaultPolicy` can be supplied to the motor and changed without resetting the motion state. Recoveries that occur within the unified airborne stepper are counted per successful update.
* The movement gym exposes **F5 Strict, F6 Diagnostic, F7 Resilient**, separately from **F1/F2/F3 movement presets**. The window title displays the selected fault mode and recovery count. The chosen fault policy survives gym tuning changes and reset.

## Deliberate limitations

* Grounded locomotion, the platform riding solver, and the nonparabolic `SteerableJumpMotor` still retain their existing collision paths. Replacing all of them requires a common motion-segment interface and a broader event-handling integration (later Phase 2C). Do not claim they use the unified scheduler yet.
* This policy does **not** silence unexpected exceptions in Release mode. Unrecoverable penetrating geometry, unsafe displacement, and repeated non-progressing contacts still fault. Safe checkpoint restore and broader transactional recovery are future work.
* The controller's normal movement phases still resolve some already-known sub-resolution timing cases using their existing exact transition logic; the new policy only controls the Phase 1 simulation service when explicitly invoked.
* Any failure in the new segment throws before the controller's fields are committed; the input buffering component also avoids committing a rejected action.

## Verification

1. Rebuild all six projects and run every xUnit test.
2. Verify the new tests for static-then-moving collision, resting floor support, invalid policy, and independent mode toggling.
3. F5/F6/F7 switch fault sensitivity; F1/F2/F3 remain movement presets.
4. Run from the platform at F1 and F3, collide with walls, land, jump, and ride across reversals. Monitor `Recoveries=` in the title.
5. If an exception occurs, preserve full simulation state and the exact contact records; do not increase event budgets as a workaround.

## Source layout

* `PlatformerSandbox.Simulation/CharacterMotor/CharacterMovementController.cs` — replacement
* `PlatformerSandbox.Simulation/CharacterMotor/UnifiedAcceleratedKinematicStepper.cs` — replacement
* `PlatformerSandbox.Gameplay/Movement/Gyms/MovementGymSession.cs` — replacement
* `Source/Game1.cs` — replacement
* `PlatformerSandbox.Tests/Simulation/CharacterMotor/Milestone24Phase2BLiveSchedulerTests.cs` — new
