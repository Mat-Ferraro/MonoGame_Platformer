# P01 — Reusable Particle Engine Foundations

**Project:** MonoGamePlatformer / PlatformerSandbox  
**Milestone:** Particle system, pass 1 of 6  
**Source baseline:** Approved Particle System & Visual Effects Architecture (PS-01 to PS-20), Phase 1: headless core runtime.

## What this package adds

The package contains *only additive files*: no existing gameplay, physics, movement, camera, renderer, audio, LDtk, diagnostics, or project files are replaced. SDK-style C# projects automatically include the new `.cs` files.

| File | Purpose |
|---|---|
| `PlatformerSandbox.Foundation/Presentation/Particles/ParticleConfiguration.cs` | Validated emission parameters, random ranges, colors, spawn shapes, fault policy, low-level spawn arguments |
| `.../ParticleState.cs` | Value-type particle state, normalized age, size/color interpolation, runtime counters |
| `.../ParticleSystem.cs` | Fixed-capacity particle pool, frame-bounded kinematics, stable active ordering, overflow and fault policies |
| `.../ParticleEmitter.cs` | Independent deterministic cosmetic RNG; bursts; rate-per-second emission with fractional carry and bounded catch-up |
| `.../ParticleEmitterLibrary.cs` | Optional, strict JSON loader for single-emitter presets (P03 will add multi-emitter compositions) |
| `Content/Particles/P01_TestEmitters.json` | Two editable *headless* example emitter presets |
| `PlatformerSandbox.Tests/Presentation/Particles/P01ParticleFoundationTests.cs` | Automated acceptance tests |

## Run P01 acceptance tests

From the project root (PowerShell):

```powershell
dotnet build --configuration Debug -m:1
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --no-build --filter FullyQualifiedName~P01ParticleFoundationTests
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --no-build
```

**Expected: P01 foundation tests pass, full regression suite passes, existing game works unchanged.**

**There are intentionally no visible particles in P01.** P02 attaches SpriteBatch rendering and the Particle Playground. Don't evaluate P01 by launching the game and expecting dust: this is a headless, reusable and testable core.

## Model and timing

- `ParticleEmitterDefinition` stores one validated, device-independent emitter preset.
- `ParticleEmitter` instances each own a SplitMix64 seed/stream, fractional emission accumulator, and stop/restart state.
- `ParticleSystem` preallocates arrays once. `TrySpawn` uses reusable slots. Active order remains stable after expiry/reuse (required for predictable transparent rendering later).
- Each particle has position, velocity, constant acceleration, rotation/angular velocity, age/lifetime, and start/end size and RGBA.
- Coordinates use the existing `Foundation.Geometry.Vector2D` convention: **positive Y is up**. No world or UI transform is owned here.
- Color values are **unpremultiplied [0,1] RGBA**; renderer chooses correct premultiplication/blend conventions in P02.
- Runtime is single-threaded (the presentation update thread) and owns no `GraphicsDevice`, `Texture2D` or `SpriteBatch`.

**Example update ordering:**

```csharp
// Runs in an eventual presentation adapter, NOT in authoritative gameplay:
ParticleEmitter emitter = new(system, definition, seed: 123);
emitter.Burst(worldPosition);             // one-shot, if requested
emitter.Update(worldPosition, frameDt);   // schedule continuous emissions
system.Update(frameDt);                   // advance bounded particle state

for (int i = 0; i < system.ActiveCount; i++)
{
    ParticleState particle = system.GetAt(i); // read-only value copy for renderer
}
```

`ParticleSystem.Update` and emitter `Update` each enforce their own configurable maximum visual/emission delta. A large frame stall **does not** accumulate an unbounded cosmetic backlog. Spawned particles are considered to have been emitted at the beginning of the presented update; finer sub-frame timing can be introduced if visual tests require it.

## Fixed capacity and fault behavior

- **Pool full:** reject a new spawn; keep all live particles; increment `DroppedAtCapacity`.
- **Burst/rate limit:** drop any spawns above `MaxSpawnsPerUpdate`; count `SkippedEmissionOpportunities`. Excess is not deferred to later frames.
- **Frame hitch:** continuous emissions use at most `MaxEmissionDeltaSeconds` and count skipped time-based opportunities. Fractional *within-cap* remainder carries across normal frames.
- **Strict (default):** invalid definition rejects at setup; invalid spawn throws, non-finite update throws immediately.
- **RecoverAndCount:** still rejects invalid definitions at setup; invalid individual particle state is dropped and counted, without catching unrelated graphics/gameplay exceptions.
- **Clear:** immediately reclaims all slots; optional reset of cumulative diagnostics. This is *pool-wide* cleanup; ownership-selective cleanup will be added when per-scene effect instances arrive.

The cumulative `ParticleDiagnostics` value reports capacity, active particles, spawned, expired, dropped-at-capacity, dropped-invalid, and skipped emission opportunities.

## JSON example presets

`P01_TestEmitters.json` is intentionally included as **authoring data only**. No loader has been added to `Game1`, and no `.csproj` or runtime asset-copy behavior is needed until P02/P03. It uses a versioned manifest:

```json
{
  "version": 1,
  "emitters": [
    { "id": "test.example", "burstCount": 10, "lifetimeSeconds": { "min": 0.2, "max": 0.5 } }
  ]
}
```

`ParticleEmitterLibrary.FromJson` validates IDs, duplicate names, nonfinite/range-invalid settings, unknown properties, enum values, and incompatible schema versions.

## P01 exit gate

1. New `P01ParticleFoundationTests` pass.
2. Full existing xUnit regression suite passes.
3. Original game builds/runs with unchanged gameplay, audio, animations, level navigation, and diagnostics.
4. No MonoGame reference is required by particle runtime; no new gameplay dependencies.
5. Rendering and visual tests are deliberately **deferred to P02**.

## Roadmap boundary

- **P02:** SpriteBatch adapter, shared sprite textures, world/UI coordinate conversions, layered alpha/additive rendering, standalone Particle Playground.
- **P03:** Composite/multi-emitter effects, richer definitions and world-vs-emitter-local behavior, five named presets.
- **P04:** Confirmed gameplay events, LDtk environmental sources, player/scene ownership and separate world/UI pause semantics.
- **P05:** Further authoring/game-feel tuning and preset usability.
- **P06:** Measured performance/profiling, optimization and full integration validation.

This sequencing preserves the approved PS-01 through PS-20 architecture without prematurely changing the authoritative physics or game loop.
