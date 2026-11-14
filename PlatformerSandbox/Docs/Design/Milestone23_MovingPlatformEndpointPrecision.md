# Milestone 23: moving-platform side-contact endpoint correction

## Symptom
The `RepeatedSixtyHertzUpdatesDoNotCreatePenetrationAfterSideContact` regression can fail even when the platform and character are physically just touching. Platform world position is evaluated from absolute time; character motion is accumulated through multiple segments. A few floating-point ULPs of discrepancy can become a strictly positive AABB overlap **at the end** of an update.

## Fix
Inside `CharacterMovementController.StepFreeOrGround`, before publishing the next state, check for a moving-platform overlap when the player is not already riding it. Only if the minimum separation distance is at most **1e-9 world units**, use the existing explicit `MovingPlatformOverlapResolver` to align the character to the platform face and resolve any inward relative velocity. A true overlap larger than this numerical budget causes an exception rather than being concealed.

This does not alter the collision detector, the platform trajectory, the larger 0.125-unit emergency recovery policy, the designer's jump trajectories, or the controls. The existing regression test is intentionally kept strict (zero positive overlap after every 1/60-second step).

## Verification
Rebuild and run all tests. Particularly check `MovingPlatformCornerContactTests.RepeatedSixtyHertzUpdatesDoNotCreatePenetrationAfterSideContact`. Then ride, jump against, and land near the corner of the platform in the gym. A passing C# build and xUnit run still need to be confirmed in Visual Studio.
