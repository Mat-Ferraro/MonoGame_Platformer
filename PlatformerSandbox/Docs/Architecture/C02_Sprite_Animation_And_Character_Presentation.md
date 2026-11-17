# C02 — Sprite Animation & Character Presentation

Status: **implementation candidate; local .NET build, tests, Content Builder and visual acceptance required**.

## Design boundaries

- `PlatformerSandbox.Foundation.Presentation.SpriteSheetDefinition`: JSON validation, atlas frame geometry and clip lookup. No MonoGame, LDtk, WorldSession, or collision dependency.
- `SpriteAnimationPlayer` from C01: independent presentation timeline; loop, once, ping-pong and per-frame timing.
- `Source/Presentation/LdtkGrayboxAnimator.cs`: platformer-specific adapter from committed `WorldPlayerSnapshot` to Idle / Run / Rising / Falling / Land / Dash and facing. LAND is a one-shot triggered by airborne -> grounded. No simulation changes.
- `Source/Presentation/LdtkCharacterSpriteAssets.cs`: MonoGame renderer asset adapter; texture loaded once by the host. Sprite anchored to the collider's bottom-center. Facing flips the artwork. The collider remains the gameplay authority.
- `Source/Gyms/LdtkLevelRenderer.cs`: draws atlas sprites when available and retains the original rectangle fallback. F9 shows player collision bounds.

## Asset authoring

The **temporary demonstration asset** lives at:

- `Content/Assets/Characters/C02_TestCharacter.png` — 256×144 PNG, 8 columns × 3 rows of 32×48 pixel frames.
- `Content/Characters/C02_TestCharacter.anim.json` — authorable clip names, frame IDs, timing, and atlas dimensions. The DesktopGL project copies this file to `Content/Characters` beside the built content.

All 20 authored frames are distinct poses across six clips. The DesktopGL project now copies the original PNG directly into its build/publish output at `Content/Characters/C02_TestCharacter.png`, alongside the JSON manifest. The MonoGame adapter reads the PNG with `Texture2D.FromStream` once during `LoadContent`; it also accepts a compiled `.xnb` if the PNG is unavailable. The runtime-loaded texture is disposed during `UnloadContent`. This avoids relying on the custom Content Builder to notice newly introduced PNG assets. Do not move `*.anim.json` into the Content Builder's `Assets` directory; its copy rule is separate.

To replace artwork later, change the PNG and JSON rather than modifying the simulation. To change character/clip vocabulary for a different game, provide another gameplay-state adapter; the Foundation clip player and manifest remain reusable. The player-2 demo uses a tint for differentiation.

## Missing-asset behavior

If the manifest or texture cannot be loaded, the host reports a Debug output warning and falls back to the C01 graybox. The main game should remain usable. Malformed frame indices, invalid durations, duplicate clip names, and suspect asset paths are rejected during manifest parsing.

## Validation

1. Close the game before building (Windows DLL file locking).
   Verify both deployed assets after building:
   ```powershell
   Test-Path .\Platforms\DesktopGL\bin\Debug\net10.0\Content\Characters\C02_TestCharacter.png
   Test-Path .\Platforms\DesktopGL\bin\Debug\net10.0\Content\Characters\C02_TestCharacter.anim.json
   ```
   Both should return `True`. If either is false, re-extract the package and rebuild.
2. `dotnet build --configuration Debug -m:1`
3. `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --no-build --filter FullyQualifiedName~C02SpriteSheetDefinitionTests`
4. `dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --no-build`
5. `dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=CAVERN_C`
6. Confirm a **pixel-art character** replaces the rectangle. Running cycles through visibly different leg/arm poses; idle blinks/bobs, jump rises, fall changes pose, landing briefly squashes, dash leans; turning flips the sprite. Test player 2 when applicable.
7. Press F9 and confirm sprite anchoring stays aligned to player collision bounds. Walk on moving supports, into wind zones, and across Cavern A/B/C exits. Test pause and return-to-level. Verify D01 Ctrl+Shift+F12 export.
8. Temporarily move `Content/Characters/C02_TestCharacter.anim.json` out of the **build output** and restart: the old graybox should appear, gameplay unaffected. Restore afterward. Do not delete the source manifest.

**Non-goals:** final illustrated character art, artist tooling, blend trees, skeletal meshes, root-motion authority, sound, particles, or lighting. C02 demonstrates genuine sprite-frame rendering while simulation remains fixed-step and authoritative.

## C02 deployment correction (2026-11-17)

An earlier implementation relied on the custom Content Builder producing an XNB for the new atlas. On the tested Windows setup, `Content.Load` raised `ContentLoadException` and the game correctly displayed the fallback rectangle. The revised DesktopGL project explicitly copies the raw PNG to the output directory and the adapter loads it directly. No physics, camera, LDtk level, or D01 diagnostics changes were necessary.
