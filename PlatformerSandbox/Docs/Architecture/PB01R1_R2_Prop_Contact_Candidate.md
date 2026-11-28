# PB01-R1/R2 — Translational prop contact candidate

## Scope and baseline

Apply after PB01F-P consolidated partial candidate and PB01 Micro-Bounce Fix. This delivery includes the latter's full modified dynamic solver and tests. It adds a runtime shared event loop for dynamic AABBs and static terrain. It does not integrate kinematic character motors or prescribed moving platforms into the event loop. PB01 remains open. The existing character motor is unchanged.

## Solver contract

- Explicit mutual collision masks: `Terrain, Dynamic` opts authored props into dynamic contact; `Terrain` alone retains the original terrain-only path if no object requests dynamic collision. Character/platform masks are rejected, not silently accepted.
- Stable local IDs and typed pair keys. Eligible prop pairs use relative linear CCD. Terrain uses the existing indexed/reference/verify sweep. Every participating prop advances to the same earliest event. Acceleration is semi-implicit as in PB01D-F.
- Pair impulse uses inverse mass, combined restitution and friction. Low impact speeds and acceleration-only support impulses use the micro-bounce cutoff. Remaining contact constraints are conservatively closed with mass-weighted normal velocity groups and fixed zero-velocity anchors. Group merges are deterministic and bounded. This is an inelastic feasibility closure, not an exact simultaneous impulse optimization; complex branching stacks may be more constrained than an ideal rigid-body solver. Friction is applied at selected impacts, not a full persistent manifold friction solver.
- Positive-area overlaps are disallowed for enabled pairs. A 1e-8 world-unit face proximity allowance and bounded numerical time backoff address rounding. A missed impact or exhausted 512-event budget raises a genuine simulation fault; no silent penetration or discarded motion. Failed advances do not commit dynamic state, pending force/impulse consumption or counters.
- Pair scans and active face discovery are exhaustive for dynamic pairs; terrain sweeps retain PO05. No angular dynamics, sleeping, rotational contact or arbitrary-shape collisions. Max speed is applied at the beginning of the tick as before; impacts can transfer additional speed.

## Developer controls and fixtures

Physics Interaction Lab: original 5 props plus a pair of stacked wood crates (6/7) and adjacent crate (8) at x=16.5/15.5, floor y=4. Engine Integration Lab: original 8 props plus stack 9/10 and adjacent 11 near x=46.5, floor y=18. All authored props opt into dynamic contacts. Spawned crates opt in as well. Existing level geometry is unchanged.

Controls: SELECT BODY, IMPULSE UP/LEFT/RIGHT, FORCE NEXT TICK, PROP COLLISION (independent of TERRAIN COLLISION), PROP PAIR ID, IGNORE PROP PAIR, PROP PAIR IMPACTS, LARGEST CONTACT ISLAND. Invalid overlapping pair restoration is rejected; remove clears exclusions. Reset Bodies restores authored fixtures. The geometric contact observation remains a separate diagnostic, not an active-constraint visualization.

Manual: select 6 in Physics Interaction Lab and apply upward impulse; 7 should move in response. Select 8 and apply rightward impulse; 6 should be displaced if friction permits (use Ice or repeated impulses if needed). Observe both settling, no penetration or persistent jitter. Apply impulse to red rubber body 2 and let it settle. Test pair exclusion and safe restoration. Repeat integrated stack 9/10 while lighting, particles, animation and platform controls are active. Player still passes through props and cannot ride them. Do not mistake this for a new regression.

## Automated verification

Run targeted `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj -c Release --filter "FullyQualifiedName~PB01CoupledPropTests|FullyQualifiedName~PB01DynamicBodyTests"`, then `py .\Tools\pb01_validate.py --tier quick`.

New tests cover relative high-speed impacts, mass transfer, isolated restitution, 2/10/50-body resting stacks, chain blocking, pair exclusions and invalid restoration, registration order and spatial mode agreement, atomic failed integration and low-speed bounce. Graphical captures target the new stack at tick 60 and require prop impact evidence. Benchmark workload version 3 applies periodic upward impulses to stack 9, checks positive prop-pair impact count over the same absolute tick interval, and records it in results. Do not directly compare FPS with workload version 2. Previous tests still run; full acceptance and real C# execution are pending.

## Outstanding PB01

R1: generalize chronological event scheduling to the existing curved/analytical character motion and runtime platform motion with transactional replay. R2: character-to-prop, character-to-character, NPC participation, platform transport and player support. R3: actual compression constraints and response dispatch (death/destroy/ghost/stop/reverse), policy inheritance. R4: LDtk entity definitions, prop shadows, active constraint overlays, broader integration fixtures and final automated/manual acceptance. PB02 angular physics remains planned. No per-level exception handlers or dynamic mass integration for the player.
