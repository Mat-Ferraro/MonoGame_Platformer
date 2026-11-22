# L03.1 — Player-following Pulse light

## Symptom

Pulse activated near a crate, then the player moved past/overlapped the crate.
The cyan flash stayed at the activation location for its lifetime, so it looked
like a light behind the sprite, even though the character had moved to its front.

## Root cause and correction

`GameplayLightingPresenter.Present` previously created a static `RadialLight`
from the committed activation event position. Other cosmetic effects already
follow their world owner; Pulse's dynamic light did not.

The Pulse flash now retains a player ID and is repositioned after *each*
successful committed `WorldSession.Step` using the corresponding player's
`WorldStepResult.Players` snapshot. Landing and respawn flashes remain static.
Stale player-attached flashes are removed on removal, respawn, missing player,
world replacement, and lifetime expiry. No gameplay, motion or collision code
was changed.

The existing front-facing sprite pass still lights a silhouette crate without
receiving blackness from its own blocker. The world/backdrop pass still applies
the crate's occlusion mask, so a flash in/near the crate does not shine through
its back wall.

## Important remaining scope distinction

This correction synchronizes an animated, player-owned light's world position.
It **does not** introduce genuine 2.5D Z coordinates or per-pixel GPU sprite
surface shading. A player and a crate can occupy the same world XY region but
different painted depth planes; full depth-aware shading is still a later
renderer feature. The current crate front-face tint is center-sampled.

## Test

1. Build and run the entire automated suite, especially
   `L031DashLightFollowsCommittedPlayerTests`.
2. Launch `--dev-level=LIGHTING_FLAT_TEST` and enter Dramatic lighting.
3. Pulse on approach to each crate, then alongside and overlapping each one.
   The blue flash should advance with the player rather than remaining where
   Pulse started. The dark crate should receive brief front-face reveal.
4. Compare environment shadows while the light moves: a crate still blocks
   illumination on the far side, and other authored lights retain their masks.
5. Repeat while moving left and right; normal lamps and unlit decorations must
   remain unchanged.
