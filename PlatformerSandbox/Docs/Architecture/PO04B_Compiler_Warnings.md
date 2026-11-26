# PO04B compiler warning review

Source: build.log in PO_auto_20261125_150617_730622.zip (previous Release run).
234 distinct diagnostic lines; all CS8632 (nullable reference annotation outside an enabled annotation context). The compiler repeats the warnings in its summary; do not count that second copy. Project distribution: {'PlatformerSandbox.DesktopGL.csproj': 234}.

| File | Warning | Count |
|---|---|---:|
| Game1.cs | CS8632 | 74 |
| GameplayLightingRenderer.cs | CS8632 | 57 |
| LdtkLevelRenderer.cs | CS8632 | 38 |
| LdtkVisualSceneAssets.cs | CS8632 | 20 |
| LightingPlayground.cs | CS8632 | 12 |
| GameDiagnosticBridge.cs | CS8632 | 9 |
| LdtkGrayboxAnimator.cs | CS8632 | 4 |
| SurfaceMaterialWorkbench.cs | CS8632 | 3 |
| PlatformerParticleVfx.cs | CS8632 | 3 |
| MonoGameWavMusicBackend.cs | CS8632 | 2 |
| MonoGameAudioBackend.cs | CS8632 | 2 |
| PlatformerSoundscapePresenter.cs | CS8632 | 2 |
| MovementGymRenderer.cs | CS8632 | 2 |
| MonoGameParticleRenderer.cs | CS8632 | 2 |
| GymDebugText.cs | CS8632 | 2 |
| ParticlePlayground.cs | CS8632 | 1 |
| PlatformerAudioPresenter.cs | CS8632 | 1 |

Recommended next action: inspect the actual DesktopGL project and inherited Directory.Build.props. If nullable analysis was intentionally off, enable annotations only (`<Nullable>annotations</Nullable>`) for that project, then separately evaluate full nullable warnings. Do not blanket-suppress CS8632 or enable full analysis across all projects without review. No .csproj was present in the source export; no build configuration is changed by this package. New warnings must be reviewed from a fresh Release build.
