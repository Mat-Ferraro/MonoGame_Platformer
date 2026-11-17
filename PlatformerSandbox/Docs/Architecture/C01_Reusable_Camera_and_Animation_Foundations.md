# C01 — Reusable Camera and Animation Foundations

Status: implementation candidate, pending local .NET 10 build, xUnit regression run, and visual acceptance in LDtk scenes.

## Architecture and boundaries

- `PlatformerSandbox.Foundation/Presentation/Camera2D.cs`: MonoGame-free, reusable Y-up camera focus, follow dead zone, horizontal look-ahead, time-clamped exponential smoothing, bounds-clamped/centered viewport projection, and screen/world conversions. Camera delta is presentation-frame time only; physics never reads the camera.
- `PlatformerSandbox.Foundation/Presentation/SpriteAnimation.cs`: MonoGame-free immutable clip definitions and presentation-only player timeline. Loop, once, and ping-pong supported. Frame IDs can later address a texture atlas or other renderer; no gameplay events or simulation requests are issued by this component.
- `Source/Presentation/LdtkGrayboxAnimator.cs`: a **game-specific** mapping from committed `WorldPlayerSnapshot` states to reusable animation clips; future player texture loading/atlas draw can replace the graybox adapter without modifying the core animation player.
- `Source/Gyms/LdtkLevelRenderer.cs`: uses a shared `Camera2DView` for collider geometry, moving platforms, players, and camera pixel offsets passed to existing parallax/scenery/terrain drawing. Its no-camera fallback remains compatible with existing direct renderer call sites.
- `Source/Game1.cs`: owns scene-lifetime camera/animation instances, snaps on new scene, freezes presentation advancement while paused, and passes the same camera projection to the LDtk visual renderer. No `WorldSession`, solver, collision, or D01 files change.

## Deliberate scope limits

- Camera zoom, camera shake, trauma impulses, split-screen, per-layer independent viewport scaling, and image-based sprite animation are not yet implemented. Zoom especially needs a coordinated rendering approach so art, tiles, gameplay and UI retain correct relative scales.
- Current visual player animation is a tiny graybox proof: idle eye blink, run/dash foot frame, rising/falling color and facing eye. It doesn't include imported character sprites or affect collider bounds.
- The smooth follow camera replaces LDtk's snap-to-player positioning. The authored gym renderers outside LDtk are unchanged. Parallax uses the existing layer factors with the new shared view origin; no collision layer becomes artwork.
- The host currently uses Draw's elapsed presentation time for camera/animation. A later rendering architecture milestone may move these presentation updates into a dedicated update pass and/or add interpolation between committed world snapshots.

## Local verification

From the project root:

```powershell
dotnet build --configuration Debug -m:1
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --no-build --filter FullyQualifiedName~C01CameraAndAnimationTests
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --no-build
```

In game, use `--dev-level=CAVERN_C` and `--dev-level=PARALLAX_DEMO` and verify:

1. The view snaps correctly upon initial entry / connected-level transition / restart without carrying the last level's camera position.
2. Camera follows horizontal motion with a little look-ahead and smoothing, remains inside level edges, and centers rooms smaller than the screen.
3. Tiles, independent parallax art, collision F9 outlines, moving platforms, and player rectangles remain aligned as the camera moves; resizing the window does not affect gameplay world units.
4. Pause (P) freezes animation/camera presentation and simulation. Resume continues without snapping or spurious input edges.
5. The graybox player's eyes face left/right; running and airborne pose cues follow committed locomotion snapshots.
6. D01 report export (Ctrl+Shift+F12) and the previous completed diagnostics/connected-world regression cases still behave correctly.

No new runtime shortcut is reserved by this phase.
