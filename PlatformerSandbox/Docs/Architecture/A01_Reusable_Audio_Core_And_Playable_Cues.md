# A01 — Reusable Audio Core and Playable Cue Integration

Status: implementation candidate awaiting local .NET 10 build, xUnit, and DesktopGL listening test.

This is the first playable audio pass built against the validated C02 sprite-animation baseline. It is informed by the approved AS-01–AS-35 audio architecture, especially AS-01–AS-10, AS-12–AS-14, and AS-17–AS-19. It is *not* a claim that every AS item has been implemented or validated.

## Files and ownership

- `PlatformerSandbox.Foundation/Audio/`: backend-independent cue catalog, policy engine, buses, voice ownership and music director. Reusable; it references neither MonoGame nor platformer gameplay.
- `Source/Audio/MonoGameAudioBackend.cs`: MonoGame `SoundEffect` instances, asset ownership, and disposal.
- `Source/Audio/MonoGameMusicBackend.cs`: optional native `Song`/`MediaPlayer` adapter. No song is shipped or requested in A01. The fade director is covered by headless tests.
- `Source/Audio/PlatformerAudioPresenter.cs`: platformer-specific mapping from *committed* `WorldGameplayEvent`s to cues. This is not part of the reusable foundation.
- `Content/Audio/cues.json`: data-driven cue IDs, sample variations, gains, priorities, cooldowns, limits and loop flags.
- `Content/Audio/*.wav`: short, **original, programmatically generated** placeholder cues. Intended only for functional testing and replaceable with properly licensed final sound design.
- `Platforms/DesktopGL/PlatformerSandbox.DesktopGL.csproj`: deploys WAVs and manifest to `Content/Audio/` adjacent to the executable. A01 intentionally uses readable WAV files as a temporary native-loader path, following the already validated C02 asset-deployment pattern. Future authored assets should use the existing MonoGame content builder after it is configured for audio.
- `Source/Game1.cs`: startup/scene/UI integration only. Gameplay, physics, LDtk content data, diagnostics and collision files are not modified.

## Semantics

1. World sounds are emitted only by `PlatformerAudioPresenter` after a successfully committed `WorldSession.Step()` event batch. Nothing plays during collision probes or failed/rolled-back ticks.
2. UI navigation, confirmation and back actions are separate semantic cues, independent of physics pause.
3. The mixer has independent Master, Music, Gameplay, Ambience, UI and Voice logical gains. World pause mutes active Gameplay and Ambience voices but leaves UI available. Ctrl+Shift+M toggles global mute.
4. A01 sample selection and pitch variation use an **audio-owned random generator**, never gameplay RNG. Optional deterministic seeds enable headless tests.
5. The service caps active voices globally and per cue, applies per-source cooldowns, tracks recent stable event IDs, and can steal lower-priority voices for important new cues. Admission and sample selection do not affect simulation state.
6. Sounds are tagged with owners (`level`, `ui`). Scene changes stop level-owned voices, including the optional cavern ambience, without terminating UI feedback.
7. Missing assets and audio-device initialization faults fall back to silent gameplay with a visible `A01 AUDIO UNAVAILABLE` warning. This is an intentionally cosmetic recovery boundary, not a general-purpose swallow of gameplay exceptions.
8. The `MusicDirector` models desired vs active track, non-overlapping fade-out/fade-in, pause and gain control. Its native adapter uses the normal MonoGame `Song`/`MediaPlayer` path; **a playable music track and scene-directed scoring are not included in A01**.
9. Stereo positioning, surface-aware sound selection, dynamic ducking, authored ambient emitters, music content, settings persistence, accessibility controls and an audio playground remain follow-up items from AS-01–AS-35.

## Test commands

Close all running PlatformerSandbox windows (otherwise DLL files remain locked). From the project root:

```powershell
dotnet build --configuration Debug -m:1
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --no-build --filter FullyQualifiedName~A01AudioSystemTests
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --no-build
```

## Listening acceptance

1. Launch normally and navigate the title / Select Level menu: each change and confirmation produces a subtle short cue.
2. Launch Cavern C: a *quiet*, low synthetic cavern bed plays. Walk and jump: jump and landing events produce different sounds. Moving / wall contacts or activated abilities may play their own cues when the corresponding gameplay events are committed.
3. Press `P` to pause: ambience becomes inaudible; UI feedback remains audible. Resume and verify ambience continues.
4. Press `Ctrl+Shift+M`: sound mutes; press again to restore. In the authored-world gym, Ctrl+Shift+M must not trigger the existing plain-M gameplay action.
5. Travel between Caverns A/B/C: there should be no overlapping stacked ambient loops. Return to title: ambience stops.
6. Close the game and run D01 full regression tests. If sound hardware or assets are unavailable, gameplay should still run and the warning should explain the failure.

### Troubleshooting

- Verify `Platforms/DesktopGL/bin/Debug/net10.0/Content/Audio/cues.json` and `.../Content/Audio/jump_01.wav` both exist. If not, make sure the new DesktopGL `.csproj` was copied and rebuild after closing running game instances.
- If audio is silent but the warning is absent, check the Windows audio device, mixer settings, output route and MonoGame's `SoundEffect` support on the target system.
- Unlike previous D01 crash/stall acceptance, A01 has **no intentional crash command**. Do not mistake lack of audio for a gameplay simulation failure.
