# P02 — Particle Rendering & Visual Playground

**State:** implementation candidate; requires local build, headless xUnit run and visual acceptance.

## Scope

- Keeps the previously approved P01 pooled, seeded, headless particle engine intact.
- Adds a headless viewport/coordinate-space contract and deterministic back-to-front render-pass ordering in `PlatformerSandbox.Foundation`.
- Adds a reusable `MonoGameParticleRenderer` adapter in `Source/Particles` using shared procedural 32×32 sprites, premultiplied alpha, alpha/additive passes, camera projection and culling. Sprites are created once, disposed once; no per-particle GPU allocations.
- Adds the **isolated Particle Playground** host mode `--dev-particles`. Does not instantiate gameplay physics, connect to `WorldSession`, or modify motor/collision/gameplay events.
- Adds five editable JSON emitter presets, deployed alongside other runtime content through DesktopGL `.csproj`.
- Adds nine headless P02 projection/tint/ordering/budget tests. Exact rendering and graphics disposal must also be inspected in-game.

## Install

Extract this P02 ZIP at the **PlatformerSandbox repository root**, overwriting the matching `Game1.cs` and DesktopGL project file. The ZIP is a partial copy-over package, not an entire project. Keep all previously validated LDtk, D01, C01, C02, A01, A02 and P01 files.

```powershell
dotnet build --configuration Debug -m:1
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~P02ParticleProjectionTests
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-particles
```

## Playground controls

| Key / input | Action |
| --- | --- |
| `1` | Dust: world-space, alpha, world layer |
| `2` | Sparks: world-space, additive, foreground |
| `3` | Smoke: world-space, alpha, background |
| `4` | Motes: continuous world-space background emitter (no manual burst) |
| `5` | Sparkles: UI pixel-space, additive, UI layer |
| `Left mouse` | Burst the selected preset at the cursor (screen-to-world for world presets) |
| `Space` | Burst the selected preset at center; UI uses a fixed panel position |
| `Right mouse` | Burst independent UI sparkles at clicked screen pixels |
| `W/A/S/D` | Pan the world camera (UI particles remain fixed on the screen) |
| `Q/E` | Zoom world camera out/in (UI particle pixel sizes stay unchanged) |
| `Tab` | Cycle the selected world preset through background/world/foreground |
| `B` | Toggle selected preset alpha/additive rendering |
| `P` | Pause/resume **world** particle simulation; UI continues |
| `G` | Bounded stress burst; observe dropped-at-capacity counter |
| `C` | Clear all pools without resetting their cumulative counters |
| `H` | Hide/show diagnostics overlay |
| `Escape` | Exit standalone playground |

### Manual acceptance

1. Launch `--dev-particles`; dust, sparks and smoke should be visible immediately, with drifting ambient motes building over a few seconds.
2. `1`, left-click away from the center: dust appears exactly at the clicked **world** location. Pan with WASD: the burst moves on screen because it stays in world coordinates.
3. `5`, right-click: UI sparkles appear exactly at the clicked **screen** location. Pan and zoom: UI effects stay anchored to pixel coordinates and retain their pixel sizes.
4. Spawn dust on the central turquoise marker and press `Tab` repeatedly: world effects switch behind/on/in front of the marker. Use `B` to contrast alpha with additive glow.
5. Zoom with Q/E and ensure world effects grow/shrink smoothly without unexpected flips. UI sprites do not scale with zoom.
6. Use `P` while motes are active: world particles freeze, UI sparkles continue drifting. Resume: no huge emission burst catches up.
7. Use `G` repeatedly: the program stays responsive, active counts remain below total pool capacity and dropped counters increase (rather than overwriting live particles).
8. Resize the game window. Projection and UI positioning use the new viewport size without exceptions. Close via Escape; resources dispose without graphics errors.
9. Relaunch **without** `--dev-particles`: all existing levels, transitions, animations, movement, audio and diagnostics work unchanged.

## Architectural decisions

- P02 is a laboratory, *not* a new `GameFlowController` destination and not a second gameplay mode.
- One P01 `ParticleSystem` per preset in the lab; render bindings carry world/UI coordinate space, named plane, blend and sprite shape. P03 may group these into multi-emitter effects.
- Background particles render after the sky but before terrain; world particles render after terrain but before reference character; foreground particles render after character; UI particles render over the HUD. Alpha then additive within each plane, stable P01 pool iteration order.
- CPU viewport translates Y-up world to Y-down screen. UI positions and sizes use device pixels. World sizes are world units scaled by camera density/zoom.
- Static procedural sprites are demonstrations only. Illustrative, organically authored textures and LDtk emitter placement are future P03/P04 passes.
- No graphics initialization, scene objects, world physics or input-side changes occur in `PlatformerSandbox.Foundation`.

## Verification limits

Automated tests verify projection, camera panning, zoom, culling, alpha premultiplication, layer pass ordering and bounded P01 spawning. This build environment has no .NET SDK or graphics device; **compilation and runtime playback must be validated on the Windows development machine**. For alpha edges, overlapping sprites and resource lifetime, manual visual checking is indispensable.
