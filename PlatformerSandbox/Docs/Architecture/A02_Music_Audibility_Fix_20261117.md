# A02 music audibility & direct-level startup fix

## Changes
- Replaced four original low-level music test WAV loops with clearly audible original synthesized melodies. All are mono 16-bit PCM 22,050 Hz and 12 seconds long.
- The initial soundscape now respects `--dev-level=...` rather than always loading TITLE.
- A brief on-screen message displays the **requested** track and effective music bus gain at level load/transition. This is diagnostic intent, not confirmation that the OS plays audio.

## Validation
1. Close any running PlatformerSandbox process, extract to project root, and overwrite.
2. Build Debug with `dotnet build --configuration Debug -m:1`.
3. Launch without flags: should hear the original title melody (allow ~1 second fade-in).
4. Launch `--dev-level=CAVERN_C`: should hear the Cavern C melody (allow ~1 second fade-in).
5. Observe the brief `A02 MUSIC REQUESTED ... GAIN ...%` overlay. If gain reads 0%, inspect `%LOCALAPPDATA%/PlatformerSandbox/Settings/audio.json` and Ctrl+Shift+M.
6. Verify SFX, pause, scene transitions, volume and tests. If the overlay shows a nonzero gain but music remains silent, inspect audio output-device routing; do not infer an active playback state from the request text.

No physics, gameplay simulation, D01 library or other audio cues are modified.
