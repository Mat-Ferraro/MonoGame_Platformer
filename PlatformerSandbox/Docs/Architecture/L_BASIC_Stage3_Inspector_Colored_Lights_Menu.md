# L-BASIC — Stage 3: color, overlap, inspector, and level-select

## Scope

This release extends the existing isolated lighting-basics lab to two
independent radial lights. It uses the existing `GameplayLightingRenderer`
(`_world`, `_illumination`, `_individual`, `_litLayer` render targets), not a
second lighting implementation. The renderer's gameplay/LDtk passes are
unchanged. No shadows, normal maps, rim lights, HDR, bloom or postprocessing are
part of this stage.

**Deferred TODO:** add an optional full-frame post-processing pass after world
composition with an explicit UI inclusion/exclusion policy. Do not sneak
postprocessing into the Stage 3 colored-light tests.

## Entry points

- Title screen > SELECT LEVEL > `LAB / LIGHTING BASICS` > Enter.
- Pause from the lab with Esc or P; resume, restart, change level or return to
  title through the existing scene-flow menu.
- Standalone shortcut is preserved: `dotnet run --project
  .\\Platforms\\DesktopGL\\PlatformerSandbox.DesktopGL.csproj -- --dev-lighting-basics`.
  In standalone mode Esc exits the process. In level-select mode Esc opens Pause.
- Direct scene boot also works with `--dev-level=LIGHTING_BASICS`.

## In-lab inspector

On the right, click LIGHTING INSPECTOR's header to collapse/expand it.
The panel uses the existing pixel/bitmap font renderer and supports:

| Control | Behavior |
| --- | --- |
| Ambient | Slider, from zero (black when lights off) to one (full white ambient) |
| Render View | Dropdown: final, albedo, combined light buffer, selected-light-only |
| Select Light | Dropdown: Light 1 and Light 2 |
| Light Enabled | Independent per-light toggle |
| Light Color | Dropdown: white, red, green, blue, amber, cyan, magenta |
| Intensity | Per-light slider, 0–2 |
| Radius | Per-light screen-relative slider, .08–.9 of shorter viewport dimension |
| Reset All Lighting | Restores neutral ambient, white light 1 on, blue light 2 off |

Click/drag in the scene (outside the panel) to move the selected light. Mouse
input over panel and its pop-up dropdowns is consumed by the UI and never moves
world lights. R resets and H toggles the short hint line; neither shortcut is
required to configure the lab.

## Expected visual acceptance (manual GPU checks)

1. Set Ambient to 0, disable both lights. The wall, floor and the two lit sample
   objects must be black. The third unlit reference remains its original color.
2. Enable Light 1 (white) and drag near each sample. Lit scene surfaces should
   brighten with the same radial falloff. Inspect FINAL and LIGHT BUFFER.
3. Choose RED for Light 1. Verify that gray surfaces become red, not simply
   brighter or covered by a partially transparent red overlay. Repeat with GREEN
   and BLUE.
4. Enable Light 2, choose BLUE, leave Light 1 RED and drag them together.
   The overlap should be magenta/purple. Move Light 2 away, and ensure Light 1
   does not change.
5. Toggle only Light 2 off, then both on. Changing one light's color, radius,
   intensity or position must not alter the other light's authored parameters.
6. Switch render view to SELECTED LIGHT, then select the other light. Ambient
   must NOT appear in this view, and the disabled light's view must be black.
7. Click each dropdown, slider, toggle, reset button and panel header. Verify
   no UI interaction moves a light accidentally. Confirm mouse drag resumes when
   clicking the scene again.
8. Resize the window and repeat placement; both lights should retain relative
   screen positions and shape. The inspector should stay attached to the right.
9. From the title menu enter this lab, Esc -> Pause -> Resume, then Esc ->
   Return to Title. Re-enter and verify the lab initializes with defaults.
   Restart and level-select transitions must also preserve scene ownership.
10. Revisit the original `--dev-lights`, `--dev-materials`, existing LDtk scenes,
    and automated tests; these should behave as before this release.

## Known limitations

- The basic light buffer is still SDR `SurfaceFormat.Color`. RGB contributions
  are additive and **clamp** to [0,1] at bright overlaps. This is a deliberate
  visual finding for later HDR work, not an HDR implementation.
- The basic lab uses neutral procedural sample art. It is not a representative
  level art benchmark and does not validate normal/rim/shadow effects.
- `Selected Light` is an isolated raw light buffer, not a tinted final scene.
- The lab bypasses gameplay simulation; lights are cosmetic only.
- The optional L03 `.fx` content dependency remains managed by the prior
  DesktopGL shader fix; no shader modifications are required for Stage 3.

## Automated tests

- `LBasicLightingPreviewTests`: baseline stage 1–2 controls and falloff.
- `LBasicStage3LightingControlsTests`: independent lights, colors, clamps,
  selected-light view and reset behavior.
- `LightingBasicsMenuEntryTests`: title-level-select entry, commit, pause,
  and return-to-title transition policy.

Run:

```powershell
 dotnet test .\\PlatformerSandbox.Tests\\PlatformerSandbox.Tests.csproj --filter "FullyQualifiedName~LBasic|FullyQualifiedName~LightingBasicsMenuEntry"
 dotnet test .\\PlatformerSandbox.Tests\\PlatformerSandbox.Tests.csproj
 dotnet run --project .\\Platforms\\DesktopGL\\PlatformerSandbox.DesktopGL.csproj
```

The final graphical behavior must be checked visually on the target machine;
headless tests cannot prove MonoGame blend or shader output.
