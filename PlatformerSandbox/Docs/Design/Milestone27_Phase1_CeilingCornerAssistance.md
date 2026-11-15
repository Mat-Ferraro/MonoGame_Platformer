# Milestone 27 — Phase 1: Optional, bounded ceiling-corner assistance

## Status
Implementation candidate built against the **working Milestone 26 Phase 2C** source. The archive was statically checked but cannot be treated as compiled or tested until the solution and full xUnit suite pass in Visual Studio.

## Intent and ownership
Intentional gameplay assistance is not floating-point precision repair and is not a simulation fault recovery. It must be explicitly configured and be observable. The engine default is **disabled**, preserving existing physics and tests. The gym offers **F9** to enable or disable a 0.18-world-unit ceiling-corner assist with up to two assists per jump; toggling resets the current run.

The pure `CeilingCornerAssistPlanner` is called only after the unified analytical collision scheduler finds exactly **one** static ceiling hit with the player rising. The planner accepts the already verified impact, aligns the contacted underside within the preexisting 1e-8 analytical impact-face budget, and looks for the smallest permitted horizontal exit along the underside:

- Left input permits only leftward correction; right input permits only rightward correction.
- Neutral input chooses the smaller legal correction; exact ties prefer left.
- Correction must be positive, representable, and no larger than the configured assistance bound.
- The full horizontal correction-path envelope must not cross any other static or moving collider at the authoritative impact time.
- Moving ceilings, simultaneous contact sets, downward motion, zero correction, and invalid geometry receive no assistance.
- Assistance happens at impact time, not after a frame delay. The jump's exact vertical reference and instantaneous velocity are kept intact. The game motor still owns all position writes and commits only on a fully successful update.
- The per-jump correction count is bounded, independent of collision event budget. `CollisionAssistEvent` reports the collider ID, world timestamp, and actual horizontal displacement; it is not logged as a physical contact or numerical recovery.

## Files

Replacements:
- `PlatformerSandbox.Simulation/CharacterMotor/{CharacterMovementController,CharacterControlStepResult,SteerableJumpMotor,SteerableJumpStepResult}.cs`
- `PlatformerSandbox.Gameplay/Movement/Gyms/MovementGymSession.cs`
- `Source/{Game1.cs,Gyms/MovementGymRenderer.cs}`

Additions:
- `PlatformerSandbox.Simulation/CharacterMotor/{CollisionAssistancePolicy,CollisionAssistEvent,CeilingCornerAssistPlanner}.cs`
- `PlatformerSandbox.Tests/Simulation/CharacterMotor/{Milestone27CeilingCornerAssistTests,Milestone27ControllerAssistTests}.cs`

## Limitations and next phases

This phase deliberately **does not** add ledge snapping, step-up, floor-edge forgiveness, wall boosting, corner correction at moving-platform undersides, or assistance across simultaneous contact manifolds. Static ceiling-corner assist is designed to be conservative and is bypassed when safe correction cannot be proven.

Future Milestone 27 phases should cover grounded micro-step/ledge assistance and corner snag prevention with independently validated bounds and scenario tests; each requires explicit play-feel policies. Avoid silently broadening geometry epsilons or weakening crush protections.

## Verification

1. Build the existing six-project solution; run all xUnit tests (including both new test classes).
2. Verify F9 toggles and resets while retaining tuning, fault sensitivity and level geometry.
3. Verify a jump that grazes a static ceiling corner with assist off blocks normally, but assist on preserves the rising reference when the requested escape is within 0.18 units.
4. Verify a head-on ceiling impact, moving-ceiling impact and simultaneous corner manifold are never assisted.
5. Re-run the four M26 crush scenarios, elevator corner, wall pin, 10,000-frame stress and deterministic replay tests in Strict mode. Any preexisting failure requires investigation, not relaxed event limits or larger tolerances.
