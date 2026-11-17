# LDtk Phase 5 — Integrated Level Content & Authoring Pipeline

**Status:** Implementation candidate built against the user's November 16/17, 2026 `PlatformerSandbox.zip`. Structural/LDtk-JSON checks completed; the local .NET 10 build, entire xUnit suite, MonoGame playback and actual LDtk editor-open/save remain required acceptance gates. D01 Phases 1–4 and existing motion solvers were not modified.

## What changed

- `LdtkContentCatalog.Discover(Content/Levels)` scans **all top-level `*.ldtk` project files** (normal embedded JSON) and validates every level, its game entities, all exit destinations, all tile layers, scenery and independent artwork. Invalid projects are rejected independently and displayed as a count on the title screen; they are not quietly loaded with defaults.
- The level-select menu now uses the discovered catalog; new editor-authored level identifiers do **not** require adding a branch in `Game1.cs`, the scene factory, or `GameLevelMenuCatalog`. The four historical one-level demos retain their existing public IDs (`LDTK_TEST`, `LDTK_PARALLAX_DEMO`, `LDTK_SCENERY_DEMO`, `LDTK_TERRAIN_DEMO`) through compatibility aliases to LDtk's actual identifiers. The graybox route/movement gyms remain registered fixtures.
- `LdtkContentCatalog.Prepare(...)` reads the selected project's JSON once at the safe scene-preparation boundary, validates the whole linked project, resolves the requested entrance and all visuals from that snapshot, and only then builds GPU textures and a `WorldSession`. Invalid edits fail preparation, leaving the previous scene intact. Content is never read from disk in the simulation tick or Draw.
- `LdtkEntityImportRegistry` dispatches typed entity handlers. Built-in gameplay kinds: `LevelEntrance`, `LevelExit`, `Checkpoint`, `WindZone`, `MovingPlatform`. `PlayerSpawn` remains owned by the base collision/spawn importer. `ScenerySprite`, `PaintedSprite` and `TerrainArtwork` remain **visual only** and cannot generate colliders. An unknown gameplay entity kind is rejected rather than silently discarded. New gameplay entities can be implemented by `registry.Register(kind, (context, entity) => ...)` and passing that registry into `LdtkContentCatalog.Discover(...)`; no new scene-factory case is required.
- `ConnectedWorldDemo.ldtk` now includes `CAVERN_C`, with separate IntGrid collision geometry and independently positioned illustrations, background/foreground parallax layers, moving platform, checkpoint, wind region, named entrances, and two exits. Cavern B gains a *third* exit to C. All pre-existing A↔B exits are preserved. Editor project content is intentionally a prototyping fixture, not final art.
- Developer LDtk exit shortcuts `1`, `2`, and **`3`** map to sorted exit IDs; the third is useful for Cavern B. Existing `F9` collision/trigger debug, `F10` collision backing, `F11` HUD controls, and `T` restart remain intact.

## New-level workflow (no new C# code)

1. Open a `.ldtk` project in `Content/Levels` using LDtk 1.5.3 or a compatible editor. Existing `ConnectedWorldDemo.ldtk` is a usable template with gameplay schemas and visual previews.
2. Create a level with a **globally unique LDtk identifier** and one top-left-pivot `PlayerSpawn`. Keep one `Collisions` IntGrid (32px, 0 empty / 1 solid) and one zero-offset `Entities` layer. Paint solids only in `Collisions`.
3. Place or resize the supported gameplay entities on `Entities`. `START` is the implicit PlayerSpawn entrance. Additional `LevelEntrance` markers use unique `EntranceId` strings. Exits require `TriggerId`, `TargetLevel`, and `TargetEntrance` pointing to levels **in the same LDtk project**. Trigger IDs are unique per level and must be positive. Collision platform IDs must also be unique. All start/entrance/checkpoint spawns must be unblocked.
4. Author independent visual layers named `BG_*`, `Terrain_Art` and `FG_*` and/or editor-preview `PaintedSprite` entities. Transparent individual PNGs can use `TerrainArtwork`; tile-backed `ScenerySprite`/`PaintedSprite` allow editor-resizable illustration placement. Depth and foreground/background ordering are separate from collider order. Rotation, tint and parallax are previewed accurately **in-game**, not necessarily in LDtk.
5. Save the project and **restart the game to discover newly added levels**. Select it from Title → Select Level. For changes to a currently running level, `T` reloads the current project snapshot without regenerating the menu. Restart if an entrance name or level identifier was added, removed or renamed.
6. Toggle `F9`, `F10`, `F11` to inspect interaction boundaries, backing and artwork. Exit volumes must be wide enough to be detected at committed tick endpoints; swept-trigger detection is **not** implemented here.

## Special fixture IDs

- `CAVERN_A`: existing two exits to B; checkpoint and negative 75% wind.
- `CAVERN_B`: existing two exits to A; **new 7105 exit → CAVERN_C/WEST**; positive 75% wind.
- `CAVERN_C`: **7201 exit → CAVERN_A/EAST**, **7202 exit → CAVERN_B/WEST**, checkpoint 7203, wind trigger 7204 at -50%, moving platform collider 8003, seven additional solid cells and repositioned illustrations.

## Validation

```powershell
# From the PlatformerSandbox solution root
# Avoid a second simultaneous Visual Studio/MSBuild build of Content.
dotnet build --configuration Debug -m:1
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --no-build --filter FullyQualifiedName~LdtkPhase5ContentPipelineTests
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --no-build

# Open the new C level directly for a manual check.
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=CAVERN_C
```

Manual LDtk and game acceptance:

- Open `Content/Levels/ConnectedWorldDemo.ldtk` in the LDtk editor and save it without schema or layer errors. Select `CAVERN_C` from the in-game menu, walk/jump, and inspect the extra shelf and overlapping illustrated artwork.
- On Cavern B press `3` to use the newly added third exit, verify transition to C/WEST. In C press `1` and `2` to travel to A/EAST and B/WEST. The old A/B routes still work with `1`/`2` and normal trigger entry.
- Enter wind and checkpoint, then test `R` respawn and `T` reload. Verify no graphical changes affect static collision, player movement, simulation fault policy or world tick.
- Edit a `PaintedSprite` and a decorative layer in LDtk, save and press `T` in game. Confirm the art moves while colliders stay unchanged.
- Exercise F4 and existing LDtk demo shortcuts F7/F8/F9/F10, both menu and pause routes, and the D01 Ctrl+Shift+F12 export. Check `Logs` for exceptions.

## Explicit limitations

- Discovery scans **top-level** `.ldtk` files in `Content/Levels` at application startup, not in-game live filesystem polling. Only normal embedded LDtk projects are supported; external/simplified exports are rejected.
- Cross-file linked exits are not yet supported; related connected levels should live in the same `.ldtk` project.
- An invalid LDtk project is excluded from the menu and counted on the title screen. The `RejectedProjects` dictionary supplies exact per-file errors for tooling/tests, while in-place invalid edits to an already registered project are rejected on scene preparation. More elaborate in-game per-file validation UI is a separate polish task.
- This update does not implement full camera architecture, art blending/masking, lighting, shaders, animation, save games, swept trigger detection, or automatic conversion of arbitrary entity types. It preserves the current free-form layered artwork approach and keeps visual/physics boundaries separate.
