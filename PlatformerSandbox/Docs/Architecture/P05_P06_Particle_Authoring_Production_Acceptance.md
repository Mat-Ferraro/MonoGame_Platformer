# P05 + P06 — Particle Authoring, Bounded Runtime and Production Acceptance

## Scope and contract

This package closes the six-pass particle roadmap using the approved P01–P04
simulation, MonoGame renderer, composite recipes, gameplay event presenter and
existing LDtk importer/scene management. **Gameplay simulation, collision,
movement, camera and audio files remain unmodified.**

- **P05:** Editor-resizable `ParticleZone` entities, continuous world-space
  composite effect selection, emission-density tuning, deterministic
  proximity activation and global zone concurrency cap.
- **P06:** Headless import validation, numeric/budget/lifecycle stress tests,
  visual diagnostics and reproducible acceptance workflow.
- Collision grid and painting are independent of environmental zones; a
  particle entity is always treated as *presentation data*, never a trigger.
- `--no-particles` still provides the same authoritative gameplay with zero
  VFX runtime. `--dev-particles` still launches the P02/P03 playground.

## LDtk authoring workflow

Open `Content/Levels/ConnectedWorldDemo.ldtk` in **LDtk 1.5.3**. Use the
**Entities** layer; choose `ParticleZone` and drag a rectangle anywhere in the
level. The rectangle is an *emission region*, not a collision rectangle, so
its bounds need not align with a tile or player-path segment. There are two
examples per cavern (A/B/C), and the entity definition itself is saved inside
the `.ldtk` project so it appears in the editor's entity palette.

| Field | Units | Valid | Meaning |
| --- | --- | --- | --- |
| Position/Width/Height | LDtk pixels | inside the level, positive dimensions | Spread particles across the rectangle; imported as Y-up world-space bounds at 32 px/world unit |
| `EffectId` | effect recipe key | exact existing catalog ID | Continuous composite effect used in this area |
| `DensityPercent` | percentage | 1–200 | Multiplies authored emission rate, not physics, color, texture or duration |
| `ActivationPaddingWorld` | world units | 0–40 | Extends activation outside the rectangular bounds; 1 world unit of hysteresis helps prevent flicker |

Three presets in `Content/Particles/P04_GameplayEffects.json` are designed for
zones: `fx.zoneDust`, `fx.zoneSpores`, and `fx.zoneEmbers`. These have two
independent continuous emitter parts, each with its own blend/layer and
bounded particle capacity. Other effect IDs must also be **world-space and
continuous** for every component. Burst-only and UI-space recipes are rejected
by the environment constructor. Unknown effect names produce a visible P05
warning in the game, not a gameplay crash.

To add a new continuous recipe: reuse or define emitters in the same JSON
catalog, configure a named `effects[]` item whose every `parts[]` entry uses a
continuous emitter (`ratePerSecond > 0`) in `World` coordinate space, then
select that `EffectId` in LDtk. Set `durationSeconds=0` for indefinite
emission. No change is required to the C# LDtk entity registry.

**Distribution:** Commit both the modified `.ldtk` project and the edited JSON
catalog. The original P02 content-copy rule already deploys particle JSON;
the existing LDtk directory discovery loads `.ldtk` assets. Settings, logs,
and local save data should not be committed.

## Performance and lifecycle decisions

- The importer permits **at most 48** authored zones per level; duplicate
  instance IDs, malformed fields and out-of-level rectangles fail during
  content discovery/preparation.
- At most **6** zone effects emit simultaneously, selected by closest distance
  to their authored rectangles and stable instance-ID tie breaking. Zone
  candidate scan/storage is reusable and bounded at 48; no per-frame temporary
  candidate list allocation. Off-screen zones neither emit nor hold particles.
- Each zone's emitter-part capacity is capped at **256**, and the usual
  P01 max-spawns-per-update/time-step bounds still apply. With the supplied
  zone recipes (two parts), six simultaneous zones have no more than
  **3,072 live environmental particles** (typically fewer; authored part caps
  are between 40 and 160). Gameplay particles remain separately bounded by
  the previously approved P04 instance/pool policies.
- A zone becomes active when the committed player/camera-followed position is
  within its authored rectangle or `ActivationPaddingWorld` beyond it;
  already-active zones get one extra world unit before switching off.
- When a zone leaves range, its particles are removed immediately to reclaim
  its instance slot. Newly activated zones allocate bounded pools; active
  particles remain pooled in P01. Rapid boundary oscillation is mitigated by
  the hysteresis, not by holding out-of-range particles.
- The P03/P04 world clock is bounded to 0.1 seconds per update after stalls;
  pause freezes world emitters without advancing their lifetimes. UI particles
  still update through the independent P04 clock.
- Scene replacement, restart, or exit to title releases the old environment
  before the next scene installs its zones. In a level with no particle zones,
  the old P04 player-followed haze is retained for backwards compatibility.
- The full debug HUD displays **P05 ZONES active/available, PARTICLES active,
  CAPPED FRAMES** in addition to the original P04 counts. A nonzero capped
  frame counter indicates multiple nearby regions exceeded the six-zone
  priority budget, not a gameplay failure. `--no-particles` suppresses both.

## Automated validation

From the project root on the Windows development machine:

```powershell
# Exit every running game first; Visual Studio can lock generated DLLs.
dotnet build --configuration Debug -m:1

dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj `
  --filter FullyQualifiedName~P05P06ParticleZoneAndStressTests

dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj
```

The combined tests exercise legacy LDtk compatibility, actual A/B/C authored
zones and effect resolution, Y-up rectangle conversion, malformed fields,
duplicate entity identity, out-of-bounds rejection, off-screen quiescence,
region-wide spawning, density multiplier, paused world clocks, bounded
simultaneous zone stress over repeated frames, long-frame clamping, cleanup,
re-entry and unchanged authoritative world trajectories when cosmetics are
running. **The existing P01–P04 test suites must remain green.**

## Manual P05/P06 acceptance checklist

1. Launch the full game, not just the particle lab:
   `dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=CAVERN_A`
2. Move through the first authored region in Cavern A. Dust should appear
   *throughout the authored rectangle* rather than following the character.
   Continue to the second region to see drifting spores.
3. Enter **CAVERN_B** and **CAVERN_C** (through the real exits or `--dev-level`)
   and observe their different effect sets. Old particles must not persist
   when a new cavern activates.
4. Switch the debug HUD to **Full** (F11 or tilde in LDtk). Check active/total
   zone count and live particle count. Panning/walking far away should make
   off-screen zone counts fall; returning should reactivate nearby zones.
5. Pause (`P`), confirm world particles freeze and UI particles still animate,
   then resume. Trigger jump, land, wall, Pulse, respawn and menu effects.
6. Return to title, restart (`T`) and repeat A/B/C transitions. All old zone
   effects must be cleared, and menu audio/visuals still work.
7. Compare the same movement and collision test with the launch flag
   `--no-particles`. Player path and interactions should be unchanged.
8. Run `--dev-particles`; presets 1–0 and P03 follow/stop/stress remain intact.
9. Optional authoring smoke test: resize one zone in LDtk, change its preset,
   save, rebuild/restart, and verify the changed coverage/visual. Restore the
   repository source after this temporary edit if undesired.
10. Run a sustained playthrough and stress the shared-world gym, camera
    bounds, level transitions and diagnostics (Ctrl+Shift+F12 report export).
    No unbounded growth, persistent old-world effects, or movement regressions.

## Acceptance and known boundaries

Passing tests plus the visual checklist qualifies **P01–P06 as validated**.
Until the user performs those checks, this build is a release candidate, not
an independently verified runtime release. It uses CPU particles and SpriteBatch,
not GPU compute or custom shader effects. LDtk supports rectangular zones
rather than arbitrary polygon masks; complex visibility/occlusion,
per-particle physics collisions, arbitrary camera parallax per particle, and
production WYSIWYG particle editing remain intentionally outside this module.
The six-pass system can now be reused in another MonoGame project by taking
the framework-independent Foundation/Presentation/Particles simulation and
adapting its renderer, event mapping, and data import.
