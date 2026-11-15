# Milestone 25 follow-up — descending elevator side contact + combined gym

## Observed failure

Walking into the side of the **descending** elevator while standing on a **static** floor caused an `InvalidOperationException` at the controller's end-of-step overlap invariant. The measured overlap in the debugger was approximately 0.014 world units in X. This is a real collision error, **not** sub-ULP alignment noise.

## Root cause

The old `StepFreeOrGround` path invoked the shared static/moving stepper only when `grounded == false`; the `grounded == true` branch queried static colliders only. A moving side wall could therefore advance into a character walking on a static floor without being included in the contact search.

## Fix

- All free movement — including grounded-on-static movement — arbitrates static and moving contacts in the same continuous analytical query. The collision response is resolved at the first impact, not patched after a deep overlap.
- Preserve actual static floor support when a moving **side** contact or a bounded side overlap correction occurs.
- Accept multiple moving collision candidates in the headless controller and jump motor. At most one moving floor is the authoritative support per step. Moving collider IDs must be unique across both collider sets.
- Preserve the strict final overlap check (`1e-9` remains a **precision-only** correction). No arbitrary depenetration or exception swallowing has been introduced.

## Playable gym

- The default level (`MovingPlatformGymMode.Both`) includes two independently moving fixtures at once: horizontal platform (ID 7) in the original right pit and vertical lift (ID 8) in a new left pit, x = -8.4 to -5.4. A separate static floor section (ID 9) supports the original player spawn at x = 0.
- Both platforms share the same authoritative motor simulation clock, and both are drawn every frame.
- F8 has been removed. The level does **not** switch modes during gameplay. Older single-platform mode selectors remain in the headless fixture API for isolated tests.
- All jump, tuning, reset, and fault-sensitivity controls otherwise remain unchanged.

## Verification checklist

1. Build the full solution and run the entire xUnit suite, including `DescendingElevatorSideContactTests` and `BothPlatformsGymTests`.
2. In the default gym, move **left** toward the lift without pressing F8. Walk into its side while descending; its collider must stop inward penetration without a fault.
3. Jump onto the lift; ride through up/down turnarounds, jump from it, and walk off it.
4. Move **right** to the existing horizontal platform and confirm unchanged riding/jumping behavior.
5. Use Strict fault mode (F5) for these checks. A ceiling/lift crushing event, simultaneous multi-support resolution, and platform/platform obstruction still belong to Milestone 26.

## Integration scope

This package is intentionally a targeted collision fix and gym-layout improvement. It does not claim to solve opposing normals, crushing decisions, or platform-platform interactions. Full .NET compilation and runtime verification must occur in the user's Visual Studio environment.
