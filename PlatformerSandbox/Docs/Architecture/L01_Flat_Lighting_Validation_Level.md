# L01 — Flat Lighting Validation Level

## Goal

Isolate **lighting/rendering quality** from complex cavern art, camera parallax, and collision topology. Existing `CAVERN_A/B/C` and lighting playground are unchanged. This level is deliberately a diagnostic environment, not final game art.

## Launch

From the repository root:

```powershell
dotnet build --configuration Debug -m:1
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --filter FullyQualifiedName~LFlatLightingDiagnosticLevelTests
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=LIGHTING_FLAT_TEST
```

The level is also available through the existing discovered LDtk level-selection menu. In LDtk, open `Content/Levels/LightingFlatTest.ldtk`. Saving changes to the **flat test level** and pressing `T` reloads them; the cavern project remains separate.

## Layout and authoring

- One continuous physics floor, from x=0 to x=2560 px, beginning at y=832 px (LDtk coordinates); no platforms, exits, wind, or blocking gameplay props.
- Smooth **Backdrop.png** and uniform **Ground.png** are default lit receivers. Their colors and faint grids expose light falloff, shadows, and any discontinuities.
- **Pillar.png** and the *left* **Crate.png** opt into `CastsShadows`; the *right* crate is identical art but opts out. Only the casting crate enables decorative `ContactShadow`.
- **Panel.png** is a lit receiver without casting shadows. **Unlit.png** remains unlit on purpose, as does the foreground **Vines.png**.
- Four LDtk `LightSource` entities: warm and blue shadow-casting lights, then pink and green illumination-only lights. Move them in LDtk to test position and shadow changes.
- Texture assets are stored under `Content/Levels/TerrainArt/LightingTest/`. PNGs are renderer diagnostics, not gameplay colliders.
- `ReceivesLight`, `ReceivesShadows`, `CastsShadows`, and `ContactShadow` are independent LDtk fields on artwork entities.

**Important rendering limitation:** In the current renderer, objects requiring an independent lighting pass are shaded at a representative point and drawn after the backdrop shadow pass. This level is deliberately designed to reveal, not conceal, discrepancies between those surfaces. Normal-map support in the separate L01 lab is not yet a production GPU normal-map path for LDtk artwork.

## Controls

| Control | Action |
|---|---|
| A / D, Space | Walk and jump along the flat floor |
| `Ctrl+Alt+1` | Enable/disable lighting **without switching levels** |
| `Ctrl+Alt+2` | Cycle Illustrated/Moonlit/Graphic/Readable lighting presets |
| `Ctrl+Alt+3` | Cycle Low/Balanced/High light budgets; Balanced supports up to two shadowed lights |
| `Ctrl+Alt+6` | Enable/disable a **mouse-follow white inspection light** (flat test level only) |
| `T` | Restart and reimport the LDtk level after edits |
| `F11` | Cycle debug HUD; Full displays lighting/presentation counters |
| `F9` | Show/hide collision outlines to verify the flat floor |

The inspection light is not a gameplay entity, does not affect physics, and follows the live mouse position using the current world camera. With inspector enabled, use **High** quality if you need more shadow-light slots. Existing `Ctrl+Alt+4/5` controls remain available.

## Acceptance tests

1. Launch into `LIGHTING_FLAT_TEST`. Confirm the player rests on one continuous flat floor; no colliders are introduced by decoration.
2. Compare lighting on/off with `Ctrl+Alt+1`. The level must **not** change. Both the entire back wall and ground should respond, not just textured props.
3. Walk toward the pillar and identical crates. Shadow casters should darken the environment behind them; the non-casting crate should not.
4. Toggle `Ctrl+Alt+6`, point at different wall/floor locations, and watch its projected light move smoothly. Inspect both sides and corners of the pillar; avoid self-shadow gaps.
5. Contrast the lit wall panel against the explicitly unlit banner/foreground vines. Uniformly unlit decoration is intentional here, not a renderer defect.
6. Turn the quality to High and repeat shadow checks with several lights visible. Observe budget constraints rather than treating missing low-priority shadows as geometric bugs.
7. In LDtk, move a `LightSource`, save, and press `T`. The authored light moves without any physics changes.
8. Launch `CAVERN_A` again to verify the existing authored connected world is untouched; test the lighting lab separately with `--dev-lights`.
9. Run `--dev-level=LIGHTING_FLAT_TEST --no-lighting` as a baseline; player movement and collision must be identical.

## Known boundaries

- All four scene lights are static authored sources. The inspection light is temporary runtime presentation only.
- Particle-linked lights remain supported by the same system; this diagnostic level intentionally has no ambient particle zones, so they cannot obscure lighting results.
- The current LDtk renderer uses SpriteBatch compositing and center-sampled materials, not full depth-aware per-pixel normal-map shading.
- Quality presets can reduce the *number* of lights that cast shadows. Use High for multi-shadow comparisons.
- Decorative contact shadows are a separate visual feature and should not be interpreted as dynamically projected shadows.
