# A02 — Positional Audio, Authored Soundscapes, Settings and Playable Music

Status: integration candidate awaiting local .NET 10 build, full xUnit suite and DesktopGL listening test.

Built on the user-validated A01 audio core, C02 sprite presentation, C01 camera,
LDtk Phase 5, and D01 diagnostics. Does not change simulation or authoritative world state.

## Implementation and boundaries

- `PlatformerSandbox.Foundation/Audio/AudioSpatializer.cs` — listener + emitter falloff,
  mono-to-stereo pan values. Headless; no MonoGame or project-specific types.
- `AudioService` / `IAudioVoice` — optional positional information on cue requests;
  already-playing loops react to listener changes without replay or duplicate events.
  Existing A01 call sites are source-compatible. Non-spatial UI cues remain centered.
- `AudioUserSettings` + `AudioSettingsStore` — JSON settings persisted atomically by the
  host to `%LOCALAPPDATA%/PlatformerSandbox/Settings/audio.json`. Missing, corrupt,
  and out-of-range configuration uses defaults. Categories: Master, Music,
  Gameplay, Ambience, UI, Voice. Mute is session-only.
- `AudioSoundscapeCatalog` — authored, validated per-scene music and emitter definitions.
  All emitter coordinates and distances use positive-Y-up world units (not LDtk pixels).
  Versioned file: `Content/Audio/soundscapes.json`. Each emitter is an ordinary
  scene-owned loop and cleaned up when its scene changes. This *sidecar* is the
  A02 authoring path; adding native LDtk `AudioEmitter` entities is deferred.
- `MonoGameWavMusicBackend` — short **original PCM WAV test loops** to make the
  existing `MusicDirector` audible without an unconfigured Song/XNB build pipeline.
  It retains single-track fade-out/fade-in, not overlapping stems. For production
  long-form tracks prefer a streaming music backend; do not ship multi-minute
  44.1kHz WAVs preloaded as `SoundEffect`.
- `PlatformerSoundscapePresenter` — only project-specific mapping of active
  scene ID to authored environmental emitters and music; the reusable foundation
  never imports LDtk or uses WorldSession.
- `PlatformerAudioPresenter` — now passes committed event positions as optional
  emitters. SFX still occur only *after* committed gameplay events, not during
  failed/rolled-back ticks. Separate audio RNG does not affect gameplay RNG.

## Controls

- `Ctrl+Shift+M`: toggle session mute (existing A01 shortcut, now also mutes music).
- `Ctrl+Shift++` / `Ctrl+Shift+-` (or keypad `+` / `-`): raise/lower Master in 10% steps
  and save the setting to disk. A three-second HUD note confirms adjustments.
- Edit `audio.json` while the game is closed to change individual category gains.
  Volumes must be between 0.0 and 1.0. Changing scenes does not reset settings.
- Game pause still silences Gameplay and Ambience; music intentionally continues.

## Test commands

Close *all* running PlatformerSandbox instances first (otherwise Windows locks DLLs).

```powershell
dotnet build --configuration Debug -m:1
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --no-build --filter FullyQualifiedName~A02SpatialSettingsAndSoundscapesTests
dotnet test .\PlatformerSandbox.Tests\PlatformerSandbox.Tests.csproj --no-build
dotnet run --project .\Platforms\DesktopGL\PlatformerSandbox.DesktopGL.csproj --configuration Debug -- --dev-level=CAVERN_C
```

## Listening acceptance

1. Title menu has quiet short original music; entering CAVERN_C fades out title music,
   fades in a different loop, without a loud jump.
2. Cavern C has continuous quiet environmental air. Moving toward the authored
   air source at x=13 world units increases volume and pans toward the source;
   moving away lowers it. The environmental loop should not restart on movement.
3. Jump and landing sounds are still audible and positional. Menu sounds stay
   centered, and sound does not change collision/movement results.
4. Ctrl+Shift+M mutes and unmutes *music and SFX*. The volume keyboard chord
   changes volume, survives application restart, and its JSON settings file is readable.
5. Pause mutes world ambience but not title/scene music or UI. Returning to title
   stops level emitters; switching A/B/C starts correct ambience and changes music.
6. Full test suite and D01 reporting still work.

## Known limits

- No long-form streamed music, multi-stem layering or adaptive beat-quantized music.
- No native LDtk-editor audio-emitter type yet; the sidecar is data-driven.
- Stereo panning and distance falloff do not model obstruction, reverb or room acoustics.
- The provided small WAV loops are functional placeholders, not final sound design.
- Failure to load optional audio assets should not modify simulation; if a missing
  soundscape breaks A02 startup, restore the `Content/Audio` folder from this ZIP.
