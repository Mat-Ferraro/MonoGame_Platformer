# P03 — Data-Driven Composite Particle Effects

## Scope and architecture decision

P03 extends the validated P01 headless particle engine and P02 MonoGame visual
playground. It introduces authored *effects*: one named effect can combine up to eight
independent emitters, each with its own clock, start delay, duration, offset,
origin policy, particle pool, render layer, blend mode, coordinate space and sprite.

This is **Phase 3 of the approved Particle System & Visual Effects Architecture**.
Confirmed gameplay-event mapping, scene ownership, level authoring integration,
respawn/restart handling and normal-game particle rendering remain **P04**. This
pass intentionally does not modify gameplay code, world simulation or `Game1.cs`.

## Runtime structure

- `ParticleCompositeCatalog`: validates versioned JSON, reusable P01 emitter
  definitions, effect IDs, references and style/timing budgets. Unknown fields fail.
- `ParticleCompositeDefinition` / `ParticleEffectPartDefinition`: immutable authoring
  records; a part binds one emitter to its presentation metadata.
- `ParticleCompositeRuntime`: bounded manager of independent live effect instances;
  `TryPlay(effectId, origin, out instance)`, `Update(dt, worldPaused)`, `StopAll`,
  `Clear`. Capacity rejection increments `DroppedAtCapacity`.
- `ParticleCompositeInstance`: mutable origin for the active source, independent
  component clocks, `MoveSource`, `Stop` (soft), `Clear` (immediate), `IsFinished`.
- `ParticleEffectPart`: independent emitter RNG, pool, delay, and duration. A
  continuous component with duration 0 emits until explicitly stopped; a burst-only
  component completes once its particles expire.

A `Fixed` anchor emits relative to its spawn position; a `Follow` anchor emits
new particles relative to the latest `MoveSource` position. Moving the source does
not teleport particles already emitted. All coordinates are presentation-owned.

**Time semantics:** emission is bounded by the P01 limits and the P03 manager's
0.1-second update cap. A delayed burst fires once when crossing its delay.
A world pause freezes world-space components while screen-space UI continues.
An effect with both coordinate spaces is supported; each part advances on its
respective clock. Stop disables future emission while existing particles age out.

**Resource semantics:** a runtime limits concurrent effects, and each component
has a configured finite pool. Instances share renderer textures; no per-particle
GPU textures are created. P05 will measure/optimize frequent instance allocations.

## Manifest

Edit `Content/Particles/P03_CompositeEffects.json`. It is copied into DesktopGL
output by the existing P02 wildcard deployment rule.

- `version`: must equal `1`.
- `emitters`: array of validated P01 emitter presets, reusable across effects.
- `effects`: unique named recipes. Each recipe's `parts` array contains 1–8 parts.
- Part fields: `emitterId`, `offsetX`, `offsetY`, `delaySeconds` (0–10),
  `durationSeconds` (0–60; 0 is unbounded for continuous emitters), `capacity`
  (1–2048), `anchor` (`Fixed`/`Follow`), `space` (`World`/`Screen`), `layer`
  (`Background`/`World`/`Foreground`/`Ui`), `blend` (`Alpha`/`Additive`),
  `sprite` (`SoftDisc`/`SolidDisc`/`Spark`/`Square`).
- Screen-space parts must use the `Ui` layer; world-space parts cannot use `Ui`.
- Source definitions use P01's existing sizes and velocities in either world units
  or screen pixels according to their declared `space`.

Provided demo effects:

| Effect ID | Parts | Test purpose |
|---|---:|---|
| `fx.landing` | 3 | Wide dust, delayed specks, delayed smoke; layers and timings |
| `fx.impact` | 3 | Impact ring, bright sparks, delayed smoke |
| `fx.fire` | 2 | Repeating embers + rising smoke over seven seconds |
| `fx.spores` | 2 | Two independently timed follow-source emitters; eight seconds |
| `fx.uiFanfare` | 2 | Screen-space UI halo and delayed glitter |

These are temporary test effects; color and silhouettes are not final artwork.

## Local build

From the repository root:

```powershell
dotnet build --configuration Debug -m:1
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~P03CompositeEffectTests
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj
```

Launch the unchanged developer flag:

```powershell
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-particles
```

## Playground controls

| Key | Action |
|---|---|
| `1`–`5` | Original P02 single-emitter modes (unchanged) |
| `6` | Three-part landing effect |
| `7` | Three-part impact |
| `8` | Timed continuous fire and smoke |
| `9` | Timed continuous moving-source spores |
| `0` | Two-part screen-space UI fanfare |
| Left click / Space | Play selected effect at mouse / camera center |
| Right click | Original P02 UI sparkle burst |
| `F` | Toggle Follow preview: move last effect source with camera (or mouse for UI) |
| `X` | Stop last effect (remaining particles fade naturally) |
| `C` | Clear all existing single and composite particles immediately |
| `P` | Pause/unpause world effects; UI continues |
| `G` | Overload test: P02 sparks + many P03 impact requests |
| `Tab` / `B` | Original P02 single-emitter layer and blend controls |
| `WASD`, `Q/E`, `H`, `Esc` | Original P02 camera, zoom, HUD, exit |

## Acceptance criteria

1. Original P02 controls, shapes and shaders continue to behave normally.
2. `6` and `7` visibly show multiple layers/colors and delayed follow-up parts.
3. `8` emits continuous fire/smoke, automatically ceasing after its duration.
4. `9`, then `F`, then WASD: new spores move with the source; old spores do not teleport.
5. `0` emits at screen position and does not pan/zoom with the world camera.
6. `P` freezes world components while allowing UI particles to continue.
7. `G` stays responsive, shows bounded instances and increments capacity drops.
8. `C` clears all particles; `X` stops emission without destroying existing particles.
9. Complete test suite and ordinary game launch still pass, with no changes to
   gameplay, scene management, animation, audio, or diagnostics.

## Known boundaries / next pass

- P03 effects are demonstrable **only in the dedicated playground**, not yet by
  jumps, landings, dashes or LDtk emitters.
- Scene-owned effect presenter, committed-world-event deduplication, correct
  camera integration into the real LDtk renderer, and restart/respawn cleanup are
  the scope of **P04**.
- A complete editor, shader pipeline, particle-to-particle physics, GPU particle
  simulation, extensive pool reuse and profiling remain deferred.
