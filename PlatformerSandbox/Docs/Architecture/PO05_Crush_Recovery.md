# PO05 runtime crush recovery

## Root cause
A legitimate CharacterCrushException escaped the shared WorldSession step. The original single-character MovementGymSession already handles that classified outcome separately from numerical faults. The reported LDtk lab incident identified moving collider 8002, opposing collider 18732, vertical axis, world time 439.51666666666665. The lab's authored starting spawn is distant from both moving platforms.

## Contract
- WorldSession constructed from WorldDefinition defaults to RespawnAtNamedSpawn. Explicit Throw remains available for headless physics diagnosis; raw geometry worlds without named spawns retain Throw.
- Only CharacterCrushException from the staged motor step is handled. Other errors retain the existing policy and abort the transaction.
- The victim's attempted per-player events are discarded. A fresh motor is constructed at the END of the tick using the current checkpoint, then original named spawn if the checkpoint is occupied. Respawn validates live geometry and resets jump buffer, state machine, abilities, motion and contact history. The player identity and slot are preserved.
- Other players continue the same tick. Platform time is not reset. The respawn is staged until the entire world tick commits.
- The committed PlayerCrushed event carries the original classified body, axis, collider IDs and time; PlayerRespawned reuses existing presentation. Previous trigger exits are emitted in sorted order; destination trigger entries are checked on the following tick, consistent with manual respawn reset semantics. The host clears stale wind membership on respawn.
- If neither the selected checkpoint nor initial named spawn is valid at the end of the tick, fail explicitly with the original crush as inner exception. This is an invalid authored respawn configuration, not silently recoverable. No arbitrary exit selection or unchecked teleport is introduced.
- No motor, narrowphase, spatial query algorithm, GPU feature or numerical fault policy is modified.

## Validation
New WorldCrushRespawnTests exercise 60 Hz vertical/horizontal crushes under Reference/Indexed/Verify, all three numerical fault sensitivity settings, second-player continuity, committed event IDs/order, trigger lifecycle, spatial snapshot cleanup, safe checkpoint fallback, end-of-tick anchored spawn, explicit Throw and failed-respawn rollback, and unrelated validation exceptions.

Run from the solution root:

```
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj -c Release --filter "FullyQualifiedName~WorldCrushRespawnTests"
py .\Tools\po05_validate.py --tier quick
```

Manual: repeat the reported crush under the descending platform in Engine Scalability Lab. Verify one respawn, continuing platform movement, normal input, no unhandled exception, and repeat in Reference, Indexed and Verify modes. A debugger configured to break on thrown exceptions may still stop on the internally handled exception; Continue must reach the respawn. The intentionally explicit Throw policy is for tests, not the authored lab.

## Limitations
C# compilation and runtime tests could not be run in the patch preparation environment. No claim of a passing build or runtime test is made. Raw unnamed worlds still require their own configured spawn policy if crush hazards are later added. No full death animation/lives system is introduced.
