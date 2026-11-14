# Milestone 23 — Side Contact at a Fixed-Step Boundary

## Failure investigated

`MovingPlatformCornerContactTests.RepeatedSixtyHertzUpdatesDoNotCreatePenetrationAfterSideContact`
failed on `Assert.True(sideObserved)` (line 92), **not** on the positive-overlap
assertion inside the test loop.

The existing air movement code recognized a character already touching the
approaching moving platform, matched the platform's horizontal velocity, and
suppressed acceleration into it. This prevented penetration but silently
omitted a contact event. An uninterrupted 0.10 s step could report a swept
side hit; separate 1/60 s updates could reach the same face at a step boundary
and follow the pre-contact constraint path with no published event.

## Correction

When a flush, vertically overlapping moving side wall has strictly inward
relative horizontal velocity, the existing constraint now records one
`CharacterControlContact` with the world incoming/outgoing velocity, correct
normal and offset within the current step. It respects the existing contact
event limit. Motion equations and the no-overlap assertion are unchanged.

A new test checks a flush starting contact, event timing and velocity,
non-penetration, and no duplicate event after the platform and character
already share the same horizontal velocity. The existing 120-frame test also
remains unmodified.

## Scope

The controller still does not provide a unified multi-body scheduler or full
crushing resolution. This update changes event reporting for a collision
response that was already being applied; it is not general penetration recovery.
