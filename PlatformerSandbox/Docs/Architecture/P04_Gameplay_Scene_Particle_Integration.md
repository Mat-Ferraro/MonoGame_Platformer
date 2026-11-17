# P04 — Gameplay, Scene and UI Particle Integration

P04 layers the validated P01/P02/P03 particle engine over the real game. It
adds a headless semantic event presenter, authorable mapping definitions,
per-player/scene/UI lifetime scopes and composited rendering hooks in our LDtk
and shared-world test renderers. **No authoritative gameplay, movement, motor,
collision, world scheduler or diagnostics files are changed.**

## Runtime contracts

1. The host calls `ParticleEffectPresenter.PresentCommitted(WorldStepResult)`
   **after a successful `WorldSession.Step()`** and after diagnostics commit.
   No raw key/button press ever triggers a gameplay burst.
2. The adapter consumes the committed event's `WorldInstanceId`, `WorldTick`,
   `Sequence`, `PlayerId`, `Position` and ability type. It rejects stale worlds,
   irrelevant/denied events and repeated world/tick/sequence keys (bounded
   1,024-key rolling deduplication).
3. `Pulse` (accepted `AbilityActivated`) creates a bounded continuous trail.
   Each later *committed* player snapshot moves its source; existing particles
   do **not** teleport. Completed/cancelled/interrupted Pulse stops emission.
4. Respawn clears only the affected player's runtime and trail, then starts
   their respawn burst. Removing a player likewise clears only their effects.
   Scene replacement, restart, level exit and return to title discard all
   world/player/scene effects. Application-owned UI effects survive a scene
   switch and expire normally.
5. The P04 world clock uses presentation `dt`, bounded by P03; it is frozen
   when `GameFlowController.IsGameplaySuspended`. Screen-space UI particles
   advance with the independent presentation clock during pauses/menus.
6. With `--no-particles` the host never constructs or advances P04 particle
   runtime, allowing an A/B comparison against the identical authoritative
   world trajectory. No gameplay assembly depends on MonoGame.
7. The renderer shares P02 procedural sprite textures, and draws background
   after scenery BG but before terrain, world effects before players,
   foreground after players but before FG artwork, and UI effects above menus
   but below the transition curtain. The `ParticleViewportAdapters` convert
   the *clamped* LDtk camera or graybox projector exactly.

## Authoring

- `Content/Particles/P04_GameplayEffects.json` — independent versioned catalog
  reusing all P03 recipes and introducing P04 gameplay and low-opacity scene
  presets. Future replacements may tune count, size, color, delay, layer and
  blend mode without modifying event presentation code.
- `Content/Particles/P04_EventBindings.json` — links semantic gameplay/UI cues
  (`jumpStarted`, `landed`, `wallHit`, `pulseActivated`, `playerRespawned`,
  `sceneAmbient`, `uiConfirm`, `uiBack`, `uiNavigate`) to named effects.
  Unknown IDs, wrong coordinate spaces or unsupported manifest versions
  fail validation up front. Empty binding IDs disable individual cues.
- Both assets use P02's existing `Content/Particles/*.json` copy rule from the
  DesktopGL `.csproj`. They do not require MGCB/XNB import.
- LDtk levels use a *temporary* camera/player-followed, low-opacity scene
  haze during this milestone. Level-placed authorable emitters are P05.

## Expected visible effects

| Action | Result |
| --- | --- |
| Start Cavern A/B/C | Subtle floating scene particles around the player |
| Jump successfully | Small dust burst below the character |
| Land after a jump/fall | Composite dust, flecks and delayed smoke at feet |
| Hit a wall | Short spark/chip burst at the committed position |
| Press B when Pulse is ready | Blue motion trail, moving with player |
| Pulse ends or H cancels | Stops adding trail particles; existing trail fades |
| Press R for respawn | Old player VFX disappears; respawn burst appears |
| Open/pause/confirm/back/menu navigation | Small UI sparkles, screen-relative |
| Transition, restart, title | Old world haze/trails are removed; UI remains independent |
| Pause | World particles freeze; menu/UI particles continue |

Controls are **unchanged**. The new particle system does not consume new input.
`--dev-particles` continues to launch the P02/P03 isolated playground.

## Build and acceptance

```powershell
# Extract the ZIP into the repository root, overwrite the three existing source files.
dotnet build --configuration Debug -m:1
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~P04GameplayParticlePresenterTests
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj

# Standard game with active VFX
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=CAVERN_C

# Same gameplay with P04 cosmetic system disabled
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=CAVERN_C --no-particles

# Graybox two-player regression / isolation fixture
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=SHARED_WORLD

# Existing isolated P02/P03 authoring playground
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-particles
```

Check successful and rejected actions (B cooldown), jump once, land once,
wall-impact once, trail stopping when Pulse ends, repeated R then T restarts,
pause/resume, title/menu, long-running atmosphere, and scene transitions
A→B→C. In the two-player gym, R resets P1 but keeps P2's effect; U resets P2.

The Full debug overlay displays `P04 VFX WORLD / UI / DROPPED`; newly
installed manifest/renderer failures are shown as a P04 warning rather than
preventing gameplay startup. The --no-particles flag removes that overhead.

## P04 limits / next phase

- World position is from end-of-tick events, not exact time-of-impact geometry.
- Landing intensity is available as event metadata, but distinct light/heavy
  recipes and player directionality are deferred to P05 tuning.
- Scene haze currently follows P1, not an LDtk-authored fixed emitter. P05
  introduces level placements and rapid effect iteration.
- Pool/draw-source reuse is still preliminary. P06 measures actual draw calls,
  frame time, allocation and caps before considering any optimization.
- No new authoritative particle collision or gameplay effects.
