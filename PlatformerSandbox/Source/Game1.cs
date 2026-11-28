using PlatformerSandbox.Simulation.Collision.Detection;
using PlatformerSandbox.Foundation.Spatial;
using PlatformerSandbox.Foundation.Performance;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.Input.Requests;
using PlatformerSandbox.Gameplay.Input.Local;
using PlatformerSandbox.Gameplay.World;
using PlatformerSandbox.Gameplay.States;
using PlatformerSandbox.Gameplay.Abilities;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.Presentation;
using PlatformerSandbox.Input;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Gyms;
using PlatformerSandbox.Foundation.Presentation;
using PlatformerSandbox.Foundation.Presentation.Particles;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Audio;
using PlatformerSandbox.Audio;
using PlatformerSandbox.Presentation;
using PlatformerSandbox.Particles;
using PlatformerSandbox.Lighting;

namespace PlatformerSandbox;

/// <summary>
/// MonoGame application host: device polling, scene-flow input adaptation,
/// and presentation. Gameplay and Simulation remain headless and do not own
/// MonoGame graphics or native input state.
/// </summary>
public sealed partial class Game1 : Game
{
    private readonly GraphicsDeviceManager _graphics;
    // Dedicated developer-only P02 visual laboratory. Not a new gameplay scene
    // or physics system; --dev-particles bypasses normal scene update/drawing.
    private readonly bool _particleLabMode = Array.Exists(
        Environment.GetCommandLineArgs(), arg =>
        string.Equals(arg, "--dev-particles", StringComparison.OrdinalIgnoreCase));
    private ParticlePlayground? _particlePlayground;
    // L01 P1-P3 isolated lighting laboratory; game/LDtk/particles remain unaffected.
    private readonly bool _lightingLabMode = Array.Exists(
        Environment.GetCommandLineArgs(), arg =>
        string.Equals(arg, "--dev-lights", StringComparison.OrdinalIgnoreCase));
    private LightingPlayground? _lightingPlayground;
    // L-BASIC: isolated ambient + one-light visual acceptance using the real compositor.
    private readonly bool _lightingBasicsMode = Array.Exists(
        Environment.GetCommandLineArgs(), arg =>
        string.Equals(arg, "--dev-lighting-basics", StringComparison.OrdinalIgnoreCase));
    private BasicLightingLab? _lightingBasicsLab;
    private bool IsLightingBasicsScene =>
        _flow.ActiveScene == MajorSceneKind.Gameplay &&
        _flow.Scenes.ActiveDestination?.LevelId == GameLevelIds.LightingBasics;
    // L03A GPU per-pixel material workbench. Separate from production LDtk path
    // until shader/content/device validation is completed on target hardware.
    private readonly bool _materialLabMode = Array.Exists(
        Environment.GetCommandLineArgs(), arg =>
        string.Equals(arg, "--dev-materials", StringComparison.OrdinalIgnoreCase));
    private SurfaceMaterialWorkbench? _materialWorkbench;
    // L01 P4-P6: scene-owned lighting is presentation only, independent of physics.
    private GameplayLightingRenderer? _gameLighting;
    private readonly PlatformerSandbox.Presentation.GpuTimestampWindow _gpuTimer=new();
    private static string GpuBackendName =>
#if GPU01_WINDOWSDX
        "WindowsDX";
#elif GPU01_WINDOWSDX12
        "WindowsDX12";
#elif GPU01_DESKTOPVK
        "DesktopVK";
#else
        "DesktopGL";
#endif
    private bool _gpuLabTiming;
    private int _gpuStressCount;
    private bool _gpuStressMotion;
    private static int GpuStressFromEnvironment => int.TryParse(Environment.GetEnvironmentVariable("PS_PO_GPU_PARTICLE_STRESS"),out int n) ? Math.Clamp(n,0,50000) : 0;
    private int GpuStressCount => AutomatedLaunch ? GpuStressFromEnvironment : _gpuStressCount;
    private bool GpuStressMotion => AutomatedLaunch ? Environment.GetEnvironmentVariable("PS_PO_GPU_PARTICLE_MOTION")=="1" : _gpuStressMotion;
    private Effect? LoadGpuParticleEffect()
    {
        try {return Content.Load<Effect>("Effects/GPU01_ParticleInstances");}
        catch(Microsoft.Xna.Framework.Content.ContentLoadException ex) {System.Diagnostics.Debug.WriteLine("GPU01 particle effect fallback: "+ex);return null;}
    }
    private LightingGameCatalog? _lightingCatalog;
    private GameplayLightingPresenter? _lightingEvents;
    private LdtkLightingScene? _ldtkLighting;
    private string? _lightingWarning;
    private string _lightingPresetId = "Illustrated";
    private double _lightingPresetNoticeSeconds;
    private LightingQuality _lightingQuality = LightingQuality.Balanced;
    private ProductionMaterialLightMode _materialLightMode = ProductionMaterialLightMode.AllEligible;
    // L19 presentation-only controls, deliberately independent for A/B tests.
    // Phase 19B: mouse-driven, repeatable material comparison states.
    private int _l19bMaterialPreset = -1; // -1 means manual/custom
    private int _l19ShowcaseFocus;
    private bool _l19Hdr;
    private bool _l19Emission = true;
    private bool _l19Bloom;
    private float _l19Exposure = 1.15f;
    private float _l19BloomThreshold = 0.85f;
    private float _l19BloomStrength = 0.45f;
    private float _l20BloomScatter = .65f;
    private bool _l20BloomOnly;
    private bool _l20ReceiverNormals = true, _l20PolygonRims = true;
    private ReceiverProjectionMode _l20ReceiverMode = ReceiverProjectionMode.Clipped;
    private int _l20LightFilter; // 0 all, 1 sun, 2 moon, 3 point, 4 spot, 5 tube, 6 emissive
    private ShadowEdgePreset _shadowEdgePreset = ShadowEdgePreset.Hard;
    private ShadowFilterMode _shadowFilterMode = ShadowFilterMode.UniformGaussian;
    private ShadowQualityPolicy _shadowQualityPolicy = ShadowQualityPolicy.Manual;
    private int _shadowSelectedLightIndex;
    private readonly Dictionary<string, float> _shadowEmitterOverrides =
        new(StringComparer.Ordinal);
    private bool _reducedLightingFlashes;
    // Debug-only movable inspection light for the dedicated LDtk flat test.
    private bool _lightingInspectionLight;
    private bool _lightingEnabled = true;
    // Phase 19D: world lights can be disabled without disabling HDR/emission/bloom.
    private bool _l19dWorldLightsEnabled = true;
    private bool _lightingStress;
    private readonly LightingToolsPanel _lightingTools = new();
    private bool _previousUiMouseLeftDown;
    private LightingViewport? _lastLightingView;
    private IReadOnlyList<RadialLight> _lastSelectedLights = Array.Empty<RadialLight>();
    private readonly bool _noLighting = Array.Exists(
        Environment.GetCommandLineArgs(), arg =>
        string.Equals(arg, "--no-lighting", StringComparison.OrdinalIgnoreCase));

    // P04 world/scene/UI cosmetics. Never referenced by WorldSession.Step().
    private PlatformerParticleVfx? _particleVfx;
    private LdtkParticleSceneDefinition? _ldtkParticleZones;
    private string? _particleWarning;
    private readonly bool _noParticles = Array.Exists(
        Environment.GetCommandLineArgs(), arg =>
        string.Equals(arg, "--no-particles", StringComparison.OrdinalIgnoreCase));
    private readonly GameDiagnosticBridge? _diagnostics;
#if DEBUG
    private readonly bool _devCrashAfterWorldStep = Array.Exists(
        Environment.GetCommandLineArgs(), argument =>
        string.Equals(argument, "--dev-context-crash-after-world-step", StringComparison.OrdinalIgnoreCase));
    private readonly bool _devStallAfterWorldStep = Array.Exists(
        Environment.GetCommandLineArgs(), argument =>
        string.Equals(argument, "--dev-stall-after-world-step", StringComparison.OrdinalIgnoreCase));
    private bool _devStallInjected;
    private readonly bool _devFailInsideWorldStep = Array.Exists(
        Environment.GetCommandLineArgs(), argument =>
        string.Equals(argument, "--dev-context-fail-inside-world-step", StringComparison.OrdinalIgnoreCase));
#endif
    private readonly GameFlowController _flow;
    private readonly LdtkContentCatalog _ldtkCatalog;
    private readonly SceneLightingLayerCatalog _sceneLayers;
    private GamePadState _previousGamepad;
    // Gameplay assistance starts enabled in the *playable* gym. Unit-test
    // fixtures may still explicitly select the unassisted baseline.
    private MovementGymSession _gym = new(
        assistancePolicy: MovementGymSession.PlayableAssistancePreset);
    private readonly List<MovementRequest> _inputRequests = new(5);
    private readonly List<WorldPlayerCommand> _worldCommands = new(2);
    private readonly List<CharacterStateRequest> _characterStateRequests = new(4);
    private readonly List<CharacterAbilityRequest> _characterAbilityRequests = new(5);
    private readonly List<WorldMotionRequest> _motionRequests = new(3);
    private bool _motionLabMode;
    private bool _routeLabMode;
    private bool _ldtkLevelMode;
    private LdtkImportedLevel? _ldtkLevel;
    private LdtkConnectedLevel? _ldtkConnectedLevel;
    private readonly HashSet<int> _ldtkActiveWindIds = new();
    private bool _ldtkWindDirty;
    private string _lastLdtkInteraction = "NONE";
    private LdtkVisualSceneAssets? _ldtkVisuals;
    // Presentation-owned, never part of the authoritative world tick.
    private Camera2D? _ldtkCamera;
    private LdtkGrayboxAnimator? _ldtkCharacterAnimations;
    private LdtkCharacterSpriteAssets? _ldtkCharacterSprites;
    private AudioService? _audio;
    private PlatformerAudioPresenter? _audioEvents;
    private MusicDirector? _music;
    private PlatformerSoundscapePresenter? _soundscapes;
    private readonly string _audioSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PlatformerSandbox", "Settings", "audio.json");
    private string _audioControlMessage = "";
    private double _audioControlNoticeSeconds;
    private string? _audioWarning;
    private string? _ldtkCharacterArtWarning;
    private bool _ldtkShowCollisionDebug;
    private bool _ldtkDrawCollisionBackdrop = true;
    private GameDebugHudMode _debugHudMode = GameDebugHudMode.Full;
    private bool _debugHudUserSelected;
    private GameplaySession? _gameplaySession;
    private long _nextGameplaySessionId;
    private string _lastRouteStatus = "NONE";
    private bool _failNextRoutePreparation;
    private int _windSelection; // 0 off, 1 left 75%, 2 right 75%

    private string _latestStateFeedback = "NONE";
    private string _latestAbilityFeedback = "NONE";
    private readonly List<WorldGameplayEvent> _recentWorldEvents = new(8);
    private readonly SceneFlowSmokeRunner _sceneFlowSmoke = new();
    private long _nextWorldInstanceId;
    private WorldSession? _worldClockTest;
    private bool _authoredWorldMode;
    private int _worldP1Respawns;
    private int _worldP2Respawns;
    private readonly LocalPlayerInputRouter _inputRouter = new();
    private readonly MonoGameLocalInputBindings _inputBindings = new();
    private LocalPlayerInput? _lastPlayerInput;
    private bool _gamepadOneConnected;
    private bool _requestTestEnabled;
    private MovementRequestGymScenario _requestScenario = MovementRequestGymScenario.ControlLock;
    private bool _reverseRequestOrder;
    private MovementResolution? _lastResolution;
    private HorizontalDirection _lastKeyboardDirection = HorizontalDirection.Neutral;
    private SpriteBatch? _spriteBatch;
    private Texture2D? _pixel;
    private KeyboardState _previousKeyboard;
    private bool _paused;
    private int _titleRefreshCounter;
    private MovementGymParameter _selectedParameter = MovementGymParameter.GroundStoppingTime;
    private string _tuningLabel = "Baseline";

    public Game1(GameDiagnosticBridge? diagnostics = null)
    {
        _diagnostics = diagnostics;
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = Environment.GetEnvironmentVariable("PS_PO_B_1080P") == "1" ? 1920 : 1280,
            PreferredBackBufferHeight = Environment.GetEnvironmentVariable("PS_PO_B_1080P") == "1" ? 1080 : 720,
            SynchronizeWithVerticalRetrace = EnginePerformanceOptions.InitialVSync
        };
        _processOverridesGraphics = HasBenchmarkEnvironment;
        EnginePerformanceOptions.SavedGraphics = _processOverridesGraphics ? null : (GraphicsPreferences.Load(_graphicsPath) ?? GraphicsPreferences.EstablishedDefaults());
        if (EnginePerformanceOptions.SavedGraphics is { } saved)
        {
            var display=GraphicsAdapter.DefaultAdapter.CurrentDisplayMode;
            if (saved.Width <= display.Width && saved.Height <= display.Height)
            {
                _graphics.PreferredBackBufferWidth=saved.Width;
                _graphics.PreferredBackBufferHeight=saved.Height;
                _graphics.IsFullScreen=saved.Fullscreen;
            }
            _graphics.SynchronizeWithVerticalRetrace=saved.VSync;
            _perfUncappedActive=_perfUncappedRequested=!saved.VSync;
            _lightingQuality=saved.Quality;
        }
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        IsFixedTimeStep = true;
        TargetElapsedTime = TimeSpan.FromSeconds(MovementGymSession.FixedStepSeconds);
        Window.AllowUserResizing = true;
        _sceneLayers = SceneLightingLayerCatalog.FromJson(File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(FindLdtkDirectory())!, "Lighting",
                "L02_SceneLayers.json")));
        _ldtkCatalog = LdtkContentCatalog.Discover(FindLdtkDirectory(),
            layerSettings: _sceneLayers);
        _flow = new GameFlowController(new HostSceneFactory(this),
            GameLevelMenuCatalog.WithDiscoveredLevels(_ldtkCatalog.Levels.Keys));
    }

    protected override void Initialize()
    {
        _previousUiMouseLeftDown = Mouse.GetState().LeftButton == ButtonState.Pressed;
        _previousKeyboard = Keyboard.GetState();
        _previousGamepad = GamePad.GetState(PlayerIndex.One);
        _inputRouter.SelectSource(LocalInputSource.Keyboard,
            _inputBindings.CaptureKeyboard(_previousKeyboard));
        // Optional direct developer boot, e.g. --dev-level=MOTION_LAB.
        // Bypasses the title without adding a separate hidden scene manager.
        string? directLevelId = null;
        string directEntranceId = GameLevelIds.DefaultEntrance;
        foreach (string argument in Environment.GetCommandLineArgs())
        {
            if (argument.StartsWith("--dev-level=", StringComparison.OrdinalIgnoreCase))
                directLevelId = argument["--dev-level=".Length..].ToUpperInvariant();
            else if (argument.StartsWith("--dev-entrance=", StringComparison.OrdinalIgnoreCase))
                directEntranceId = argument["--dev-entrance=".Length..].ToUpperInvariant();
        }
        if (directLevelId is not null &&
            _flow.RequestPlay(directLevelId, directEntranceId) == SceneQueueResult.Queued)
            _flow.CommitPending();
        UpdateWindowTitle();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
        GymDebugText.Initialize(GraphicsDevice);
        if (_lightingBasicsMode)
        {
            _lightingBasicsLab = new BasicLightingLab(GraphicsDevice, Content);
            Window.Title = "PlatformerSandbox - Basic Lighting Lab (Stages 1-5)";
            return;
        }
        if (_materialLabMode)
        {
            string materialManifest = Path.Combine(AppContext.BaseDirectory,
                "Content", "Lighting", "L03_SurfaceMaterials.json");
            _materialWorkbench = new SurfaceMaterialWorkbench(GraphicsDevice, Content, materialManifest);
            Window.Title = "PlatformerSandbox - L03 GPU Surface Materials Lab";
            return;
        }
        if (_lightingLabMode)
        {
            string lightingManifest = Path.Combine(AppContext.BaseDirectory,
                "Content", "Lighting", "L123_LightingLab.json");
            _lightingPlayground = new LightingPlayground(GraphicsDevice, lightingManifest);
            Window.Title = "PlatformerSandbox - L01 Phase 1-3 Lighting Lab";
            return;
        }
        if (_particleLabMode)
        {
            string particleManifest = Path.Combine(AppContext.BaseDirectory,
                "Content", "Particles", "P02_PlaygroundEmitters.json");
            _particlePlayground = new ParticlePlayground(GraphicsDevice, particleManifest);
            Window.Title = "PlatformerSandbox - P02 Particle Playground";
            return;
        }
        // C02 test artwork. Missing/malformed optional art must not prevent
        // opening LDtk scenes: retain the C01 procedural character fallback.
        try
        {
            _ldtkCharacterSprites = LdtkCharacterSpriteAssets.Load(Content, GraphicsDevice);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            Microsoft.Xna.Framework.Content.ContentLoadException or
            System.Text.Json.JsonException or ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine("C02 sprite fallback: " + ex);
            _ldtkCharacterArtWarning = ex.GetType().Name + ": " + ex.Message;
            _ldtkCharacterSprites = null;
        }
        // A01: authored cue manifest, preloaded test SFX, and native playback
        // adapter. Keep failures in presentation so missing audio hardware
        // cannot corrupt or prevent the deterministic simulation.
        try
        {
            string audioFolder = Path.Combine(AppContext.BaseDirectory, "Content", "Audio");
            AudioCueCatalog cues = AudioCueCatalog.FromJson(
                File.ReadAllText(Path.Combine(audioFolder, "cues.json")));
            _audio = new AudioService(cues,
                new MonoGameAudioBackend(audioFolder, cues), maxVoices: 16);
            AudioSettingsStore.Load(_audioSettingsPath).Apply(_audio);
            _audioEvents = new PlatformerAudioPresenter(_audio);
            // Optional music must not disable otherwise working UI/gameplay SFX.
            try
            {
                _music = new MusicDirector(new MonoGameWavMusicBackend(audioFolder));
                AudioSoundscapeCatalog authoredSound = AudioSoundscapeCatalog.FromJson(
                    File.ReadAllText(Path.Combine(audioFolder, "soundscapes.json")));
                _soundscapes = new PlatformerSoundscapePresenter(_audio, _music, authoredSound);
                RefreshMusicVolume();
                // Direct --dev-level launches commit their scene before LoadContent.
                // Initialize the matching soundscape rather than always playing TITLE.
                string initialAudioScene = _flow.ActiveScene == MajorSceneKind.Gameplay
                    ? (_flow.Scenes.ActiveDestination?.LevelId ?? "")
                    : "TITLE";
                _soundscapes.ChangeScene(initialAudioScene);
                ShowMusicStatus();
            }
            catch (Exception musicFailure) when (musicFailure is not OutOfMemoryException)
            {
                System.Diagnostics.Debug.WriteLine("A02 optional soundscape music unavailable: " + musicFailure);
                _music?.Dispose();
                _music = null;
                _soundscapes = null;
                ShowAudioMessage("A02 MUSIC/SOUNDSCAPE UNAVAILABLE");
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine("A01 optional audio fallback: " + ex);
            _audioWarning = ex.GetType().Name + ": " + ex.Message;
            _audioEvents = null;
            _soundscapes = null;
            _audio?.Dispose();
            _audio = null;
            _music?.Dispose();
            _music = null;
        }
        if (!_noParticles)
        {
            try
            {
                _particleVfx = new PlatformerParticleVfx(GraphicsDevice,
                    Path.Combine(AppContext.BaseDirectory, "Content", "Particles"),LoadGpuParticleEffect());
                ActivateParticleWorld(); // also handles direct --dev-level boot
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                System.Diagnostics.Debug.WriteLine("P04 optional particle fallback: " + ex);
                _particleWarning = ex.GetType().Name + ": " + ex.Message;
                _particleVfx?.Dispose();
                _particleVfx = null;
            }
        }
        // L01 P4-P6 lighting is optional presentation; asset/hardware failures
        // never change a committed game level or authoritative world state.
        if (!_noLighting)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory,
                    "Content", "Lighting", "L456_GameplayLighting.json");
                _lightingCatalog = LightingGameCatalog.FromJson(File.ReadAllText(path));
                _lightingPresetId = _lightingCatalog.DefaultPreset;
                // Stage 15B: production and lab use the same material GPU shader.
                // The effect remains owned by the host ContentManager.
                _gameLighting = new GameplayLightingRenderer(GraphicsDevice,
                    Content.Load<Effect>("Effects/L_BASIC_SurfaceLighting"),
                    Content.Load<Effect>("Effects/L20_PolygonRim"),
                    Content.Load<Effect>("Effects/L21_TangentTransform"));
                _lightingEvents = new GameplayLightingPresenter();
                ActivateLightingWorld();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                System.Diagnostics.Debug.WriteLine("L456 optional lighting unavailable: " + ex);
                _lightingWarning = ex.GetType().Name + ": " + ex.Message;
                _gameLighting?.Dispose();
                _gameLighting = null;
                _lightingEvents = null;
                _lightingCatalog = null;
            }
        }
    }

    private void ActivateLightingWorld()
    {
        if (_lightingEvents is null) return;
        if (_lightingCatalog is not null)
        {
            _lightingPresetId = _lightingCatalog.GetDefaultPresetForScene(_ldtkLighting?.LevelId);
            if (_ldtkLighting?.LevelId == "LIGHTING_FLAT_TEST")
                _lightingPresetNoticeSeconds = 4;
        }
        _lightingEvents.Clear();
        if (_ldtkLighting is not null && _worldClockTest is WorldSession world)
            _lightingEvents.EnterWorld(world.WorldInstanceId, _ldtkLighting);
    }

    private IEnumerable<RadialLight> EnumerateParticleLights()
    {
        if (_particleVfx is null || _lightingCatalog is null) yield break;
        int sequence = 0;
        foreach (var (id, position) in _particleVfx.LightCandidates())
        {
            LightingParticleBinding? binding = _lightingCatalog.ForParticle(id);
            if (binding is null) continue;
            yield return new RadialLight
            {
                Id = "particle_" + sequence++,
                Owner = "particle",
                Position = new LightPoint(position.X, position.Y),
                Color = binding.Color, Radius = binding.Radius,
                Intensity = binding.Intensity, Priority = binding.Priority,
                CastShadows = binding.CastShadows,
                Channels = LightChannel.Terrain | LightChannel.Hero | LightChannel.Effects
            };
        }
    }

    private IEnumerable<RadialLight> LightingStressCandidates(Camera2DView camera)
    {
        // Actual LDtk world-projection inspection. No light is written to the
        // authoritative world, and changing this never advances a simulation tick.
        if (_lightingInspectionLight &&
            _ldtkLighting?.LevelId == "LIGHTING_FLAT_TEST")
        {
            MouseState mouse = Mouse.GetState();
            Vector2D worldPoint = camera.ScreenToWorld(mouse.X, mouse.Y);
            yield return new RadialLight
            {
                Id = "flat_inspector", Owner = "stress",
                Position = new LightPoint(worldPoint.X, worldPoint.Y),
                Color = new LightRgb(1, .95, .79), Radius = 8.5,
                Intensity = 1.55, Priority = 100, CastShadows = true,
                Channels = LightChannel.All
            };
        }
        if (!_lightingStress) yield break;
        // Fixed grid relative to the view for a reproducible culling / budget
        // stress case. These lights never enter a gameplay tick or input path.
        for (int i = 0; i < 120; i++)
            yield return new RadialLight
            {
                Id = "stress_" + i, Owner = "stress", Tag = "STRESS",
                Position = new LightPoint(camera.Left + (i % 12 + .5) *
                    camera.VisibleWidth / 12,
                    camera.Bottom + (i / 12 + .5) * camera.VisibleHeight / 10),
                Radius = 1.9, Intensity = .34, Priority = -25,
                Color = (i % 3) switch
                {
                    0 => new LightRgb(.8, .4, .24),
                    1 => new LightRgb(.28, .56, .95),
                    _ => new LightRgb(.45, .8, .48)
                },
                CastShadows = true
            };
    }

    private readonly AdaptiveAreaSamplePolicy _adaptiveAreaSamples = new();
    private int CpuAreaSampleCount => IsPerformanceLab && _perfIsolation == 5 ? 1 : EnginePerformanceOptions.AreaSamples == 0 ? AreaEmitterSampling.MaximumSamples : EnginePerformanceOptions.AreaSamples;

    private Color ShadeGameplaySurface(string iid, IReadOnlyList<RadialLight> lights,
        LightingArtPreset art, LdtkLightingScene? scene, IReadOnlyList<LightingWorldSampler.PreparedSample>? prepared = null)
    {
        if (scene is null ||
            !scene.SurfaceCenters.TryGetValue(iid, out LightPoint point))
            return Color.White;
        LightingSurfaceOptions options = scene.GetSurface(iid);
        SceneLightingLayer layer = scene.Layers.Get(options.LayerId);
        // A light-receiving sprite may independently cast shadows. Passing its
        // IID into the sampler excludes its own hull from front-face shading.
        // Silhouettes normally stay dark, but an explicitly front-facing
        // gameplay flash can reveal their visible face. This runs in the same
        // sprite post-pass as ordinary prop lighting, after the back-wall pass.
        LightRgb? reveal = options.LightingMode == LightingSurfaceMode.Silhouette
            ? LightingWorldSampler.SampleSilhouetteReveal(point, options.Channel,
                options.ReceivesShadows, iid, art, lights, scene.Occluders, layer, prepared, CpuAreaSampleCount)
            : null;
        LightRgb sampled = options.LightingMode is LightingSurfaceMode.Silhouette or
            LightingSurfaceMode.Unlit
            ? LightRgb.White
            : LightingWorldSampler.Sample(point, options.Channel,
                options.ReceivesShadows, iid, art, lights, scene.Occluders, layer, prepared, CpuAreaSampleCount);
        return GameplayLightingRenderer.ToColor(
            LightingSurfaceShading.ResolveTint(options, layer, sampled, reveal));
    }

    private Color ShadeGameplayHero(LightPoint point, IReadOnlyList<RadialLight> lights,
        LightingArtPreset art, LdtkLightingScene? scene, IReadOnlyList<LightingWorldSampler.PreparedSample>? prepared = null)
    {
        if (scene is null) return Color.White;
        SceneLightingLayer playerLayer = scene.Layers.Gameplay;
        if (playerLayer.Mode == RenderLayerLightingMode.Unlit) return Color.White;
        LightRgb rgb = LightingWorldSampler.Sample(point, LightChannel.Hero,
            playerLayer.ReceivesShadows, null, art, lights, scene.Occluders, playerLayer, prepared, CpuAreaSampleCount);
        // Accessibility: player silhouette remains legible in dark art presets.
        return GameplayLightingRenderer.ToColor(new LightRgb(
            Math.Max(art.HeroMinimumBrightness, rgb.R),
            Math.Max(art.HeroMinimumBrightness, rgb.G),
            Math.Max(art.HeroMinimumBrightness, rgb.B)));
    }

    // Initialize presentation-only gameplay particles AFTER the scene (which
    // may have been direct-booted) and the shared renderer are available.
    // No VFX asset failure should prevent the existing game from launching.
    private void ActivateParticleWorld()
    {
        if (_particleVfx is null) return;
        if (_worldClockTest is not WorldSession world)
        {
            _particleVfx.Presenter.LeaveWorld();
            _particleVfx.ConfigureEnvironment(null);
            return;
        }
        // Temporary scene-level ambient follows P1 in LDtk. Authored LDtk
        // placements belong to P05. No ambient effect in graybox gym fixtures.
        Vector2D? ambientOrigin = _ldtkLevelMode && world.HasPlayer(1)
            ? world.GetPlayer(1).Bounds.Center : null;
        // P05 authored LDtk rectangles replace the P04 camera-followed haze.
        // A zero-zone older project deliberately retains the P04 fallback.
        if (_ldtkParticleZones is { Zones.Count: > 0 })
            ambientOrigin = null;
        try
        {
            _particleVfx.ConfigureEnvironment(_ldtkLevelMode ? _ldtkParticleZones : null);
            _particleWarning = null;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // An invalid cosmetic zone must never prevent the gameplay scene
            // from committing. Display the authoring error in the debug HUD.
            _particleVfx.ConfigureEnvironment(null);
            _particleWarning = "P05 ZONE: " + ex.Message;
            ambientOrigin = null;
        }
        _particleVfx.Presenter.EnterWorld(world.WorldInstanceId, ambientOrigin);
    }

    private Action<ParticleVisualLayer>? CreateGymParticleDraw(WorldSession world,
        SpriteBatch batch)
    {
        if (_particleVfx is null || !world.HasPlayer(1) || !world.HasPlayer(2))
            return null;
        Vector2D one = world.GetPlayer(1).Bounds.Center;
        Vector2D two = world.GetPlayer(2).Bounds.Center;
        ParticleViewport view = ParticleViewportAdapters.FromMovementGym(
            GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height,
            (one.X + two.X) * .5);
        return layer => _particleVfx.DrawLayer(batch, view, layer);
    }

    private void PlayParticleUi(ParticleUiFeedback feedback)
    {
        if (_particleVfx is null) return;
        _particleVfx.Presenter.PresentUi(feedback, new Vector2D(
            GraphicsDevice.Viewport.Width * .67,
            GraphicsDevice.Viewport.Height * .51));
    }

    protected override void Update(GameTime gameTime)
    {
        if (!string.IsNullOrWhiteSpace(_poVisualOutput))
        {
            if (!IsActive) return;
            if (!_poVisualStarted && (IsPerformanceLab || IsQualityLab || IsSpatialLab) && _flow.ActiveScene == MajorSceneKind.Gameplay &&
                !_flow.IsMenuOpen && !_flow.IsTransitionActive && _worldClockTest is not null && _gameLighting is not null)
            {
                ApplyPhase21LabPreset(5);
                SetPerfQuality(LightingQuality.High);
                string? visualMask = Environment.GetEnvironmentVariable("PS_PO_AUTO_MASK");
                if (visualMask is not null && (!int.TryParse(visualMask, out int selectedMask) || selectedMask < 0 || selectedMask > 127))
                    throw new InvalidOperationException("Invalid visual optimization mask.");
                _perfMask = visualMask is null ? 127 : int.Parse(visualMask);
                _perfFreeze = Environment.GetEnvironmentVariable("PS_PO_B_MOTION") != "1";
                if (Environment.GetEnvironmentVariable("PS_PO_10_SOFT") == "1") _shadowEdgePreset = ShadowEdgePreset.Soft8Px;
                _perfOverlay = false;
                _phase21HullOverlay = false;
                _ldtkCamera = null;
                _poVisualStarted = true;
            }
            if (VisualReady) return;
            _perfFixedTotal += TargetElapsedTime;
            UpdateFrame(new GameTime(_perfFixedTotal, TargetElapsedTime));
            if (_poVisualStarted) _poVisualTicks++;
            return;
        }
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        var perfKeys = Keyboard.GetState();
        bool toggle = perfKeys.IsKeyDown(Keys.F) &&
            (perfKeys.IsKeyDown(Keys.LeftControl) || perfKeys.IsKeyDown(Keys.RightControl)) &&
            (perfKeys.IsKeyDown(Keys.LeftShift) || perfKeys.IsKeyDown(Keys.RightShift));
        if (toggle && !_perfToggleDown && !InteractiveLab) _perfOverlay = !_perfOverlay;
        _perfToggleDown = toggle;
        bool capture = perfKeys.IsKeyDown(Keys.B) &&
            (perfKeys.IsKeyDown(Keys.LeftControl) || perfKeys.IsKeyDown(Keys.RightControl)) &&
            (perfKeys.IsKeyDown(Keys.LeftShift) || perfKeys.IsKeyDown(Keys.RightShift));
        if (capture && !_poCaptureKeyDown && !InteractiveLab && _flow.ActiveScene == MajorSceneKind.Gameplay && !_flow.IsMenuOpen)
        {
            if (PerfCaptureActive) CancelPerfCapture(); else BeginPerfCapture();
        }
        _poCaptureKeyDown = capture;
        try
        {
            ApplyPerfPacing(gameTime);
            if (!_perfVariableLoop)
            {
                _perfUpdates++;
                UpdateFrame(gameTime);
            }
            else
            {
                // The framework's variable loop may call Update hundreds of
                // times a second. Never step physics at that variable rate.
                long tick = TargetElapsedTime.Ticks;
                _perfFixedRemainder += Math.Clamp(gameTime.ElapsedGameTime.Ticks,0L,MaxElapsedTime.Ticks);
                while (_perfFixedRemainder >= tick)
                {
                    // Give Draw a chance to record the exact warmup/end boundary. Do not advance
                    // an extra physics tick when one Update contains multiple catch-up steps.
                    if(PbBenchmarkBoundaryReached) { _perfFixedRemainder=0;break; }
                    _perfFixedRemainder -= tick;
                    _perfFixedTotal += TargetElapsedTime;
                    _perfUpdates++;
                    UpdateFrame(new GameTime(_perfFixedTotal,TargetElapsedTime));

                }
            }
        }
        finally { _perfUpdateMs += PerfElapsed(started); }
    }

    private void UpdateFrame(GameTime gameTime)
    {
        using var diagnosticUpdateScope = _diagnostics?.MonitorOperation("Game1.Update");
        MouseState uiMouse = Mouse.GetState();
        bool uiClicked = uiMouse.LeftButton == ButtonState.Pressed && !_previousUiMouseLeftDown;
        _previousUiMouseLeftDown = uiMouse.LeftButton == ButtonState.Pressed;
        Point uiPoint = new(uiMouse.X, uiMouse.Y);
        if (_lightingBasicsMode && _lightingBasicsLab is BasicLightingLab basicsLab)
        {
            basicsLab.Update(Keyboard.GetState(), Mouse.GetState(),
                GraphicsDevice.PresentationParameters.BackBufferWidth,
                GraphicsDevice.PresentationParameters.BackBufferHeight,
                elapsedSeconds: gameTime.ElapsedGameTime.TotalSeconds);
            if (basicsLab.ExitRequested) Exit();
            base.Update(gameTime);
            return;
        }
        // Level-select can enter this same lab as a normal scene. Preserve the
        // flow controller's pause, restart, transition and return-to-title paths.
        if (!_lightingBasicsMode && IsLightingBasicsScene)
        {
            _lightingBasicsLab ??= new BasicLightingLab(GraphicsDevice, Content);
            KeyboardState labKeys = Keyboard.GetState();
            GamePadState labPad = GamePad.GetState(PlayerIndex.One);
            if (!HandleGraphicsMenu(labKeys,labPad,uiClicked,uiPoint)) HandleSceneNavigation(labKeys, labPad, uiClicked, uiPoint);
            bool sceneChanged = _flow.AdvanceTransition(
                gameTime.ElapsedGameTime.TotalSeconds);
            if (sceneChanged)
            {
                _lightingBasicsLab.Dispose();
                _lightingBasicsLab = null;
                ActivateParticleWorld();
                ActivateLightingWorld();
                UpdateAudioListener();
                if (IsLightingBasicsScene)
                    _lightingBasicsLab = new BasicLightingLab(GraphicsDevice, Content);
            }
            else
            {
                _lightingBasicsLab.Update(labKeys, Mouse.GetState(),
                    GraphicsDevice.PresentationParameters.BackBufferWidth,
                    GraphicsDevice.PresentationParameters.BackBufferHeight,
                    interactive: !_flow.IsGameplaySuspended, standalone: false,
                    elapsedSeconds: gameTime.ElapsedGameTime.TotalSeconds);
            }
            if (_flow.ExitRequested) Exit();
            _previousKeyboard = labKeys;
            _previousGamepad = labPad;
            if (++_titleRefreshCounter >= 6)
            {
                _titleRefreshCounter = 0;
                UpdateWindowTitle();
            }
            base.Update(gameTime);
            return;
        }
        if (_materialWorkbench is SurfaceMaterialWorkbench materialLab)
        {
            materialLab.Update(Keyboard.GetState(), Mouse.GetState());
            if (materialLab.ExitRequested) Exit();
            base.Update(gameTime);
            return;
        }
        if (_lightingPlayground is LightingPlayground lightingLab)
        {
            lightingLab.Update(gameTime.ElapsedGameTime.TotalSeconds,
                Keyboard.GetState(), Mouse.GetState(),
                GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            if (lightingLab.ExitRequested) Exit();
            base.Update(gameTime);
            return;
        }
        if (_particlePlayground is ParticlePlayground particleLab)
        {
            particleLab.Update(gameTime.ElapsedGameTime.TotalSeconds,
                Keyboard.GetState(), Mouse.GetState(),
                GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height);
            if (particleLab.ExitRequested) Exit();
            base.Update(gameTime);
            return;
        }
        // Listener uses only committed player state from the previous tick.
        // It is intentionally never part of a solver or gameplay command.
        long audioStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_audio is not null)
        {
            UpdateAudioListener();
            _audio.Update(gameTime.ElapsedGameTime.TotalSeconds);
        }
        _music?.Update(gameTime.ElapsedGameTime.TotalSeconds);
        _perfAudioMs += PerfElapsed(audioStarted);
        if (_audioControlNoticeSeconds > 0)
            _audioControlNoticeSeconds = Math.Max(0,
                _audioControlNoticeSeconds - gameTime.ElapsedGameTime.TotalSeconds);
        if (_lightingPresetNoticeSeconds > 0)
            _lightingPresetNoticeSeconds = Math.Max(0,
                _lightingPresetNoticeSeconds - gameTime.ElapsedGameTime.TotalSeconds);
        KeyboardState keys = Keyboard.GetState();
        // Dev lighting controls are visible, clickable UI buttons; no hidden chords.
        if (_lightingEvents is not null)
        {
            _lightingEvents.Enabled = _lightingEnabled;
            _lightingEvents.Quality = _lightingQuality;
            _lightingEvents.ReducedFlashes = _reducedLightingFlashes;
        }
        // Sound, diagnostics and volume adjustments are also available in the
        // visible development panel; ordinary gameplay input is unaffected.
        GamePadState gamepadState = GamePad.GetState(PlayerIndex.One);
        LocalDeviceInput keyboard = _inputBindings.CaptureKeyboard(keys);
        LocalDeviceInput gamepad = _inputBindings.CaptureGamepad(gamepadState);
        _gamepadOneConnected = gamepad.IsConnected;
        // Presentation-only debug overlay cycle. Available in every gameplay
        // scene without consuming movement, ability or menu input.
        // LDtk uses the visible tool panel instead of undocumented key chords.
        if (_flow.ActiveScene == MajorSceneKind.Gameplay && !_flow.IsMenuOpen &&
            !_ldtkLevelMode && JustPressed(keys, Keys.OemTilde))
        {
            _debugHudMode = GameDebugHudModes.Next(_debugHudMode);
            _debugHudUserSelected = true;
        }
        // A semantic UI command has priority over gameplay and gym shortcuts.
        // Scene preparation/commit occurs here, strictly outside world.Step().
        bool wasSuspended = _paused;
        bool cancelledSmoke = _sceneFlowSmoke.IsRunning &&
            JustPressed(keys, Keys.Escape);
        if (cancelledSmoke)
            _sceneFlowSmoke.Abort("USER CANCELLED");
        bool toolClicked = false;
        bool panelWasFocused = _labControls.Focused;
        bool panelHandled = HandleManualLab(keys,gamepadState,uiClicked,uiPoint);
        bool graphicsHandled = HandleGraphicsMenu(keys,gamepadState,uiClicked,uiPoint);
        bool panelCaptured = panelHandled || panelWasFocused || _labControls.Focused;
        if (panelCaptured)
        {
            // Leave authoritative simulation running, but never route UI input to the player.
            keyboard = new LocalDeviceInput(true,false,false,false);
            gamepad = new LocalDeviceInput(gamepad.IsConnected,false,false,false);
        }
        else if (_labInputWasCaptured)
            _inputRouter.SelectSource(_inputRouter.Source,_inputRouter.Source == LocalInputSource.Keyboard ? keyboard : gamepad);
        _labInputWasCaptured=panelCaptured;
        toolClicked=panelHandled || graphicsHandled;
        if (!IsPerformanceLab && !IsQualityLab && !IsSpatialLab && _ldtkLevelMode && _flow.ActiveScene == MajorSceneKind.Gameplay &&
            !_flow.IsMenuOpen && !_flow.IsTransitionActive)
        {
            if (!toolClicked)
            {
                LightingToolAction action = _lightingTools.Hit(GraphicsDevice.Viewport.Width,
                    GraphicsDevice.Viewport.Height, uiPoint.X, uiPoint.Y, uiClicked);
                if (action != LightingToolAction.None)
                {
                    ApplyLightingToolAction(action);
                    if (IsPerformanceLab) CancelPerfCapture();
                    toolClicked = true;
                }
            }
        }
        if (!cancelledSmoke && !_sceneFlowSmoke.IsRunning && !graphicsHandled && !panelHandled)
            HandleSceneNavigation(keys, gamepadState, uiClicked && !toolClicked, uiPoint);
        if (InteractiveLab || IsSpatialLab || _flow.IsMenuOpen || _flow.ActiveScene != MajorSceneKind.Gameplay)
            _lightingTools.Close();
        // Curtain timing uses presentation time, never world time. Scene
        // preparation runs only after the covered phase on a safe Update.
        bool changedScene = _flow.AdvanceTransition(
            gameTime.ElapsedGameTime.TotalSeconds);
        // SM Phase 6: a real coordinator/curtain smoke sequence, not a fake
        // scene counter. Execute only at this safe Update boundary, and
        // never step the authoritative world while the smoke test is running.
        if (_sceneFlowSmoke.IsRunning)
        {
            if (CurrentRouteProbe() is SceneFlowProbeSnapshot observation)
            {
                SceneFlowSmokeCommand? next = _sceneFlowSmoke.Advance(observation,
                    _flow.IsTransitionActive, _flow.Curtain.LastSucceeded);
                if (next is SceneFlowSmokeCommand command)
                {
                    SceneQueueResult outcome = command.Operation == SceneFlowSmokeOperation.Restart
                        ? _flow.RequestRestart()
                        : _flow.RequestChangeLevel(command.LevelId, command.EntranceId);
                    if (outcome != SceneQueueResult.Queued)
                        _sceneFlowSmoke.Abort("Coordinator rejected " + outcome);
                    else if (command.Operation == SceneFlowSmokeOperation.ExpectedFailure)
                        _failNextRoutePreparation = true;
                }
            }
            else
                _sceneFlowSmoke.Abort("Lost active route world");
        }
        // IMPORTANT: sample suspension *after* advancing the curtain.
        // The fade-in may end this frame. Before the first resumed physics
        // tick, re-seed held inputs so a menu/transition key does not jump.
        _paused = _flow.IsGameplaySuspended;
        _audio?.SetWorldPaused(_paused);
        long diagnosticStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _diagnostics?.CaptureScene(_flow, _worldClockTest, _gym);
        _perfDiagnosticMs += PerfElapsed(diagnosticStarted);
        if (wasSuspended && !_paused)
        {
            _inputRouter.SelectSource(_inputRouter.Source,
                _inputRouter.Source == LocalInputSource.Keyboard ? keyboard : gamepad);
            _lastPlayerInput = null;
        }
        if (changedScene)
        {
            // Scene-owned sounds never survive the scene that created them.
            // Application/UI audio remains available during transitions.
            _audio?.StopOwner("level");
            // The newly activated scene must supply its own listener before
            // we admit spatial loops, avoiding a brief old-scene pan spike.
            UpdateAudioListener();
            if (_soundscapes is not null)
            {
                string soundScene = _flow.ActiveScene == MajorSceneKind.Gameplay
                    ? (_flow.Scenes.ActiveDestination?.LevelId ?? "")
                    : "TITLE";
                try
                {
                    _soundscapes.ChangeScene(soundScene);
                    ShowMusicStatus();
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // Audio is cosmetic: asset faults never roll back a scene.
                    System.Diagnostics.Debug.WriteLine("A02 scene audio unavailable: " + ex);
                    _music?.Dispose();
                    _music = null;
                    _soundscapes = null;
                    ShowAudioMessage("A02 SCENE MUSIC UNAVAILABLE");
                }
            }
            // Seed current held states at the scene/input-context boundary.
            // An Enter/A button that started gameplay must not become a jump.
            _inputRouter.SelectSource(_inputRouter.Source,
                _inputRouter.Source == LocalInputSource.Keyboard ? keyboard : gamepad);
            _lastPlayerInput = null;
            _lastResolution = null;
            _paused = _flow.IsGameplaySuspended;
            _titleRefreshCounter = 6;
        }
        if (changedScene)
        {
            CancelPerfCapture();
            _lightingTools.Close();
            _lastLightingView = null;
            _lastSelectedLights = Array.Empty<RadialLight>();
            ActivateParticleWorld();
            ActivateLightingWorld();
            _adaptiveAreaSamples.Clear();
            // Construct after the scene has committed. The lab shares the
            // normal menu, but its GPU resources are presentation-owned.
            if (IsLightingBasicsScene)
                _lightingBasicsLab ??= new BasicLightingLab(GraphicsDevice, Content);
        }
        // UI cosmetic clock continues during menus/covered scene transitions.
        // World cosmetics freeze whenever the authoritative world is suspended.
        long particlesStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _particleVfx?.Presenter.Update(gameTime.ElapsedGameTime.TotalSeconds, _paused);
        _perfParticleUpdateMs += PerfElapsed(particlesStarted);
        long lightsStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _lightingEvents?.Update(gameTime.ElapsedGameTime.TotalSeconds, _paused || ((IsPerformanceLab || IsQualityLab) && _perfFreeze));
        _perfLightUpdateMs += PerfElapsed(lightsStarted);
        particlesStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        // Use committed player location for cosmetic culling; no world mutation.
        if (_particleVfx is not null && _worldClockTest is WorldSession particleWorld &&
            particleWorld.HasPlayer(1))
            _particleVfx.UpdateEnvironment(particleWorld.GetPlayer(1).Bounds.Center,
                gameTime.ElapsedGameTime.TotalSeconds, _paused);
        _perfParticleUpdateMs += PerfElapsed(particlesStarted);
        if (_flow.ExitRequested)
        {
            Exit();
            return;
        }
        if (changedScene || _flow.IsGameplaySuspended || _sceneFlowSmoke.IsRunning)
        {
            // No simulation ticks when Title or any gameplay overlay is active.
            // UI input continues through HandleSceneNavigation() every frame.
            _previousKeyboard = keys;
            _previousGamepad = gamepadState;
            if (++_titleRefreshCounter >= 6)
            {
                _titleRefreshCounter = 0;
                UpdateWindowTitle();
            }
            base.Update(gameTime);
            return;
        }

        // Panel arrows/confirm are not gameplay or developer shortcut inputs.
        KeyboardState physicalKeys=keys;
        GamePadState physicalPad=gamepadState;
        if (panelCaptured) { keys=default; gamepadState=default; }
        // Developer fixture changes are typed scene requests, not immediate
        // mid-world mutations. They are committed below before the next tick.
        if (JustPressed(keys, Keys.F4))
            _flow.RequestPlay(_worldClockTest is null
                ? GameLevelIds.SharedWorld : GameLevelIds.MovementGym);
        if (_worldClockTest is not null && JustPressed(keys, Keys.F2))
            _flow.RequestPlay(_authoredWorldMode
                ? GameLevelIds.SharedWorld : GameLevelIds.AuthoredWorld);
        if (_worldClockTest is not null && JustPressed(keys, Keys.F1))
            _flow.RequestPlay(_motionLabMode
                ? GameLevelIds.SharedWorld : GameLevelIds.MotionLab);
        if (_worldClockTest is not null && JustPressed(keys, Keys.T))
            _flow.RequestRestart();
        // Lighting chords must not double as developer level-switch digits.
        // Route and LDtk shortcuts respond to *plain* 1/2/3 only.
        bool unmodifiedLevelDigit =
            !(keys.IsKeyDown(Keys.LeftControl) || keys.IsKeyDown(Keys.RightControl) ||
              keys.IsKeyDown(Keys.LeftAlt) || keys.IsKeyDown(Keys.RightAlt) ||
              keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift));
        // Dedicated lab: direct, discoverable comparison keys, no settings panel.
        if (unmodifiedLevelDigit && _ldtkLighting?.LevelId == GameLevelIds.EmissiveLightingLab)
        {
            int preset = JustPressed(keys, Keys.D1) ? 1 : JustPressed(keys, Keys.D2) ? 2 :
                JustPressed(keys, Keys.D3) ? 3 : JustPressed(keys, Keys.D4) ? 4 :
                JustPressed(keys, Keys.D5) ? 5 : 0;
            if (preset != 0) ApplyEmissionLabPreset(preset);
        }
        if (!toolClicked && unmodifiedLevelDigit && !_flow.IsMenuOpen && !_flow.IsTransitionActive && IsPhase21Lab && !IsQualityLab && !IsPerformanceLab)
        {
            int preset = JustPressed(keys, Keys.D1) ? 1 : JustPressed(keys, Keys.D2) ? 2 :
                JustPressed(keys, Keys.D3) ? 3 : JustPressed(keys, Keys.D4) ? 4 :
                JustPressed(keys, Keys.D5) ? 5 : JustPressed(keys, Keys.D6) ? 6 : 0;
            if (preset != 0) SelectPerfWorkload(preset);
            if (JustPressed(keys, Keys.D7)) TogglePerfHulls();
            if (JustPressed(keys, Keys.D8))
                SetPerfQuality(_lightingQuality switch { LightingQuality.High => LightingQuality.Balanced,
                    LightingQuality.Balanced => LightingQuality.Low, _ => LightingQuality.High });
            if (JustPressed(keys, Keys.D9)) BeginPerfCapture();
        }
        // Direct route-debug shortcuts still use the real, validated exit
        // mapping. One changes level only at this safe game-loop boundary.
        if (_routeLabMode && _flow.Scenes.ActiveDestination is SceneDestination currentRoute)
        {
            // F9 runs an eight-operation scene lifecycle regression using the
            // same fade/prepare/commit path as ordinary gameplay exits.
            if (JustPressed(keys, Keys.F9) &&
                CurrentRouteProbe() is SceneFlowProbeSnapshot startingRoute)
                _sceneFlowSmoke.Start(startingRoute);
            if (_sceneFlowSmoke.IsRunning)
            {
                _previousKeyboard = physicalKeys;
                _previousGamepad = physicalPad;
                base.Update(gameTime);
                return;
            }
            // A one-shot, explicitly simulated content-loading failure.
            // The prior world must remain fully playable after recovery.
            // F10 is unused by the ordinary request gym in this route mode.
            if (JustPressed(keys, Keys.F10))
            {
                string alternate = currentRoute.LevelId == GameLevelIds.RouteA
                    ? GameLevelIds.RouteB : GameLevelIds.RouteA;
                if (_flow.RequestChangeLevel(alternate, RouteLevelGym.WestEntrance)
                    == SceneQueueResult.Queued)
                {
                    _failNextRoutePreparation = true;
                    _lastRouteStatus = "TEST: NEXT ROUTE PREPARATION WILL FAIL";
                }
            }
            int? selectedGate = !unmodifiedLevelDigit ? null :
                JustPressed(keys, Keys.D1) ? RouteLevelGym.LeftGateId :
                JustPressed(keys, Keys.D2) ? RouteLevelGym.RightGateId : null;
            if (selectedGate is int gate &&
                RouteLevelGym.Catalog.TryResolveExit(currentRoute, gate,
                    out SceneDestination? target) && target is not null)
            {
                _flow.RequestChangeLevel(target.LevelId, target.EntranceId);
                _lastRouteStatus = "MANUAL EXIT " + gate + " TO " +
                    target.LevelId + "/" + target.EntranceId;
            }
        }
        // Developer inspection of an LDtk exit without needing to traverse an
        // unfinished art-level layout. Gameplay trigger crossings use the
        // exact same validated exit destinations through committed events.
        if (unmodifiedLevelDigit && !IsQualityLab && _ldtkConnectedLevel is LdtkConnectedLevel authoredLevel &&
            _flow.Scenes.ActiveDestination is SceneDestination authoredDestination)
        {
            int index = JustPressed(keys, Keys.D1) ? 0 :
                JustPressed(keys, Keys.D2) ? 1 :
                JustPressed(keys, Keys.D3) ? 2 : -1;
            if (index >= 0)
            {
                var links = new List<LdtkExitLink>(authoredLevel.Exits.Values);
                links.Sort((a,b) => a.TriggerId.CompareTo(b.TriggerId));
                if (index < links.Count)
                    _flow.RequestChangeLevel(links[index].Destination.LevelId,
                        links[index].Destination.EntranceId);
            }
        }
        // Any developer shortcut that queued a transition must immediately
        // gate gameplay; preparation itself waits for the curtain and the
        // next safe Update. Never simulate a tick after requesting a switch.
        if (_flow.IsTransitionActive)
        {
            _paused = true;
            _previousKeyboard = physicalKeys;
            _previousGamepad = physicalPad;
            UpdateWindowTitle();
            base.Update(gameTime);
            return;
        }

        // Explicit single-player device ownership. The switch seeds held
        // state, preventing a held jump from becoming a new press. It also
        // resets the gym for a reproducible keyboard vs gamepad comparison.
        if (JustPressed(keys, Keys.F8))
        {
            LocalInputSource next = _inputRouter.Source == LocalInputSource.Keyboard
                ? LocalInputSource.GamepadOne : LocalInputSource.Keyboard;
            _inputRouter.SelectSource(next,
                next == LocalInputSource.Keyboard ? keyboard : gamepad);
            if ((_authoredWorldMode || _motionLabMode || _routeLabMode || _ldtkLevelMode) &&
                _worldClockTest is WorldSession sourceWorld)
                sourceWorld.AssignInputSource(AuthoredWorldGym.FirstPlayerId,
                    _inputRouter.SourceId);
            if (_worldClockTest is null)
                ResetRequestTestBaseline();
            _lastPlayerInput = null;
        }

        // These controls edit gameplay configuration; they never mutate the
        // motor's velocity directly. Any change resets to the same spawn.
        if (_worldClockTest is null && JustPressed(keys, Keys.F1))
            ApplyPreset(MovementGymTuning.Baseline, "Baseline");
        if (_worldClockTest is null && JustPressed(keys, Keys.F2))
            ApplyPreset(MovementGymTuning.Responsive, "Responsive");
        if (_worldClockTest is null && JustPressed(keys, Keys.F3))
            ApplyPreset(MovementGymTuning.Snappy, "Snappy");
        if (JustPressed(keys, Keys.F5))
        {
            _gym.SetFaultSensitivity(SimulationFaultSensitivity.Strict);
            _worldClockTest?.SetFaultSensitivity(SimulationFaultSensitivity.Strict);
        }
        if (JustPressed(keys, Keys.F6))
        {
            _gym.SetFaultSensitivity(SimulationFaultSensitivity.Diagnostic);
            _worldClockTest?.SetFaultSensitivity(SimulationFaultSensitivity.Diagnostic);
        }
        if (JustPressed(keys, Keys.F7))
        {
            _gym.SetFaultSensitivity(SimulationFaultSensitivity.Resilient);
            _worldClockTest?.SetFaultSensitivity(SimulationFaultSensitivity.Resilient);
        }
        // The world-clock demo uses a fixed assistance policy for its entire
        // lifetime; F9 remains the ordinary movement-gym test switch.
        // Collision/backdrop toggles for LDtk are now mouse-operated tools.
        if (_worldClockTest is null && JustPressed(keys, Keys.F9))
            _gym.SetAssistancePolicy(_gym.AssistancePolicy.AnyEnabled
                ? CollisionAssistancePolicy.Disabled
                : MovementGymSession.PlayableAssistancePreset);

        // Request resolver gym is presentation/test input only. Each change
        // resets the level so A/B comparisons start from identical geometry.
        if (_worldClockTest is null && JustPressed(keys, Keys.F10))
        {
            _requestTestEnabled = !_requestTestEnabled;
            ResetRequestTestBaseline();
        }
        if (_requestTestEnabled && JustPressed(keys, Keys.F11))
        {
            _requestScenario = MovementRequestGymHarness.Next(_requestScenario);
            ResetRequestTestBaseline();
        }
        if (_requestTestEnabled && JustPressed(keys, Keys.F12))
        {
            _reverseRequestOrder = !_reverseRequestOrder;
            ResetRequestTestBaseline();
        }
        if (_worldClockTest is null && JustPressed(keys, Keys.Tab))
            _selectedParameter = (MovementGymParameter)(
                ((int)_selectedParameter + 1) % Enum.GetValues<MovementGymParameter>().Length);
        if (_worldClockTest is null &&
            (JustPressed(keys, Keys.OemMinus) || JustPressed(keys, Keys.Subtract)))
            AdjustParameter(-1);
        if (_worldClockTest is null &&
            (JustPressed(keys, Keys.OemPlus) || JustPressed(keys, Keys.Add)))
            AdjustParameter(1);

        if (JustPressed(keys, Keys.R))
        {
            if (_worldClockTest is WorldSession world)
            {
                if (world.GetPlayer(1).IsDead)
                {
                    var life = world.GetPlayer(1).Life;
                    if (life.Mode == WorldRespawnMode.Manual && life.RespawnAllowed) world.RequestPlayerRespawn(1);
                }
                else if (_authoredWorldMode || _motionLabMode || _routeLabMode || _ldtkLevelMode)
                    world.RespawnPlayer(1);
                else
                    world.RespawnPlayer(SharedWorldClockGym.FirstPlayerId,
                        SharedWorldClockGym.SpawnBounds(world, SharedWorldClockGym.FirstPlayerId));
            }
            else
                _gym.Reset();
        }
        if (_worldClockTest is WorldSession testWorld &&
            testWorld.HasPlayer(2) && JustPressed(keys, Keys.U))
        {
            if (testWorld.GetPlayer(2).IsDead)
            {
                var life = testWorld.GetPlayer(2).Life;
                if (life.Mode == WorldRespawnMode.Manual && life.RespawnAllowed) testWorld.RequestPlayerRespawn(2);
            }
            else if (_authoredWorldMode || _motionLabMode || _routeLabMode)
                testWorld.RespawnPlayer(2);
            else
                testWorld.RespawnPlayer(SharedWorldClockGym.SecondPlayerId,
                    SharedWorldClockGym.SpawnBounds(testWorld, SharedWorldClockGym.SecondPlayerId));
        }
        if (_worldClockTest is not null && JustPressed(keys, Keys.F3))
        {
            _recentWorldEvents.Clear(); // Presentation only; not world state.
            _latestStateFeedback = "NONE";
            _latestAbilityFeedback = "NONE";
        }
        if (_authoredWorldMode && _worldClockTest is WorldSession authoredWorld)
        {
            if (JustPressed(keys, Keys.N) && !authoredWorld.HasPlayer(AuthoredWorldGym.ThirdPlayerId))
                AuthoredWorldGym.JoinGuest(authoredWorld);
            if (JustPressed(keys, Keys.M) &&
                authoredWorld.HasPlayer(AuthoredWorldGym.ThirdPlayerId))
                authoredWorld.RemovePlayer(AuthoredWorldGym.ThirdPlayerId);
            if (JustPressed(keys, Keys.V))
            {
                WorldPlayerSlotInfo p1Slot = authoredWorld.GetPlayerSlot(AuthoredWorldGym.FirstPlayerId);
                authoredWorld.SetPlayerSpawn(AuthoredWorldGym.FirstPlayerId,
                    p1Slot.SpawnId == AuthoredWorldGym.LeftSpawn
                        ? AuthoredWorldGym.GroundSpawn : AuthoredWorldGym.LeftSpawn);
            }
        }
        if (!_paused)
        {
            // The same device-independent state/edge router feeds both
            // ordinary gameplay and the A08 multi-source request harness.
            LocalPlayerInput input = _inputRouter.Sample(keyboard, gamepad);

            // The test harness submits real typed requests to the SAME
            // authoritative session as ordinary keyboard input. It never
            // writes motor bounds, velocity, support, or jump state.
            if (_requestTestEnabled)
            {
                MovementRequestGymHarness.Populate(_inputRequests,
                    _requestScenario, input.Direction,
                    input.JumpPressed, input.JumpReleased,
                    _reverseRequestOrder, input.SourceId);
            }
            else
            {
                LocalInputRequestAdapter.Populate(_inputRequests, input);
            }

            // One resolution and one authoritative physics step per Update.
            // Preserve only the diagnostics from a successfully completed step.
            if (_worldClockTest is WorldSession clockWorld)
            {
                // Resolve the same A08 requests as the ordinary gym, then
                // associate them with logical PlayerId 1. Player 2 has an
                // independent test source (J/L movement; I jump).
                MovementFrameIntent resolved = MovementRequestResolver.Resolve(_inputRequests);
                HorizontalDirection secondDirection =
                    keys.IsKeyDown(Keys.J) == keys.IsKeyDown(Keys.L)
                        ? HorizontalDirection.Neutral
                        : keys.IsKeyDown(Keys.J)
                            ? HorizontalDirection.Left : HorizontalDirection.Right;
                // A09 gym requests are only interpreted by the WorldSession;
                // the host never changes state, velocity or hitboxes directly.
                _characterStateRequests.Clear();
                if (JustPressed(keys, Keys.Z))
                {
                    CharacterActionState action = clockWorld.GetPlayer(1).State.Action;
                    _characterStateRequests.Add(new CharacterStateRequest(1,
                        action == CharacterActionState.None
                            ? CharacterStateRequestKind.BeginAction
                            : CharacterStateRequestKind.EndAction));
                }
                if (JustPressed(keys, Keys.X))
                    _characterStateRequests.Add(new CharacterStateRequest(1,
                        CharacterStateRequestKind.ApplyStun, 90));
                if (JustPressed(keys, Keys.C))
                    _characterStateRequests.Add(new CharacterStateRequest(1,
                        CharacterStateRequestKind.ClearStun));
                if (clockWorld.HasPlayer(2) && JustPressed(keys, Keys.O))
                    _characterStateRequests.Add(new CharacterStateRequest(2,
                        CharacterStateRequestKind.ApplyStun, 90));
                // A10: ability requests are semantic and player attributed.
                // Do not edit hitboxes, velocity, cooldowns or energy in Game1.
                _characterAbilityRequests.Clear();
                if (JustPressed(keys, Keys.B) && !_poCaptureKeyDown)
                    _characterAbilityRequests.Add(new CharacterAbilityRequest(1,
                        CharacterAbilityId.Pulse, CharacterAbilityRequestKind.Activate));
                if (JustPressed(keys, Keys.G))
                    _characterAbilityRequests.Add(new CharacterAbilityRequest(1,
                        CharacterAbilityId.Focus, CharacterAbilityRequestKind.Activate));
                if (JustPressed(keys, Keys.H))
                    _characterAbilityRequests.Add(new CharacterAbilityRequest(1,
                        CharacterAbilityId.Pulse, CharacterAbilityRequestKind.Cancel));
                if (JustPressed(keys, Keys.K))
                    _characterAbilityRequests.Add(new CharacterAbilityRequest(1,
                        CharacterAbilityId.Focus, CharacterAbilityRequestKind.Cancel));
                if (clockWorld.HasPlayer(2) && JustPressed(keys, Keys.Y))
                    _characterAbilityRequests.Add(new CharacterAbilityRequest(2,
                        CharacterAbilityId.Pulse, CharacterAbilityRequestKind.Activate));
                _worldCommands.Clear();
                _worldCommands.Add(new WorldPlayerCommand(
                    SharedWorldClockGym.FirstPlayerId, resolved.Direction,
                    resolved.JumpPressed, resolved.JumpReleased));
                if(IsSpatialLab && _poVisualStarted) _worldCommands[0]=new WorldPlayerCommand(1,HorizontalDirection.Right, clockWorld.WorldTick%90==20,clockWorld.WorldTick%90==45);
                if (_ldtkLevel?.World.LevelId == GameLevelIds.EngineIntegrationLab && PbScriptEnabled)
                    _worldCommands[0] = new WorldPlayerCommand(1, clockWorld.WorldTick % 240 < 120 ? HorizontalDirection.Right : HorizontalDirection.Left,
                        clockWorld.WorldTick % 90 == 20, clockWorld.WorldTick % 90 == 45);
                if (clockWorld.HasPlayer(SharedWorldClockGym.SecondPlayerId))
                    _worldCommands.Add(new WorldPlayerCommand(
                        SharedWorldClockGym.SecondPlayerId, secondDirection,
                        JustPressed(keys, Keys.I),
                        keys.IsKeyUp(Keys.I) && _previousKeyboard.IsKeyDown(Keys.I)));
                _motionRequests.Clear();
                // LDtk authored wind zones are event-driven. Only committed
                // enter/exit results change the desired environment velocity;
                // a typed request applies that velocity on the NEXT world tick.
                if (_ldtkConnectedLevel is LdtkConnectedLevel activeContent && _ldtkWindDirty)
                {
                    double fraction = 0.0;
                    foreach (int triggerId in _ldtkActiveWindIds)
                        fraction += activeContent.WindRegions[triggerId].WalkingSpeedFraction;
                    _motionRequests.Add(new WorldMotionRequest(1,
                        WorldMotionRequestKind.SetEnvironmentVelocity,
                        fraction * MotionContributionGym.NormalWalkingSpeed));
                }
                if (_motionLabMode)
                {
                    // A wind-region fixture: persistent signed world velocity,
                    // not a key-press priority override or direct physics edit.
                    if (JustPressed(keys, Keys.Q))
                    {
                        _windSelection = (_windSelection + 1) % 3;
                        double wind = _windSelection switch
                        {
                            1 => MotionContributionGym.WindLeft75,
                            2 => MotionContributionGym.WindRight75,
                            _ => 0.0
                        };
                        _motionRequests.Add(new WorldMotionRequest(1,
                            WorldMotionRequestKind.SetEnvironmentVelocity, wind));
                    }
                    if (JustPressed(keys, Keys.E))
                        _motionRequests.Add(new WorldMotionRequest(1,
                            WorldMotionRequestKind.AddHorizontalImpulse,
                            MotionContributionGym.KnockbackAgainstFacing(
                                clockWorld.GetPlayerMotion(1))));
                }
#if DEBUG
                // D01 Phase 2 failed-tick acceptance: commit tick 1 normally, then
                // submit one duplicate player command for tick 2. WorldSession.Step
                // rejects duplicates before forking any simulation state. This
                // produces a genuine in-Step exception while retaining tick 1 as
                // the last known committed state and tick 2 as an in-flight attempt.
                // No fault is injected in Release builds or normal Debug runs.
                if (_devFailInsideWorldStep && clockWorld.WorldTick == 1 &&
                    _worldCommands.Count > 0)
                    _worldCommands.Add(_worldCommands[0]);
#endif
                long diagStart = System.Diagnostics.Stopwatch.GetTimestamp();
                _diagnostics?.BeginWorldStep(_flow, clockWorld, _gym.FaultSensitivity,
                    _worldCommands, _characterStateRequests, _characterAbilityRequests,
                    _motionRequests);
                _perfDiagnosticMs += PerfElapsed(diagStart);
                long simulationStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                ApplyPbScript(clockWorld);
                WorldStepResult committed = clockWorld.Step(_worldCommands,
                    _characterStateRequests, _characterAbilityRequests,
                    _motionRequests);
                _perfSimulationMs += PerfElapsed(simulationStarted);
                diagStart = System.Diagnostics.Stopwatch.GetTimestamp();
                _diagnostics?.CompleteWorldStep(_flow, clockWorld, _gym.FaultSensitivity, committed);
                _perfDiagnosticMs += PerfElapsed(diagStart);
#if DEBUG
                // Diagnostic-only test: block the game thread after a committed
                // tick so a separate watchdog thread can write a stall report.
                if (_devStallAfterWorldStep && !_devStallInjected)
                {
                    _devStallInjected = true;
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(4));
                }
#endif
#if DEBUG
                if (_devCrashAfterWorldStep)
                    throw new InvalidOperationException("D01 Phase 2 intentional gameplay-context crash test.");
#endif
                if (_ldtkConnectedLevel is not null) _ldtkWindDirty = false;
                // Consume only successfully committed semantic events. VFX is
                // presentation-only and never enters the fixed-step solver.
                long eventsStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                _particleVfx?.Presenter.PresentCommitted(committed);
                foreach (WorldGameplayEvent gameplayEvent in committed.Events)
                {
                    // A01: only post-commit world events create game audio.
                    // This never calls from WorldSession.Step() or a solver.
                    _audioEvents?.Present(gameplayEvent);
                    _lightingEvents?.Present(gameplayEvent);
                    if (gameplayEvent.Kind == WorldGameplayEventKind.PlayerRespawned)
                    {
                        if (gameplayEvent.PlayerId == 1) _worldP1Respawns++;
                        if (gameplayEvent.PlayerId == 2) _worldP2Respawns++;
                    }
                    if(PbTickBenchmark&&_pbTickWarmupComplete)
                    {
                        if(gameplayEvent.Kind==WorldGameplayEventKind.PlayerDied) _pbMeasuredDeaths++;
                        if(gameplayEvent.Kind==WorldGameplayEventKind.PlayerRespawned) _pbMeasuredRespawns++;
                    }
                    if ((gameplayEvent.Kind is WorldGameplayEventKind.PlayerRespawned or WorldGameplayEventKind.PlayerDied) &&
                        gameplayEvent.PlayerId == 1 && _ldtkConnectedLevel is not null)
                    {
                        // Respawn clears the authoritative external-motion state.
                        // Discard old wind membership before any destination entries.
                        _ldtkActiveWindIds.Clear();
                        _ldtkWindDirty = true;
                    }
                    if (gameplayEvent.Kind == WorldGameplayEventKind.CharacterStateRequestDenied)
                        _latestStateFeedback = "DENIED P" + gameplayEvent.PlayerId + " " +
                            gameplayEvent.StateRequestKind + " " + gameplayEvent.Rejection;
                    else if (gameplayEvent.Kind == WorldGameplayEventKind.CharacterStateChanged &&
                             gameplayEvent.StateChange is WorldCharacterStateChange change &&
                             change.Lane != CharacterStateLane.Locomotion)
                        _latestStateFeedback = "P" + gameplayEvent.PlayerId + " " +
                            change.Lane + " " + change.Current;
                    if (gameplayEvent.AbilityId is CharacterAbilityId abilityId)
                        _latestAbilityFeedback = "P" + gameplayEvent.PlayerId + " " +
                            abilityId + " " + (gameplayEvent.AbilityRejection is { } rejected
                                ? "DENIED " + rejected
                                : gameplayEvent.Kind.ToString().Replace("Ability", ""));
                    // A confirmed end-of-tick trigger entry may request the
                    // next level, but cannot replace the world during Step().
                    // P1 owns exits in this two-player test; P2 continues to
                    // be fully independent within the current level.
                    if (_routeLabMode &&
                        gameplayEvent.Kind == WorldGameplayEventKind.TriggerEntered &&
                        gameplayEvent.PlayerId == 1 &&
                        gameplayEvent.TriggerId is int exitTrigger &&
                        _flow.Scenes.ActiveDestination is SceneDestination from &&
                        RouteLevelGym.Catalog.TryResolveExit(from, exitTrigger,
                            out SceneDestination? nextLevel) && nextLevel is not null &&
                        _flow.RequestChangeLevel(nextLevel.LevelId,
                            nextLevel.EntranceId) == SceneQueueResult.Queued)
                        _lastRouteStatus = "P1 EXIT " + exitTrigger + " TO " +
                            nextLevel.LevelId + "/" + nextLevel.EntranceId;
                    if (_ldtkConnectedLevel is LdtkConnectedLevel authored &&
                        gameplayEvent.PlayerId == 1 && gameplayEvent.TriggerId is int authoredTrigger)
                    {
                        if (gameplayEvent.Kind == WorldGameplayEventKind.TriggerEntered)
                        {
                            if (authored.Checkpoints.TryGetValue(authoredTrigger,
                                out LdtkCheckpointLink? checkpoint))
                            {
                                clockWorld.SetPlayerSpawn(1, checkpoint.SpawnId);
                                _lastLdtkInteraction = "CHECKPOINT " + checkpoint.SpawnId;
                            }
                            if (authored.WindRegions.ContainsKey(authoredTrigger) &&
                                _ldtkActiveWindIds.Add(authoredTrigger))
                            {
                                _ldtkWindDirty = true;
                                _lastLdtkInteraction = "WIND ENTER " + authoredTrigger;
                            }
                            if (authored.Exits.TryGetValue(authoredTrigger, out LdtkExitLink? exit))
                            {
                                if (_flow.RequestChangeLevel(exit.Destination.LevelId,
                                    exit.Destination.EntranceId) == SceneQueueResult.Queued)
                                    _lastLdtkInteraction = "EXIT TO " + exit.Destination.LevelId;
                            }
                        }
                        else if (gameplayEvent.Kind == WorldGameplayEventKind.TriggerExited &&
                            authored.WindRegions.ContainsKey(authoredTrigger) &&
                            _ldtkActiveWindIds.Remove(authoredTrigger))
                        {
                            _ldtkWindDirty = true;
                            _lastLdtkInteraction = "WIND EXIT " + authoredTrigger;
                        }
                    }
                    _recentWorldEvents.Add(gameplayEvent);
                    if (_recentWorldEvents.Count > 8)
                        _recentWorldEvents.RemoveAt(0);
                }
                // A moving Pulse's light must use the newly committed player
                // position, not the frozen position of the activation event.
                // Presentation-only: no feedback to the movement simulation.
                _lightingEvents?.FollowCommittedPlayers(committed);
                _perfEventsMs += PerfElapsed(eventsStarted);
            }
            else
            {
                _diagnostics?.BeginGymStep(_flow, _gym, input);
                long gymStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                _gym.Step(MovementGymSession.FixedStepSeconds, _inputRequests,
                    out MovementResolution committedResolution);
                _perfSimulationMs += PerfElapsed(gymStarted);
                _diagnostics?.CompleteGymStep(_flow, _gym);
                _lastResolution = committedResolution;
            }
            _lastPlayerInput = input;
            _lastKeyboardDirection = input.Direction;
        }

        _previousKeyboard = physicalKeys;
        _previousGamepad = physicalPad;
        if (++_titleRefreshCounter >= 6)
        {
            _titleRefreshCounter = 0;
            UpdateWindowTitle();
        }
        base.Update(gameTime);
    }

    // Wall-clock draw cadence includes waiting/present and update catch-up.
    // It is deliberately independent of GameTime's fixed simulation timestep.
    private readonly LightingCpuTimeWindow _frameIntervals = new(120);
    private readonly LiveFrameReadout _liveFrameReadout = new();
    private long _perfLastDrawStart;
    private double _perfUpdateMs, _perfLastUpdateMs, _perfLastDrawMs;
    private int _perfUpdates, _perfLastUpdates;
    private double _perfSimulationMs, _perfAudioMs, _perfParticleUpdateMs, _perfLightUpdateMs, _perfDiagnosticMs, _perfEventsMs;
    private double _perfSceneDrawMs, _perfParticleDrawMs, _perfLightPrepareMs, _perfHudMs, _perfCameraMs;
    private double _perfDisplaySimMs, _perfDisplayAudioMs, _perfDisplayParticleMs, _perfDisplayDiagMs, _perfDisplaySceneMs, _perfDisplayHudMs;
    private int _perfDrawnParticles, _perfWorldEffects, _perfUiEffects;
    private long _perfAllocatedAtDrawStart, _perfFrameAllocatedBytes;
    private int _perfGc0, _perfGc1, _perfGc2, _perfFrameGc0, _perfFrameGc1, _perfFrameGc2;
    private bool _perfPreviousLightingActive;
    private long _perfDrawCalls, _perfPrimitives, _perfSprites, _perfTargetChanges;
    private bool _perfUncappedRequested = !EnginePerformanceOptions.InitialVSync, _perfUncappedActive = !EnginePerformanceOptions.InitialVSync;
    private bool _perfVariableLoop, _poCaptureKeyDown;
    // Explicit, opt-in, one-shot automation. A fresh process owns each case
    // because EnginePerformanceOptions are process-lifetime switches.
    private readonly string? _poAutoOutput = Environment.GetEnvironmentVariable("PS_PO_AUTO_OUTPUT");
    private readonly string? _poVisualOutput = Environment.GetEnvironmentVariable("PS_PO_VISUAL_OUTPUT");
    private bool _poVisualStarted;
    private int _poVisualTicks, _poVisualDraws;
    private static readonly int VisualTicks = int.TryParse(Environment.GetEnvironmentVariable("PS_PO_VISUAL_TICKS"), out int visualTicks) && visualTicks is >= 30 and <= 240 ? visualTicks : 60;
    private bool VisualReady => _poVisualStarted && _poVisualTicks >= VisualTicks;
    private bool _poAutoStarted;
    private bool _poAutoSaved;
    private int _poAutoMask, _poAutoIsolation;
    private int _poAutoWarmupFrames = 120, _poAutoRecordedFrames = 300;
    private int _poMinimumWarmup, _poMaximumWarmup;
    private BenchmarkWarmupPolicy? _poWarmupPolicy;
    private string _poWarmupStatus = "fixed";
    private readonly List<string> _poWarmupTrace = new();
    private LightingViewport? _poPreviousView;
    private Aabb? _poPreviousPlayerBounds;
    private long _poPreviousMaskSerial = -1;
    private int _poPreviousLightHash;
    private bool _poPreviousLightHashValid;
    private Camera2DView? _poLockedCamera;
    private bool PoAutomated => _poAutoStarted || _poVisualStarted;
    private string PoFixture => Environment.GetEnvironmentVariable("PS_PO_10C_FIXTURE") ?? "normal";
    private bool PoLockCamera => Environment.GetEnvironmentVariable("PS_PO_10C_LOCK_CAMERA") == "1";
    private int PoLightGeometryHash()
    {
        var h = new HashCode();
        foreach (var l in _lastSelectedLights)
        { h.Add(l.Id); h.Add(l.Position); h.Add(l.Radius); h.Add(l.Shape); h.Add(l.Direction); h.Add(l.TubeLength); h.Add(l.EmitterWidth); h.Add(l.EmitterFootprint); h.Add(l.DirectionalShadowLength); h.Add(l.AreaSampleCount); h.Add(l.CastShadows); h.Add(l.VirtualZ); }
        return h.ToHashCode();
    }
    private int ActivePerfWarmup => _poAutoStarted ? _poAutoWarmupFrames : InteractiveLab ? _manualWarmup : 120;
    private int ActivePerfRecorded => _poAutoStarted ? _poAutoRecordedFrames : InteractiveLab ? _manualRecorded : 300;
    private static bool TryReadAutoFrameCount(string name, int fallback, int minimum, int maximum, out int value)
    {
        string? raw = Environment.GetEnvironmentVariable(name);
        if (raw is null) { value = fallback; return true; }
        return int.TryParse(raw, out value) && value >= minimum && value <= maximum;
    }
    private bool _poAutoFreeze;
    private void StartAutomatedPerfCaptureIfReady()
    {
        if (string.IsNullOrWhiteSpace(_poAutoOutput) || _poAutoStarted ||
            !(IsPerformanceLab || IsQualityLab) || !IsActive || !_perfVariableLoop ||
            _flow.ActiveScene != MajorSceneKind.Gameplay || _flow.IsMenuOpen ||
            _flow.IsTransitionActive || _worldClockTest is null) return;
        if (_gameLighting is null || _lightingEvents?.Scene is null)
        {
            Console.Error.WriteLine("PO benchmark: lighting renderer or authored scene unavailable.");
            _poAutoStarted = true;
            Exit();
            return;
        }
        if (!int.TryParse(Environment.GetEnvironmentVariable("PS_PO_AUTO_MASK"), out int mask) || mask < 0 || mask > 127 ||
            !int.TryParse(Environment.GetEnvironmentVariable("PS_PO_AUTO_ISOLATION"), out int isolation) || isolation < 0 || isolation >= PerfDiagnostics.Length)
        {
            Console.Error.WriteLine("PO benchmark: invalid mask or isolation.");
            _poAutoStarted = true;
            Exit();
            return;
        }
        if (!TryReadAutoFrameCount("PS_PO_AUTO_WARMUP_FRAMES", 120, 30, 1200, out _poAutoWarmupFrames) ||
            !TryReadAutoFrameCount("PS_PO_AUTO_RECORDED_FRAMES", 300, 60, 3000, out _poAutoRecordedFrames))
        {
            Console.Error.WriteLine("PO benchmark: invalid warmup or recorded frame count.");
            _poAutoStarted = true;
            Exit();
            return;
        }
        _poMinimumWarmup = _poAutoWarmupFrames;
        if (!TryReadAutoFrameCount("PS_PO_AUTO_MAX_WARMUP", _poMinimumWarmup, _poMinimumWarmup, 1200, out _poMaximumWarmup))
        { Console.Error.WriteLine("PO benchmark: invalid maximum warmup"); _poAutoStarted = true; Exit(); return; }
        if (PoFixture is not ("normal" or "adaptive" or "static" or "static_shadow"))
        { Console.Error.WriteLine("PO benchmark: invalid fixture"); _poAutoStarted = true; Exit(); return; }
        ApplyPhase21LabPreset(Environment.GetEnvironmentVariable("PS_PO_10B_STRESS") == "1" ? 6 : 5);
        SetPerfQuality(Enum.TryParse<LightingQuality>(Environment.GetEnvironmentVariable("PS_PO_B_QUALITY"), out var quality) && Enum.IsDefined(quality) ? quality : LightingQuality.High);
        _perfMask = _poAutoMask = mask;
        _perfIsolation = _poAutoIsolation = isolation;
        _perfFreeze = _poAutoFreeze = Environment.GetEnvironmentVariable("PS_PO_B_MOTION") != "1";
        // PO10A also exercises actual Gaussian passes, not just the hard-shadow default.
        if (Environment.GetEnvironmentVariable("PS_PO_10_SOFT") == "1")
            _shadowEdgePreset = ShadowEdgePreset.Soft8Px;
        _phase21HullOverlay = false;
        _poAutoStarted = true;
        if (Environment.GetEnvironmentVariable("PS_PO_AUTO_ADAPTIVE_WARMUP") == "1" && _poAutoFreeze)
        {
            _poWarmupPolicy = new BenchmarkWarmupPolicy(_poMinimumWarmup, _poMaximumWarmup);
            _poAutoWarmupFrames = _poMaximumWarmup;
            _poWarmupStatus = "warming";
        }
        BeginPerfCapture();
    }
    private double _poPresentWallMs;
    private long _pbMeasurementStartTick;
    private string? _poCaptureScene;
    private long? _poCaptureWorld;
    private bool _poCaptureLighting, _poCaptureOverlay;
    private LightingQuality _poCaptureQuality;
    private string? _poCapturePreset;
    private double _poHudP95, _poLightP95;
    private long _poHudNextRefresh;
    private string _poFpsLine = "FPS -", _poFrameLine = "FRAME -", _poUpdateLine = "UPDATE -", _poCountLine = "CPU WALL TIME";
    private void RefreshPerfHud(long now)
    {
        if (now < _poHudNextRefresh) return;
        _poHudNextRefresh = now + System.Diagnostics.Stopwatch.Frequency / 4;
        _poHudP95 = _frameIntervals.P95;
        _poLightP95 = _gameLighting?.CpuSubmitWindow.P95 ?? 0;
        double fps = _frameIntervals.Mean > 0 ? 1000 / _frameIntervals.Mean : 0;
        _poFpsLine = _frameIntervals.Mean > 0 ? $"FPS {fps:F1}" : "FPS -";
        _poFrameLine = $"FRAME {_frameIntervals.Mean:F2} MS / P95 {_poHudP95:F2}";
        _poUpdateLine = $"UPDATE {_perfLastUpdateMs:F2} MS / DRAW PREV {_perfLastDrawMs:F2} MS";
        _poCountLine = $"CPU WALL TIME / UPDATES {_perfLastUpdates}";
    }
    protected override void EndDraw()
    {
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        try { base.EndDraw(); }
        finally { _poPresentWallMs = PerfElapsed(start); }
    }
    private void ApplyRenderCap()
    {
        int cap = IsActive ? EffectiveRenderCap : EnginePerformanceOptions.InactiveCap;
        if (cap <= 0 || _perfLastDrawStart == 0) return;
        double remaining = 1000.0 / cap - PerfElapsed(_perfLastDrawStart);
        if (remaining >= 1) System.Threading.Thread.Sleep((int)remaining);
    }
    private long _perfFixedRemainder;
    private TimeSpan _perfFixedTotal;
    private int _perfCaptureWidth, _perfCaptureHeight;
    private void RequestPerfPacing()
    {
        CancelPerfCapture();
        _perfUncappedRequested = !_perfUncappedRequested;
    }
    // Both lab VSync settings use the same fixed simulation accumulator.
    // Normal gameplay retains the framework loop unless a custom cap or VSync-off is requested.
    private void ApplyPerfPacing(GameTime time)
    {
        bool lab = IsPerformanceLab || IsQualityLab;
        bool disableVSync = lab ? _perfUncappedRequested : !EffectiveVSync;
        bool variableLoop = lab || EnginePerformanceOptions.ForceVariableLoop || EffectiveRenderCap > 0 || !EffectiveVSync;
        if (disableVSync != _perfUncappedActive || variableLoop != _perfVariableLoop)
        {
            CancelPerfCapture();
            if (disableVSync != _perfUncappedActive)
            {
                _gameLighting?.InvalidateSharedVisibility();
                var pp = GraphicsDevice.PresentationParameters;
                _graphics.PreferredBackBufferWidth = pp.BackBufferWidth;
                _graphics.PreferredBackBufferHeight = pp.BackBufferHeight;
                _graphics.SynchronizeWithVerticalRetrace = !disableVSync;
                _graphics.ApplyChanges();
                _perfUncappedActive = disableVSync;
            }
            if (variableLoop != _perfVariableLoop)
            {
                _perfVariableLoop = variableLoop;
                IsFixedTimeStep = !variableLoop;
                _perfFixedRemainder = 0;
                _perfFixedTotal = time.TotalGameTime - time.ElapsedGameTime;
                ResetElapsedTime();
            }
            ResetPerfWindow();
        }
    }
    private double _perfIntervalMs;
    private bool _perfOverlay = Environment.GetEnvironmentVariable("PS_PO_HUD") != "0", _perfToggleDown;
    private bool _qualityLabActive, _qualityLabPan;
    private int _qualityLabSweepAxis = 3; // 1 horizontal, 2 vertical, 3 both
    private string _qualityLabFixture = "normal";
    private BenchmarkLightingFixtureSet? _benchmarkFixtureLights;
    private bool IsQualityLab => _qualityLabActive;
    private bool IsPerformanceLab => !IsQualityLab && _ldtkLighting?.LevelId == GameLevelIds.LightingPerformanceLab;
    private bool PoPanCamera => Environment.GetEnvironmentVariable("PS_PO_10D_PAN_CAMERA") == "1";

    private void SetQualityLabProfile(int number) => ChangeLabProfile(LightingQualityLabProfile.For(number));

    private void LeaveQualityLab()
    {
        SpatialQueryPolicy.ResetLab();
        _qualityLabActive = false;
        RestoreLabState();
        EnginePerformanceOptions.ManualGpuShadowBuffers = null;
        EnginePerformanceOptions.ManualGpuShadowResident = EnginePerformanceOptions.ManualGpuShadowIndexed = null;
        EnginePerformanceOptions.ManualGpuLocalMasks=null;
        EnginePerformanceOptions.ManualGpuSpecializedMaterials=null;
        EnginePerformanceOptions.ManualGpuFusedLayer=null;
        EnginePerformanceOptions.ManualGpuBatchUnshadowed=null;
        EnginePerformanceOptions.ManualGpuFastBloom=null;
        EnginePerformanceOptions.ManualGpuInstancedParticles=null;
        EnginePerformanceOptions.ManualGpuMaterialClear=null;
        EnginePerformanceOptions.ManualGpuScopedBinds=null;
        if (EnginePerformanceOptions.ManualLightingLab is not null)
        {
            EnginePerformanceOptions.ManualLightingLab = null;
            _gameLighting?.ResetManualLightingProfile();
            _adaptiveAreaSamples.Clear();
        }
        _labControls.Reset(); _labGpuPage=false; _gpuStressCount=0; _gpuStressMotion=false; _gpuLabTiming=false; _labNormalMaps=true; _labAmbient=100; _labCap=0; _manualWarmup=60; _manualRecorded=120;
        _qualityLabPan=false; _qualityLabSweepAxis=3; _qualityLabFixture="normal"; _labInputWasCaptured=false;
        _labPreserveSceneLights=true; _labLightSelection.Clear(); _labInspectedLightId=null;
    }

    private bool IsPhase21Lab => _ldtkLighting?.LevelId is GameLevelIds.Phase21LightingLab or GameLevelIds.LightingPerformanceLab;
    private const int AllPerfOptimizations = 127;
    private int _perfMask;
    private bool _perfFreeze;
    private string _perfRunId = "manual";
    private bool PerfEnabled(int flag) => (((IsPerformanceLab || IsQualityLab) ? _perfMask : EnginePerformanceOptions.ProductionLightingMask) & flag) != 0;
    private bool PerfCaptureActive => _phase21BenchmarkFrame >= 0;
    private void CancelPerfCapture()
    {
        bool wasRunning = PerfCaptureActive;
        _phase21BenchmarkFrame = -1;
        ResetPerfWindow();
        _phase21BenchmarkStatus = wasRunning ? "BENCHMARK CANCELLED" :
            IsPerformanceLab ? "READY - CAPTURE CURRENT" : IsQualityLab ? "QUALITY LAB READY" : "READY - CAPTURE CURRENT";
    }
    // Both keyboard and mouse commands enter the same action paths. Changing
    // a measured setting discards an incomplete capture rather than mixing data.
    private void SelectPerfWorkload(int preset)
    {
        LightingQuality quality = _lightingQuality;
        bool running = PerfCaptureActive;
        ApplyPhase21LabPreset(preset);
        if (IsPerformanceLab)
        {
            _lightingQuality = quality;
            if (_lightingEvents is not null) _lightingEvents.Quality = quality;
            _phase21BenchmarkStatus = running ? "BENCHMARK CANCELLED" : "READY - CAPTURE CURRENT";
        }
    }
    private void SetPerfQuality(LightingQuality quality)
    {
        _lightingQuality = quality;
        if (_lightingEvents is not null) _lightingEvents.Quality = quality;
        CancelPerfCapture();
    }
    private void SetPerfIsolation(int mode) { _perfIsolation = mode; CancelPerfCapture(); }
    private void TogglePerfHulls() { _phase21HullOverlay = !_phase21HullOverlay; CancelPerfCapture(); }
    private void TogglePerfOptimization(int flag) { _perfMask ^= flag; CancelPerfCapture(); }
    private void SetAllPerfOptimizations(bool on)
    {
        _perfMask = on ? AllPerfOptimizations : 0;
        CancelPerfCapture();
    }
    private void TogglePerfFreeze() { _perfFreeze = !_perfFreeze; CancelPerfCapture(); }
    private void BeginPerfCapture()
    {
        SetPerfCaptureDimensions();
        _perfRunId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
        ResetPerfWindow(); ResetPbBenchmark(); _phase21Benchmark.Clear(); _phase21BenchmarkFrame = 0;
        _phase21BenchmarkStatus = $"WARMUP {ActivePerfWarmup} FRAMES";
    }
    private void SetPerfCaptureDimensions()
    {
        _perfCaptureWidth=GraphicsDevice.PresentationParameters.BackBufferWidth;
        _perfCaptureHeight=GraphicsDevice.PresentationParameters.BackBufferHeight;
        _poCaptureScene = _flow.Scenes.ActiveDestination?.LevelId;
        _poCaptureWorld = _worldClockTest?.WorldInstanceId;
        _poCaptureLighting = _lightingEnabled;
        _poCaptureOverlay = _perfOverlay;
        _poCaptureQuality = _lightingQuality;
        _poCapturePreset = _lightingPresetId;
    }
    private int _perfIsolation;
    private static double PerfElapsed(long start) =>
        (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    private void ResetPerfWindow()
    {
        _frameIntervals.Clear(); _liveFrameReadout.Reset(); _perfLastDrawStart = 0; _perfIntervalMs = 0;
        _poHudNextRefresh = 0; _poHudP95 = _poLightP95 = 0;
        _gameLighting?.CpuSubmitWindow.Clear();
    }
    protected override void Draw(GameTime gameTime)
    {
        if (!string.IsNullOrWhiteSpace(_poVisualOutput))
        {
            if (!VisualReady || !IsActive) return;
            if (_flow.IsMenuOpen || _flow.IsTransitionActive || !(IsPerformanceLab || IsQualityLab || IsSpatialLab) || _paused || _worldClockTest is null)
                throw new InvalidOperationException("Visual capture scene changed.");
            DrawFrame(new GameTime(_perfFixedTotal, TimeSpan.Zero));
            if (++_poVisualDraws == (Environment.GetEnvironmentVariable("PS_PO_GPU_VISUAL_WARMUP")=="1" ? GpuMaskAdmission.DefaultStableFrames + 2 : 2)) { SaveVisualCapture(); Exit(); }
            return;
        }
        StartAutomatedPerfCaptureIfReady();
        ApplyRenderCap();
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_perfLastDrawStart != 0)
        {
            _perfIntervalMs = (now - _perfLastDrawStart) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (_perfIntervalMs > 0)
            {
                _frameIntervals.Add(_perfIntervalMs);
                if (InteractiveLab || IsSpatialLab)
                    _liveFrameReadout.Add(_perfIntervalMs, _perfUpdates, _perfUpdateMs, _perfLastDrawMs, _poPresentWallMs);
            }
        }
        bool completedInterval = _perfLastDrawStart != 0;
        _perfLastDrawStart = now;
        _perfLastUpdateMs = _perfUpdateMs; _perfLastUpdates = _perfUpdates;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        int gc0=GC.CollectionCount(0), gc1=GC.CollectionCount(1), gc2=GC.CollectionCount(2);
        _perfFrameAllocatedBytes = completedInterval ? Math.Max(0, allocated-_perfAllocatedAtDrawStart) : 0;
        _perfFrameGc0 = completedInterval ? gc0-_perfGc0 : 0;
        _perfFrameGc1 = completedInterval ? gc1-_perfGc1 : 0;
        _perfFrameGc2 = completedInterval ? gc2-_perfGc2 : 0;
        _perfAllocatedAtDrawStart=allocated; _perfGc0=gc0; _perfGc1=gc1; _perfGc2=gc2;
        // Record the just-completed draw and intervening updates BEFORE starting the next draw.
        if (PerfCaptureActive && (_flow.IsMenuOpen || _flow.IsTransitionActive ||
            _flow.Scenes.ActiveDestination?.LevelId != _poCaptureScene || _worldClockTest?.WorldInstanceId != _poCaptureWorld ||
            _lightingEnabled != _poCaptureLighting || _perfOverlay != _poCaptureOverlay ||
            _lightingQuality != _poCaptureQuality || _lightingPresetId != _poCapturePreset))
            CancelPerfCapture();
        if (_poAutoStarted && PerfCaptureActive && (_paused || _perfFreeze != _poAutoFreeze || _phase21HullOverlay ||
            _perfMask != _poAutoMask || _perfIsolation != _poAutoIsolation))
            CancelPerfCapture();
        if (PerfCaptureActive && completedInterval)
            RecordPerfFrame(_perfPreviousLightingActive);
        if (_poAutoStarted && !PerfCaptureActive)
        {
            if (!_poAutoSaved) Console.Error.WriteLine("PO benchmark incomplete: " + _phase21BenchmarkStatus);
            Exit();
            return;
        }
        _perfDisplaySimMs=_perfSimulationMs; _perfDisplayAudioMs=_perfAudioMs;
        _perfDisplayParticleMs=_perfParticleUpdateMs; _perfDisplayDiagMs=_perfDiagnosticMs;
        _perfDisplaySceneMs=_perfSceneDrawMs; _perfDisplayHudMs=_perfHudMs;
        _perfUpdateMs = 0; _perfUpdates = 0;
        _perfSimulationMs=_perfAudioMs=_perfParticleUpdateMs=_perfLightUpdateMs=_perfDiagnosticMs=_perfEventsMs=0;
        _perfSceneDrawMs=_perfParticleDrawMs=_perfLightPrepareMs=_perfHudMs=_perfCameraMs=0;
        _perfPreviousLightingActive=false;
        if (_perfOverlay || IsPhase21Lab || _debugHudMode == GameDebugHudMode.Full)
            RefreshPerfHud(now);
        DrawFrame(gameTime);
        if (_perfOverlay && !InteractiveLab && !_flow.IsMenuOpen && _spriteBatch is not null && _pixel is not null)
        {
            int w = GraphicsDevice.Viewport.Width, h = GraphicsDevice.Viewport.Height;
            int x = Math.Max(4, w - 440), y = Math.Max(4, h - (IsPerformanceLab ? 82 : 68));
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            _spriteBatch.Draw(_pixel, new Rectangle(x,y,Math.Min(434,w-x),IsPerformanceLab ? 64 : 46),new Color(15,19,24,235));
            if (IsPerformanceLab)
            {
                PerfDrawText(new Rectangle(x+6,y+6,Math.Min(420,w-x-8),16),
                    _poFpsLine, new Color(167,232,235), 2);
                PerfDrawText(new Rectangle(x+6,y+27,Math.Min(420,w-x-8),10),
                    _poFrameLine,Color.White,1);
                PerfDrawText(new Rectangle(x+6,y+40,Math.Min(420,w-x-8),10),
                    _poUpdateLine,Color.LightGray,1);
                PerfDrawText(new Rectangle(x+6,y+51,Math.Min(420,w-x-8),10),
                    _poCountLine,Color.LightGray,1);
            }
            else
            {
                GymDebugText.Draw(_spriteBatch,_pixel,x+5,y+5,
                    _poFpsLine + " / " + _poFrameLine,Color.White,1);
                GymDebugText.Draw(_spriteBatch,_pixel,x+5,y+18,
                    _poUpdateLine,Color.LightGray,1);
                GymDebugText.Draw(_spriteBatch,_pixel,x+5,y+31,
                    PerfCaptureActive ? _phase21BenchmarkStatus : (_phase21BenchmarkStatus.StartsWith("SAVED:", StringComparison.Ordinal) ? _phase21BenchmarkStatus : "CTRL+SHIFT+B CAPTURE / F HUD"),Color.LightGray,1);
            }
            _spriteBatch.End();
        }
        _perfDrawnParticles=_particleVfx?.LastDrawCount??0;
        _perfWorldEffects=_particleVfx?.Presenter.ActiveWorldEffects??0;
        _perfUiEffects=_particleVfx?.Presenter.ActiveUiEffects??0;
        var metrics = GraphicsDevice.Metrics;
        _perfDrawCalls=metrics.DrawCount; _perfPrimitives=metrics.PrimitiveCount;
        _perfSprites=metrics.SpriteCount; _perfTargetChanges=metrics.TargetCount;
        _perfLastDrawMs = PerfElapsed(now);
    }

    private void SaveVisualCapture()
    {
        int w = GraphicsDevice.PresentationParameters.BackBufferWidth, h = GraphicsDevice.PresentationParameters.BackBufferHeight;
        if (w != 1280 || h != 720 || _gameLighting is null || (!IsSpatialLab && _gameLighting.LastLights == 0))
            throw new InvalidOperationException("Invalid visual capture dimensions or inactive lighting.");
        var pixels = new Color[checked(w*h)];
        GraphicsDevice.GetBackBufferData(pixels);
        Directory.CreateDirectory(_poVisualOutput!);
        using (var output = new BinaryWriter(File.Create(Path.Combine(_poVisualOutput!, "frame.rgba"))))
            foreach (Color c in pixels) { output.Write(c.R); output.Write(c.G); output.Write(c.B); output.Write(c.A); }
        File.WriteAllLines(Path.Combine(_poVisualOutput!, "visual.txt"), new[] {
            "Spatial mode: "+SpatialQueryPolicy.Current,"Spatial queries: "+SpatialCollisionDiagnostics.Snapshot.Queries,"Visual draws: "+_poVisualDraws,"Light coverage: "+(UseManualLightCoverage?"Preserve":"Budget"),"Width: "+w, "Height: "+h, "Ticks: "+_poVisualTicks, "World tick: "+_worldClockTest!.WorldTick,
            "Scene: "+_flow.Scenes.ActiveDestination?.LevelId, "Lights: "+_gameLighting.LastLights,
            "PB visible bodies: "+_pbVisible,"PB visual target: "+PbVisualTargetId,"PB target visible: "+_pbVisualTargetVisible,"PB body signature: "+PbBodySignature(),"PB contacts: "+(_worldClockTest?.DynamicBodies?.Metrics.Contacts??0),"PB pair impacts: "+(_worldClockTest?.DynamicBodies?.PairContacts??0),
            "Player bounds: "+_worldClockTest!.GetPlayer(1).Bounds, "Camera: "+(IsSpatialLab?_spatialVisualCamera:_lastLightingView.ToString()),
            "Fixture coordinates: "+(PoFixture == "normal" ? "authored" : BenchmarkLightingFixtureSet.CoordinateVersion),
            "Light positions: "+string.Join(";", _lastSelectedLights.Select(l => FormattableString.Invariant($"{l.Id}={l.Position.X:R},{l.Position.Y:R}"))),
            "Original mask: "+_perfMask, "Quality: "+_lightingQuality,
            "Material regions: "+EnginePerformanceOptions.Po10MaterialRegions,
            "Light regions: "+EnginePerformanceOptions.Po10LightRegions,
            "Shared fields: "+EnginePerformanceOptions.Po10SharedFields,
            "Skip clears: "+EnginePerformanceOptions.Po10SkipClears,
            "Cache capacity: "+EnginePerformanceOptions.Po10CacheCapacity,
            "Light width: "+_gameLighting.LightWidth, "Mask width: "+_gameLighting.MaskWidth,
            "Light scale: "+EnginePerformanceOptions.Po10LightScale, "Mask scale: "+EnginePerformanceOptions.Po10MaskScale,
            "Bloom scale: "+EnginePerformanceOptions.Po10bBloomScale, "Motion frozen: "+_perfFreeze,
            "Shadow edge: "+_shadowEdgePreset, "Adaptive: "+EnginePerformanceOptions.Po10bAdaptiveSamples,
            "Static shadow fields: "+EnginePerformanceOptions.Po10fStaticShadowFields, "Static shadow hits: "+_gameLighting.LastStaticShadowFieldHits, "Static fields: "+EnginePerformanceOptions.Po10bStaticFields, "Fixture: "+PoFixture, "Camera locked: "+PoLockCamera, "Camera pan: "+PoPanCamera, "Camera convergence: "+EnginePerformanceOptions.CameraConvergence, "Static hits: "+_gameLighting.LastStaticFieldHits, "GPU: "+GraphicsDevice.Adapter.Description,"GPU backend: "+GpuBackendName,"GPU GL renderer: "+_gpuTimer.Adapter,"GPU GL driver: "+_gpuTimer.Driver,"Process ID: "+Environment.ProcessId,"Executable: "+Environment.ProcessPath,
            "GPU shadow buffers requested: "+EnginePerformanceOptions.GpuShadowBuffers,
            "GPU buffered draws: "+_gameLighting.LastGpuBufferedDraws, "GPU reference draws: "+_gameLighting.LastGpuReferenceDraws,
            "GPU fallback draws: "+_gameLighting.LastGpuFallbackDraws,
            "GPU resident requested: "+EnginePerformanceOptions.GpuShadowResident, "GPU indexed requested: "+EnginePerformanceOptions.GpuShadowIndexed,
            "GPU resident hits: "+_gameLighting.LastGpuResidentHits, "GPU resident misses: "+_gameLighting.LastGpuResidentMisses,
            "GPU indexed draws: "+_gameLighting.LastGpuIndexedDraws, "GPU index bypasses: "+_gameLighting.LastGpuIndexBypasses,
            "GPU skipped binds: "+_gameLighting.LastGpuSkippedBinds,"GPU skipped clear pixels: "+_gameLighting.LastGpuSkippedClearPixels,"GPU scoped_binds: "+EnginePerformanceOptions.GpuScopedBinds,"GPU material_clear: "+EnginePerformanceOptions.GpuMaterialClear,"GPU local_masks: "+EnginePerformanceOptions.GpuLocalMasks, "GPU specialized_materials: "+EnginePerformanceOptions.GpuSpecializedMaterials, "GPU fused_layer: "+EnginePerformanceOptions.GpuFusedLayer, "GPU batch_unshadowed: "+EnginePerformanceOptions.GpuBatchUnshadowed, "GPU fast_bloom: "+EnginePerformanceOptions.GpuFastBloom, "GPU instanced_particles: "+EnginePerformanceOptions.GpuInstancedParticles,
            "GPU Local mask builds: "+_gameLighting.LastGpuLocalBuilds, "GPU Local mask hits: "+_gameLighting.LastGpuLocalHits, "GPU Local mask prior hits: "+_gameLighting.LastGpuLocalPriorHits, "GPU Local mask bypasses: "+_gameLighting.LastGpuLocalBypasses, "GPU Local mask bytes: "+_gameLighting.GpuLocalBytes, "GPU Specialized passes: "+_gameLighting.LastGpuSpecializedPasses, "GPU Fused layers: "+_gameLighting.LastGpuFusedLayers, "GPU Direct lights: "+_gameLighting.LastGpuDirectLights, "GPU Fast bloom: "+_gameLighting.LastGpuFastBloom, "GPU Particle instances: "+(_particleVfx?.LastGpuInstances??0), "GPU Particle draws: "+(_particleVfx?.LastGpuDraws??0), "GPU Particle analytic: "+(_particleVfx?.LastGpuAnalytic??0), "GPU Particle fallbacks: "+(_particleVfx?.LastGpuFallbacks??0), "GPU Particle upload bytes: "+(_particleVfx?.LastGpuUploadBytes??0), "GPU particle stress: "+GpuStressCount,"GPU particle motion: "+GpuStressMotion });
    }

    private void DrawFrame(GameTime gameTime)
    {
        _gpuTimer.Begin(EnginePerformanceOptions.GpuTiming || (InteractiveLab && _gpuLabTiming), GpuBackendName=="DesktopGL");
        try {DrawFrameCore(gameTime);}
        finally {_gpuTimer.End();}
    }
    private void DrawFrameCore(GameTime gameTime)
    {
        using var diagnosticDrawScope = _diagnostics?.MonitorOperation("Game1.Draw");
        GraphicsDevice.Clear(new Color(18, 23, 35));
        if (_lightingBasicsLab is BasicLightingLab basicsLab &&
            (_lightingBasicsMode || IsLightingBasicsScene) &&
            _spriteBatch is not null && _pixel is not null)
        {
            basicsLab.Draw(_spriteBatch, _pixel);
            if (!_lightingBasicsMode)
            {
                // Menu overlays and transition curtain are drawn above the
                // inspector, exactly as in ordinary gameplay scenes.
                _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
                if (_flow.IsMenuOpen) DrawFlowMenu(_spriteBatch, _pixel);
                DrawTransitionCurtain(_spriteBatch, _pixel);
                _spriteBatch.End();
            }
            base.Draw(gameTime);
            return;
        }
        if (_materialWorkbench is SurfaceMaterialWorkbench materialLab &&
            _spriteBatch is not null && _pixel is not null)
        {
            materialLab.Draw(_spriteBatch, _pixel);
            base.Draw(gameTime);
            return;
        }
        if (_lightingPlayground is LightingPlayground lightingLab &&
            _spriteBatch is not null && _pixel is not null)
        {
            lightingLab.Draw(_spriteBatch, _pixel);
            base.Draw(gameTime);
            return;
        }
        if (_particlePlayground is ParticlePlayground particleLab &&
            _spriteBatch is not null && _pixel is not null)
        {
            particleLab.Draw(GraphicsDevice, _spriteBatch, _pixel);
            base.Draw(gameTime);
            return;
        }
        if (_spriteBatch is not null && _pixel is not null)
        {
            _particleVfx?.BeginFrame();
            int perfIsolation = (IsPerformanceLab || IsQualityLab) ? _perfIsolation : 0;
            bool lightingWorldActive = !IsSpatialLab && perfIsolation is not (1 or 7 or 8) && _gameLighting is not null &&
                _lightingCatalog is not null && _lightingEvents?.Scene is not null &&
                _lightingEnabled && _ldtkLevelMode && _ldtkLevel is not null &&
                _worldClockTest is not null && _flow.ActiveScene == MajorSceneKind.Gameplay;
            // L02 captures each authored render depth plane individually, so
            // the active layer (not the whole scene) is the light receiver.
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            bool showFullHud = !IsPerformanceLab && !IsQualityLab && !IsSpatialLab && !VisualReady && perfIsolation != 6 && _debugHudMode == GameDebugHudMode.Full;
            if (_flow.ActiveScene == MajorSceneKind.Title)
                DrawTitleBackdrop(_spriteBatch, _pixel);
            else
            {
                if (_worldClockTest is WorldSession clockWorld)
                {
                    if (_ldtkLevelMode && _ldtkLevel is LdtkImportedLevel imported)
                    {
                        long cameraStarted=System.Diagnostics.Stopwatch.GetTimestamp();
                        Camera2DView camera = GetLdtkCameraView(imported, clockWorld,
                            _paused ? 0 : gameTime.ElapsedGameTime.TotalSeconds);
                        if (_poVisualStarted && clockWorld.DynamicBodies is { } dynamics && dynamics.Bodies.Count>0)
                        {
                            var focus=dynamics.Get(PbVisualTargetId).Bounds.Center;
                            if(IsPhysicsLab && PbVisualTargetId==1) focus=new Vector2D((focus.X+clockWorld.GetPlayer(1).Bounds.Center.X)/2,focus.Y);
                            camera=Camera2DView.FromFocus(focus,imported.WidthPx/LdtkLevelImporter.PixelsPerWorldUnit,
                                imported.HeightPx/LdtkLevelImporter.PixelsPerWorldUnit,camera.ViewportWidth,camera.ViewportHeight,camera.PixelsPerWorldUnit);
                        }
                        else if (PoAutomated && PoPanCamera)
                        {
                            double ppu = LdtkLevelImporter.PixelsPerWorldUnit;
                            camera = BenchmarkCameraPath.View(camera, imported.WidthPx / ppu,
                                imported.HeightPx / ppu, clockWorld.WorldTick);
                        }
                        else if ((InteractiveLab && _qualityLabPan) || (IsSpatialLab && !AutomatedLaunch && _spatialCameraAxes!=0))
                        {
                            double ppu = LdtkLevelImporter.PixelsPerWorldUnit;
                            camera = BenchmarkCameraPath.ManualView(camera, imported.WidthPx / ppu, imported.HeightPx / ppu,
                                clockWorld.WorldTick, ((IsSpatialLab?_spatialCameraAxes:_qualityLabSweepAxis) & 1) != 0, ((IsSpatialLab?_spatialCameraAxes:_qualityLabSweepAxis) & 2) != 0);
                        }
                        else if (PoAutomated && PoLockCamera)
                        {
                            _poLockedCamera ??= camera;
                            camera = _poLockedCamera.Value;
                        }
                        if(IsSpatialLab) _spatialVisualCamera=camera.ToString();
                        _ldtkCharacterAnimations ??= new LdtkGrayboxAnimator(
                            _ldtkCharacterSprites?.Definition);
                        _ldtkCharacterAnimations.Advance(
                            _paused ? 0 : gameTime.ElapsedGameTime.TotalSeconds,
                            clockWorld.Snapshot().Players);
                        // Interleave each VFX layer with the existing camera
                        // and scene renderer, rather than drawing all effects
                        // over the character or beneath the level background.
                        ParticleViewport particleView =
                            ParticleViewportAdapters.FromLdtkCamera(camera);
                        _perfCameraMs=PerfElapsed(cameraStarted);
                        long lightPrepareStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                        LightingArtPreset? art = lightingWorldActive
                            ? _lightingCatalog!.GetPreset(_lightingPresetId) : null;
                        if (art is not null && InteractiveLab && _labAmbient != 100)
                            art=art with { Ambient=art.Ambient*(_labAmbient/100.0) };
                        string? filterTag = IsPhase21Lab
                            ? Phase21LabTag()
                            : _ldtkLighting?.LevelId == GameLevelIds.EmissiveLightingLab
                            ? "EMISSIVE"
                            : _ldtkLighting?.LevelId == GameLevelIds.LightTypesLab
                            ? _l20LightFilter switch { 1 => "SUN", 2 => "MOON", 3 => "POINT", 4 => "SPOT", 5 => "TUBE", 6 => "EMISSIVE", _ => null }
                            : null;
                        string fixture = InteractiveLab ? _qualityLabFixture : PoAutomated ? PoFixture : "normal";
                        // Keep admission and every GPU shadow consumer on the same budget.
                        // The widened manual lab can exceed the original five-mask High quota.
                        LightingFrameBudget frameLightBudget = EffectiveFrameLightBudget;
                        _labLightSelection.Clear();
                        IReadOnlyList<RadialLight> lights = lightingWorldActive && _l19dWorldLightsEnabled
                            ? _lightingEvents!.BuildVisible(GameplayLightingRenderer.View(camera),
                                EnumerateParticleLights(), LightingStressCandidates(camera), filterTag,
                                IsQualityLab ? frameLightBudget : null,
                                InteractiveLab && IsQualityLab && fixture == "normal" ? _labLightSelection : null)
                            : Array.Empty<RadialLight>();
                        if (_ldtkLighting?.LevelId == GameLevelIds.EmissiveLightingLab && _emissionLabDarkWall)
                            lights = lights.Select(light => light with { VirtualZ = -1 }).ToArray();
                        if (lightingWorldActive && fixture != "normal")
                            lights = (_benchmarkFixtureLights ?? throw new InvalidOperationException("Missing scene-owned benchmark fixture layout.")).Get(fixture);
                        if (perfIsolation == 2)
                            lights = lights.Select(light => light with { CastShadows = false }).ToArray();
                        if (lightingWorldActive && EnginePerformanceOptions.Po10bAdaptiveSamples &&
                            EnginePerformanceOptions.AreaSamples == 0 && perfIsolation != 5)
                            lights = _adaptiveAreaSamples.Apply(lights, GameplayLightingRenderer.View(camera).PixelsPerUnit,
                                AreaEmitterSampling.CountForRadialBudget(LightingFrameBudget.For(_lightingQuality).ShadowRadialSamples));
                        if (lightingWorldActive)
                        {
                            _lastLightingView = GameplayLightingRenderer.View(camera);
                            _lastSelectedLights = lights;
                        }
                        // Artwork keeps animating when lighting is disabled through DEV TOOLS.
                        LdtkLightingScene? frameLighting = _lightingEvents?.CurrentScene();
                        if (lightingWorldActive)
                        {
                            LightingFrameBudget shadowBudget = frameLightBudget;
                            ShadowEdgePreset effectiveShadowPreset =
                                _shadowQualityPolicy == ShadowQualityPolicy.FollowLightingQuality
                                    ? ShadowContactControls.QualityPreset(_lightingQuality)
                                    : _shadowEdgePreset;
                            _gameLighting!.ReuseGeometry = PerfEnabled(1);
                            _gameLighting.ReuseLayerFields = PerfEnabled(2);
                            _gameLighting.PruneAreaSamples = PerfEnabled(4);
                            _gameLighting.CacheAcrossFrames = PerfEnabled(8);
                            _gameLighting.ReuseHardMasks = PerfEnabled(16);
                            _gameLighting.ReuseSharedMasks = PerfEnabled(32);
                            _gameLighting.PersistSharedMasks = PerfEnabled(64);
                            _gameLighting.AreaSampleOverride = perfIsolation == 5 ? 1 : EnginePerformanceOptions.AreaSamples == 0 ? null : EnginePerformanceOptions.AreaSamples;
                            _gameLighting.ReceiverComparisonOverride = _ldtkLighting?.LevelId == GameLevelIds.LightTypesLab ? _l20ReceiverMode : null;
                            _gameLighting.ReceiverOrientationEnabled = _ldtkLighting?.LevelId != GameLevelIds.LightTypesLab || _l20ReceiverNormals;
                            _gameLighting.PolygonRimsEnabled = _ldtkLighting?.LevelId != GameLevelIds.LightTypesLab || _l20PolygonRims;
                            _gameLighting.ShadowSoftnessPixels =
                                ShadowEdgeSettings.EffectiveWidth(effectiveShadowPreset, shadowBudget);
                            _gameLighting.FilterMode = _shadowFilterMode;
                            _gameLighting.MaterialLightMode = _materialLightMode;
                            _gameLighting.HdrEnabled = _l19Hdr;
                            _gameLighting.EmissionEnabled = _l19Emission;
                            _gameLighting.BloomEnabled = _l19Bloom && perfIsolation != 3;
                            _gameLighting.PostExposure = _l19Exposure;
                            _gameLighting.BloomThreshold = _l19BloomThreshold;
                            _gameLighting.BloomStrength = _l19BloomStrength;
                            _gameLighting.BloomScatter = _l20BloomScatter;
                            _gameLighting.BloomOnly = _l20BloomOnly;
                            _gameLighting.ShadowEmitterOverrides = _shadowEmitterOverrides;
                            _gameLighting.BeginLayeredFrame(lights, shadowBudget);
                        }
                        // Visual layer controls draw order, not a particle's
                        // lighting depth. All world-space effect parts default to
                        // the gameplay receiver layer (including landing dust
                        // drawn behind or in front of the player).
                        _perfLightPrepareMs = PerfElapsed(lightPrepareStarted);
                        Action<ParticleVisualLayer>? layerHook = _particleVfx is null || perfIsolation is 4 or 7 or 8 ? null : layer =>
                        {
                            long particleDrawStarted=System.Diagnostics.Stopwatch.GetTimestamp();
                            if (lightingWorldActive && frameLighting is not null &&
                                frameLighting.Layers.Gameplay.Mode != RenderLayerLightingMode.Unlit)
                                _particleVfx.DrawSceneLitLayer(_spriteBatch, particleView, layer,
                                    () => _gameLighting!.BeginLayer(),
                                    additive => _gameLighting!.ComposeLayer(_spriteBatch, camera,
                                        lights, frameLighting.Occluders, art!,
                                        frameLightBudget,
                                        frameLighting.Layers.Gameplay, additive));
                            else
                                _particleVfx.DrawLayer(_spriteBatch, particleView, layer);
                            _perfParticleDrawMs += PerfElapsed(particleDrawStarted);
                        };
                        IReadOnlyList<LightingWorldSampler.PreparedSample>? preparedSamples = null;
                        IReadOnlyList<LightingWorldSampler.PreparedSample>? CpuSamples() => EnginePerformanceOptions.LightingSamples
                            ? (preparedSamples ??= LightingWorldSampler.Prepare(lights, art!, CpuAreaSampleCount, EnginePerformanceOptions.SharedSamples ? _gameLighting?.PreparedLights : null)) : null;
                        long sceneStarted=System.Diagnostics.Stopwatch.GetTimestamp();
                        _pbVisible=0;
                        if (perfIsolation != 8) LdtkLevelRenderer.Draw(_spriteBatch, _pixel,
                            GraphicsDevice.Viewport, clockWorld, imported, _paused,
                            _ldtkVisuals, _ldtkShowCollisionDebug, showFullHud,
                            _ldtkDrawCollisionBackdrop, _ldtkConnectedLevel,
                            camera, _ldtkCharacterAnimations, _ldtkCharacterSprites,
                            layerHook,
                            null,
                            lightingWorldActive ? frameLighting : null,
                            lightingWorldActive ? id => ShadeGameplaySurface(id, lights, art!, frameLighting, CpuSamples()) : null,
                            lightingWorldActive ? point => ShadeGameplayHero(point, lights, art!, frameLighting, CpuSamples()) : null,
                            lightingWorldActive ? _gameLighting!.ContactShadowTexture : null,
                            lightingWorldActive ? () => _gameLighting!.BeginLayer() : null,
                            lightingWorldActive ? (view, layer) => _gameLighting!.ComposeLayer(
                                _spriteBatch, view, lights, frameLighting!.Occluders,
                                art!, frameLightBudget, layer) : null,
                            lightingWorldActive ? () => _gameLighting!.EndLayeredFrame(_spriteBatch) : null,
                            lightingWorldActive ? (submission, region, albedo, normals, edges, emission) =>
                                _gameLighting!.DrawProductionMaterialSurface(_spriteBatch,
                                    GameplayLightingRenderer.View(camera),
                                    submission.Channel == LightChannel.Hero ? submission with
                                        { MinimumAmbient = art!.HeroMinimumBrightness } : submission, region, lights,
                                    frameLighting!.Occluders, art!,
                                    frameLighting.Layers.Get(submission.LayerId),
                                    albedo, InteractiveLab && !_labNormalMaps ? null : normals, edges, emission) : null,
                            frameLighting, clockWorld.DynamicBodies is null ? null : view=>DrawPbBodies(view,clockWorld));
                        if(IsSpatialLab && !_flow.IsMenuOpen && !AutomatedLaunch) DrawSpatialOverlay(camera,clockWorld);
                        _perfSceneDrawMs = PerfElapsed(sceneStarted);
                    }
                    else if (_authoredWorldMode || _motionLabMode || _routeLabMode)
                        MovementGymRenderer.DrawAuthoredWorld(
                            _spriteBatch, _pixel, GraphicsDevice.Viewport,
                            clockWorld, _paused, _worldP1Respawns, _worldP2Respawns,
                            _motionLabMode, showFullHud,
                            CreateGymParticleDraw(clockWorld, _spriteBatch));
                    else
                        MovementGymRenderer.DrawSharedWorldClock(
                            _spriteBatch, _pixel, GraphicsDevice.Viewport,
                            clockWorld, _paused, _worldP1Respawns, _worldP2Respawns,
                            showFullHud, CreateGymParticleDraw(clockWorld, _spriteBatch));
                }
                else
                    MovementGymRenderer.Draw(_spriteBatch, _pixel,
                        GraphicsDevice.Viewport, _gym, _paused, showFullHud);
                if ((IsPerformanceLab||IsQualityLab) && GpuStressCount>0 && _particleVfx is not null)
                {
                    _spriteBatch.End();
                    _particleVfx.DrawStress(_spriteBatch,GpuStressCount,GraphicsDevice.Viewport.Width,GraphicsDevice.Viewport.Height,
                        (float)(gameTime.TotalGameTime.TotalSeconds%1000),GpuStressMotion);
                    _spriteBatch.Begin(samplerState:SamplerState.PointClamp);
                }
                if (VisualReady) { _spriteBatch.End(); return; }
                long debugHudStarted=System.Diagnostics.Stopwatch.GetTimestamp();
                if (showFullHud)
                {
                    if (_worldClockTest is WorldSession renderedWorld)
                    {
                        DrawWorldEventHud(_spriteBatch, _pixel, renderedWorld);
                        DrawCharacterStateHud(_spriteBatch, _pixel, renderedWorld);
                        DrawAbilityHud(_spriteBatch, _pixel, renderedWorld);
                        if (_motionLabMode)
                            DrawMotionLabHud(_spriteBatch, _pixel, renderedWorld);
                        if (_routeLabMode)
                            DrawRouteLabHud(_spriteBatch, _pixel, renderedWorld);
                    }
                    DrawRequestTestHud(_spriteBatch, _pixel);
                    DrawDeviceInputHud(_spriteBatch, _pixel);
                    if (_flow.ShowFlowHints && !_flow.IsMenuOpen && !_routeLabMode)
                        DrawFlowHint(_spriteBatch, _pixel);
                }
                else if (!IsPerformanceLab && !IsQualityLab && !IsSpatialLab && perfIsolation != 6 && _debugHudMode == GameDebugHudMode.Compact)
                    DrawCompactDebugHud(_spriteBatch, _pixel);
                _perfHudMs += PerfElapsed(debugHudStarted);
            }
            long toolHudStarted=System.Diagnostics.Stopwatch.GetTimestamp();
            if (!IsPerformanceLab && !IsQualityLab && !IsSpatialLab && perfIsolation != 6 && _ldtkLevelMode && _flow.ActiveScene == MajorSceneKind.Gameplay &&
                !_flow.IsMenuOpen)
            {
                IReadOnlyList<string> lightStatuses = GetAuthoredLightStatuses();
                RadialLight? shadowEditedLight = SelectedShadowLight();
                string editLightName = shadowEditedLight?.Id ?? "NONE";
                if (editLightName.Length > 24)
                    editLightName = editLightName[..24];
                float emitterPx = shadowEditedLight is not null &&
                    _shadowEmitterOverrides.TryGetValue(shadowEditedLight.Id, out float configured)
                        ? configured : 8f;
                ShadowEdgePreset effectiveEdge =
                    _shadowQualityPolicy == ShadowQualityPolicy.FollowLightingQuality
                        ? ShadowContactControls.QualityPreset(_lightingQuality) : _shadowEdgePreset;
                _lightingTools.Draw(_spriteBatch, _pixel,
                    GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height,
                    _lightingEnabled, _l19dWorldLightsEnabled, _lightingPresetId, _lightingQuality.ToString(),
                    _materialLightMode == ProductionMaterialLightMode.AllEligible ? "ALL" :
                        _materialLightMode == ProductionMaterialLightMode.CompareTwo ?
                            "COMPARE 2" : "EMISSION ONLY",
                    _l19Hdr, _gameLighting?.HdrActive ?? false, _l19Emission, _l19Bloom,
                    _l19Exposure.ToString("F2"), _l19BloomThreshold.ToString("F2"),
                    _l19BloomStrength.ToString("F2"),
                    ShadowEdgeSettings.Label(effectiveEdge),
                    _shadowFilterMode == ShadowFilterMode.ContactHardening ? "CONTACT" : "UNIFORM",
                    _shadowQualityPolicy == ShadowQualityPolicy.FollowLightingQuality ? "AUTO" : "MANUAL",
                    editLightName.ToUpperInvariant(), emitterPx.ToString("F0") + "PX",
                    _reducedLightingFlashes, _lightingStress,
                    _lightingEvents?.WallMotionEnabled ?? false, _lightingInspectionLight,
                    _debugHudMode.ToString(), _ldtkShowCollisionDebug,
                    _ldtkDrawCollisionBackdrop, _audio?.IsMuted ?? false,
                    _audio is null ? 0 : (int)Math.Round(_audio.Mixer.MasterVolume * 100),
                    lightStatuses,
                    _l19bMaterialPreset switch
                    {
                        0 => "BASELINE",
                        1 => "SELF EMISSION",
                        2 => "BLOOM HERO",
                        3 => "TRANSPARENCY",
                        4 => "GHOST / OPACITY",
                        5 => "HOLOGRAM / HDR",
                        6 => "SHOWCASE BOOST",
                        _ => "CUSTOM"
                    },
                    CurrentShowcaseFocusLabel(), CurrentShowcaseNotes(),
                    CurrentLightTypeFilterLabel(), _l20BloomScatter.ToString("F2"),
                    _l20BloomOnly, CurrentLightTypeNotes(), _ldtkLighting?.LevelId == GameLevelIds.LightTypesLab ? _l20ReceiverMode.ToString().ToUpperInvariant() : "LAB ONLY", _l20ReceiverNormals, _l20PolygonRims);
            }
            if (_ldtkLighting?.LevelId == GameLevelIds.EmissiveLightingLab && !_flow.IsMenuOpen)
            {
                const int y = 42;
                _spriteBatch.Draw(_pixel, new Rectangle(12, y, Math.Min(680, GraphicsDevice.Viewport.Width - 24), 60), new Color(15, 19, 24, 235));
                GymDebugText.Draw(_spriteBatch, _pixel, 20, y + 7,
                    "1 BASELINE / 2 SELF GLOW / 3 COMPARE WALLS / 4 ALL / 5 ALL DARK", Color.White, 1);
                GymDebugText.Draw(_spriteBatch, _pixel, 20, y + 22,
                    "WORLD " + (_l19dWorldLightsEnabled ? "ON" : "OFF") + "  EMISSION " + (_l19Emission ? "ON" : "OFF") +
                    "  BLOOM " + (_l19Bloom ? "ON" : "OFF") + "  ACTIVE LIGHTS " +
                    (lightingWorldActive ? _lastSelectedLights.Count : 0), new Color(167, 232, 235), 1);
                GymDebugText.Draw(_spriteBatch, _pixel, 20, y + 37,
                    (_emissionLabDarkWall
                        ? "LEFT WALL: DARK / RIGHT WALL: DARK / CYAN LEFT / MAGENTA RIGHT"
                        : "LEFT WALL: FRONT-LIT / RIGHT WALL: DARK-FACE / CYAN LEFT / MAGENTA RIGHT"),
                    Color.LightGray, 1);
                if (!lightingWorldActive || (_l19dWorldLightsEnabled && _lastSelectedLights.Count != 2))
                    GymDebugText.Draw(_spriteBatch, _pixel, 20, y + 49,
                        "CHECK: EXPECT TWO LIGHTS IN WORLD COLOR MODE", new Color(255, 180, 100), 1);
            }
            _perfHudMs += PerfElapsed(toolHudStarted);
            _perfPreviousLightingActive=lightingWorldActive;
            if (IsPhase21Lab && !_flow.IsMenuOpen)
                DrawPhase21LabHud(lightingWorldActive);
            if ((InteractiveLab || (IsSpatialLab && !AutomatedLaunch)) && !_flow.IsMenuOpen && !_flow.IsTransitionActive)
            {
                long labHudStarted=System.Diagnostics.Stopwatch.GetTimestamp();
                DrawManualLab();
                _perfHudMs += PerfElapsed(labHudStarted);
            }
            if (_flow.IsMenuOpen)
                DrawFlowMenu(_spriteBatch, _pixel);
            // Screen-space UI effects are drawn above menus, but below the
            // transition curtain. Gameplay pause never freezes this layer.
            if (_particleVfx is not null)
            {
                _spriteBatch.End();
                _particleVfx.DrawLayer(_spriteBatch, new ParticleViewport(
                    GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height,
                    Vector2D.Zero, 1), ParticleVisualLayer.Ui);
                _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            }
            DrawTransitionCurtain(_spriteBatch, _pixel);
            if (_ldtkLevelMode && _ldtkCharacterSprites is null)
            {
                // Do not let optional artwork failures silently masquerade as
                // an animation bug during visual acceptance.
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 38),
                    "C02 ART FALLBACK - " + (_ldtkCharacterArtWarning?.Length > 58
                        ? _ldtkCharacterArtWarning[..58] : _ldtkCharacterArtWarning),
                    new Color(255, 189, 112), 1);
            }
            if (_audioControlNoticeSeconds > 0)
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 74),
                    _audioControlMessage, new Color(153, 220, 194), 1);
            if (_audioWarning is not null)
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 55),
                    "A01 AUDIO UNAVAILABLE - " + (_audioWarning.Length > 70
                        ? _audioWarning[..70] : _audioWarning),
                    new Color(255, 189, 112), 1);
            if (_particleVfx is not null && showFullHud)
            {
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 113),
                    "P04 VFX WORLD " + _particleVfx.Presenter.ActiveWorldEffects +
                    "  UI " + _particleVfx.Presenter.ActiveUiEffects +
                    "  DROPPED " + _particleVfx.Presenter.DroppedWorldEffects,
                    new Color(156, 223, 255), 1);
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 150),
                    "P05 ZONES " + _particleVfx.ActiveEnvironmentZones + "/" +
                    _particleVfx.EnvironmentZones + "  PARTICLES " +
                    _particleVfx.EnvironmentParticles + "  CAPPED FRAMES " +
                    _particleVfx.CappedZoneFrames,
                    new Color(174, 220, 169), 1);
            }
            if (_particleWarning is not null)
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 131),
                    "P04 PARTICLES UNAVAILABLE - " +
                    (_particleWarning.Length > 58 ? _particleWarning[..58] : _particleWarning),
                    new Color(255, 189, 112), 1);
            if (_gameLighting is not null && _lightingEvents is not null &&
                _ldtkLevelMode && showFullHud)
            {
                // Stage 15C diagnostic lines intentionally separated so the
                // material-mask counters remain visible at 1280px and below.
                var cpu = _gameLighting.CpuSubmitWindow;
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 170),
                    "L456 LIGHTS " + _gameLighting.LastLights +
                    " SHADOWS " + _gameLighting.LastShadowLights +
                    " TRI " + _gameLighting.LastShadowTriangles +
                    " MAT " + _gameLighting.LastMaterialSurfaces +
                    " MAT-LIMIT " + _gameLighting.LastMaterialLightsDropped +
                    " MAT-VIS " + _gameLighting.LastMaterialVisibilityMasks +
                    " MAT-BACK " + _gameLighting.LastMaterialBackRimSurfaces,
                    new Color(184, 208, 255), 1);
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 188),
                    "L15C CPU-MS LAST " + cpu.Latest.ToString("F2") +
                    " AVG " + cpu.Mean.ToString("F2") +
                    " P95 " + _poLightP95.ToString("F2") +
                    " N " + cpu.Count +
                    " LAYER " + _gameLighting.LastLayerCpuMilliseconds.ToString("F2") +
                    " MAT " + _gameLighting.LastMaterialCpuMilliseconds.ToString("F2") +
                    " MAT-GEO " + _gameLighting.LastMaterialMaskGeometryCpuMilliseconds.ToString("F2") +
                    " LEG-GEO " + _gameLighting.LastLayerMaskGeometryCpuMilliseconds.ToString("F2"),
                    new Color(175, 232, 195), 1);
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 206),
                    "L15C MASK WORK MAT/LEG " + _gameLighting.LastMaterialVisibilityMasks +
                    "/" + _gameLighting.LastLegacyVisibilityMasks +
                    " OCC " + _gameLighting.LastMaterialMaskOccludersSubmitted +
                    " RAYS " + _gameLighting.LastMaterialMaskRadialSamples +
                    " RT-MB " + _gameLighting.EstimatedTargetsMegabytes.ToString("F1") +
                    " RT-RESIZE " + _gameLighting.TargetRebuilds +
                    " QUALITY " + _lightingQuality +
                    " STRESS " + (_lightingStress ? "ON" : "OFF"),
                    new Color(179, 199, 249), 1);
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 224),
                    "L16A SHADOW BROADPHASE: PRUNED " +
                    _gameLighting.LastShadowCastersPrunedByRange +
                    " MASKS SKIPPED " + _gameLighting.LastShadowMasksSkippedByRange,
                    new Color(177, 232, 212), 1);
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 242),
                    "L16B MATERIAL FALLBACK: NEUTRAL MAPS " +
                    _gameLighting.LastMaterialNeutralMapsReused +
                    " WHITE MASKS " + _gameLighting.LastMaterialWhiteMasksReused,
                    new Color(182, 212, 245), 1);
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 296),
                    "L17 SHADOW " + _shadowFilterMode +
                    " EDGE " + ShadowEdgeSettings.Label(
                        _shadowQualityPolicy == ShadowQualityPolicy.FollowLightingQuality
                            ? ShadowContactControls.QualityPreset(_lightingQuality) : _shadowEdgePreset) +
                    " LAYER " + _gameLighting.LastSoftLayerMasks +
                    " MAT " + _gameLighting.LastSoftMaterialMasks,
                    new Color(213, 185, 241), 1);
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 332),
                    "L18 MATERIAL: MODE " + (_materialLightMode == ProductionMaterialLightMode.AllEligible
                        ? "ALL" : _materialLightMode == ProductionMaterialLightMode.CompareTwo
                            ? "COMPARE-2" : "EMISSION-ONLY") +
                    " EVAL " + _gameLighting.LastMaterialLightsEvaluated +
                    " LIGHT-PASSES " + _gameLighting.LastMaterialLightPasses +
                    " AMBIENT-PASSES " + _gameLighting.LastMaterialAmbientPasses +
                    " MAX/SURFACE " + _gameLighting.LastMaterialMaxLightsOnSurface +
                    " DROPPED " + _gameLighting.LastMaterialLightsDropped,
                    new Color(252, 218, 155), 1);
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 350),
                    "L19 HDR " + (_gameLighting.HdrActive ? "RGBA16F" :
                        _gameLighting.HdrUnsupported ? "UNSUPPORTED" : "SDR") +
                    " EMISSIVE " + _gameLighting.LastEmissiveSurfaces +
                    " BLOOM PASSES " + _gameLighting.LastBloomPasses +
                    " BLOOM PIXELS " + _gameLighting.LastBloomPixels +
                    " POST CPU " + _gameLighting.LastPostProcessCpuMilliseconds.ToString("F2"),
                    new Color(241, 175, 223), 1);
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 368),
                    "L20 LIGHTS: SUN/MOON " + _gameLighting.LastDirectionalLights +
                    " SPOT " + _gameLighting.LastSpotLights +
                    " TUBE " + _gameLighting.LastTubeLights +
                    " SCATTER " + _l20BloomScatter.ToString("F2") +
                    " BLOOM-ONLY " + (_l20BloomOnly ? "ON" : "OFF"),
                    new Color(148, 236, 242), 1);
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 314),
                    "L17D CONTACT PROFILES " + _gameLighting.LastContactProfilesBuilt +
                    " PROFILE CPU " + _gameLighting.LastContactProfileCpuMilliseconds.ToString("F2") +
                    " UPLOAD BYTES " + _gameLighting.LastContactProfileBytesUploaded +
                    " FILTER PASSES " + _gameLighting.LastSoftFilterPasses +
                    " FILTER PX " + _gameLighting.LastSoftFilterPixels,
                    new Color(193, 220, 239), 1);
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 278),
                    "L16D DYNAMIC HULLS " + _lightingEvents.LastDynamicHullsUpdated +
                    " STATIC REUSED " + _lightingEvents.LastStaticHullsReused,
                    new Color(174, 227, 245), 1);
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 260),
                    "L16C MATERIAL REGION: DRAW " + _gameLighting.LastMaterialRegionsDrawn +
                    " OFFSCREEN " + _gameLighting.LastMaterialRegionsOffscreen +
                    " SHADED PX " + _gameLighting.LastMaterialShadePixels +
                    " AVOIDED PX " + _gameLighting.LastMaterialShadePixelsAvoided,
                    new Color(196, 232, 190), 1);
            }
            if (_lightingPresetNoticeSeconds > 0 &&
                _ldtkLighting?.LevelId == "LIGHTING_FLAT_TEST")
                GymDebugText.Draw(_spriteBatch, _pixel, 14, 120,
                    "FLAT LIGHTING: " + _lightingPresetId.ToUpperInvariant() +
                    "  OPEN DEV TOOLS TO SWITCH", new Color(255, 221, 159), 1);
            if (_lightingWarning is not null)
                GymDebugText.Draw(_spriteBatch, _pixel, 12,
                    Math.Max(4, GraphicsDevice.Viewport.Height - 242),
                    "L456 LIGHTING UNAVAILABLE - " +
                        (_lightingWarning.Length > 55 ? _lightingWarning[..55] : _lightingWarning),
                    new Color(255, 189, 112), 1);
            if (_diagnostics is not null && !IsPerformanceLab && !IsQualityLab)
            {
                // Minimal always-available report UI, including when Title or
                // Pause menus are open. No MonoGame dependencies in diagnostics.
                string message = _diagnostics.StatusMessage;
                if (message.Length > 105) message = message[..105];
                GymDebugText.Draw(_spriteBatch, _pixel, 10,
                    Math.Max(2, GraphicsDevice.Viewport.Height - 18),
                    message, new Color(170, 219, 198), 1);
            }
            _spriteBatch.End();
        }
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        // Do not retain lab process overrides beyond host lifetime.
        EnginePerformanceOptions.ManualLightingLab = null;
        EnginePerformanceOptions.ManualGpuShadowBuffers = null;
        EnginePerformanceOptions.ManualGpuShadowResident = EnginePerformanceOptions.ManualGpuShadowIndexed = null;
        EnginePerformanceOptions.ManualGpuLocalMasks=null;
        EnginePerformanceOptions.ManualGpuSpecializedMaterials=null;
        EnginePerformanceOptions.ManualGpuFusedLayer=null;
        EnginePerformanceOptions.ManualGpuBatchUnshadowed=null;
        EnginePerformanceOptions.ManualGpuFastBloom=null;
        EnginePerformanceOptions.ManualGpuInstancedParticles=null;
        EnginePerformanceOptions.ManualGpuMaterialClear=null;
        EnginePerformanceOptions.ManualGpuScopedBinds=null;
        EnginePerformanceOptions.SavedGraphics = null;
        _qualityLabActive = false;
        _gpuTimer.Dispose();
        _gameLighting?.Dispose();
        _gameLighting = null;
        _lightingEvents?.Clear();
        _lightingEvents = null;
        _lightingCatalog = null;
        _materialWorkbench?.Dispose();
        _materialWorkbench = null;
        _lightingBasicsLab?.Dispose();
        _lightingBasicsLab = null;
        _lightingPlayground?.Dispose();
        _lightingPlayground = null;
        _particlePlayground?.Dispose();
        _particlePlayground = null;
        _particleVfx?.Dispose();
        _particleVfx = null;
        _music?.Dispose();
        _music = null;
        _soundscapes = null;
        _audio?.Dispose();
        _audio = null;
        _audioEvents = null;
        _ldtkCharacterSprites?.Dispose();
        _ldtkCharacterSprites = null;
        _flow.Dispose();
        GymDebugText.Release(GraphicsDevice);
        _pixel?.Dispose();
        _spriteBatch?.Dispose();
        base.UnloadContent();
    }

    private void UpdateAudioListener()
    {
        if (_audio is null) return;
        if (_worldClockTest is WorldSession world && world.HasPlayer(1))
        {
            var center = world.GetPlayer(1).Bounds.Center;
            _audio.SetListener(new AudioListener(center.X, center.Y));
        }
        else
            _audio.SetListener(null);
    }

    private void RefreshMusicVolume()
    {
        if (_audio is null || _music is null) return;
        float volume = _audio.IsMuted ? 0f : _audio.Mixer.MasterVolume *
            _audio.Mixer.GetCategoryVolume(AudioCategory.Music);
        _music.SetBusGain(volume);
    }

    private void ShowMusicStatus()
    {
        if (_music is null || _audio is null) return;
        float gain = _audio.IsMuted ? 0 : _audio.Mixer.MasterVolume *
            _audio.Mixer.GetCategoryVolume(AudioCategory.Music);
        // This reports the requested track, not a guarantee of sound output.
        // It helps distinguish a silent/zero-gain mixer from a missing track.
        ShowAudioMessage("A02 MUSIC REQUESTED: " +
            (_music.RequestedTrack ?? "NONE") +
            "  GAIN " + (int)Math.Round(gain * 100) + "%");
    }

    private void ShowAudioMessage(string message)
    {
        _audioControlMessage = message;
        _audioControlNoticeSeconds = 3;
    }

    private Camera2DView GetLdtkCameraView(LdtkImportedLevel level,
        WorldSession world, double presentationSeconds)
    {
        WorldPlayerSnapshot player = world.GetPlayer(1);
        if (_ldtkCamera is null)
        {
            _ldtkCamera = new Camera2D(new CameraFollowSettings {
                ConvergenceDistance = EnginePerformanceOptions.CameraConvergence ? .001 : 0 });
            _ldtkCamera.Snap(player.Bounds.Center);
        }
        else
            _ldtkCamera.Follow(player.Bounds.Center, player.Velocity,
                presentationSeconds);
        double pixelsPerUnit = LdtkLevelImporter.PixelsPerWorldUnit;
        return _ldtkCamera.GetView(level.WidthPx / pixelsPerUnit,
            level.HeightPx / pixelsPerUnit,
            GraphicsDevice.Viewport.Width, GraphicsDevice.Viewport.Height,
            pixelsPerUnit);
    }

    private void ActivatePreparedScene(MovementGymSession gym, WorldSession? world,
        string? levelId, LdtkImportedLevel? importedLdtk = null,
        LdtkVisualSceneAssets? visuals = null,
        LdtkConnectedLevel? connectedLdtk = null,
        LdtkParticleSceneDefinition? particleZones = null,
        LdtkLightingScene? lighting = null)
    {
        LeaveQualityLab();
        SpatialQueryPolicy.ResetLab();
        _gym = gym;
        _worldClockTest = world;
        _authoredWorldMode = levelId == GameLevelIds.AuthoredWorld;
        _motionLabMode = levelId == GameLevelIds.MotionLab;
        _routeLabMode = levelId is (GameLevelIds.RouteA or GameLevelIds.RouteB);
        _ldtkLevelMode = importedLdtk is not null;
        // Default to a small overlay in illustrated LDtk scenes; existing
        // diagnostics-rich gym fixtures retain Full unless user chooses a
        // mode, which persists across reloads and scene changes.
        if (!_debugHudUserSelected)
            _debugHudMode = GameDebugHudModes.DefaultForScene(_ldtkLevelMode);
        _ldtkLevel = importedLdtk;
        _bodyLabMaterials = null; _bodyLabMaterialError = null; _pbIntegrationPage=false;_pbSelected=1;_pbTerrainId=-1;_pbNotice="READY";
        if(IsSpatialLab) { if(!AutomatedLaunch) SpatialQueryPolicy.LabMode=SpatialQueryMode.Reference; _spatialGrid=true; _spatialQuery=true; _spatialCameraAxes=0; }
        _ldtkConnectedLevel = connectedLdtk;
        _ldtkParticleZones = particleZones;
        _ldtkLighting = lighting;
        _benchmarkFixtureLights = null;
        if (levelId is (GameLevelIds.LightingQualityLab or GameLevelIds.LightingPerformanceLab or GameLevelIds.EngineIntegrationLab) &&
            importedLdtk is not null && world is not null)
        {
            var spawnCenter = world.GetPlayer(1).Bounds.Center;
            double ppu = LdtkLevelImporter.PixelsPerWorldUnit;
            _benchmarkFixtureLights = new BenchmarkLightingFixtureSet(new LightPoint(spawnCenter.X, spawnCenter.Y),
                importedLdtk.WidthPx / ppu, importedLdtk.HeightPx / ppu, ppu);
        }
        // Phase 19D teaching lab: start with a visibly dramatic, curated look.
        // Existing gameplay/integration levels retain the user's lighting state.
        if (levelId is GameLevelIds.LightingQualityLab or GameLevelIds.EngineIntegrationLab)
        {
            SaveLabState();
            _qualityLabActive = true;
            ApplyPhase21LabPreset(5);
            _perfMask = AllPerfOptimizations;
            _perfFreeze = false;
            _phase21HullOverlay = false;
            _qualityLabPan = false;
            _qualityLabFixture = "normal";
            _perfUncappedRequested = !EffectiveVSync;
            if (!AutomatedLaunch) SetQualityLabProfile(2);
        }
        else if (levelId == GameLevelIds.MaterialShowcaseTest)
        {
            _lightingQuality = LightingQuality.High;
            ApplyMaterialShowcasePreset(6);
            _lightingEnabled = true;
        }
        else if (levelId is GameLevelIds.Phase21LightingLab or GameLevelIds.LightingPerformanceLab)
        {
            if (levelId == GameLevelIds.LightingPerformanceLab) { SaveLabState(); ApplyPhase21LabPreset(5); }
            else ApplyPhase21LabPreset(2);
            _phase21HullOverlay = false;
            _perfMask = 0;
            _perfFreeze = levelId == GameLevelIds.LightingPerformanceLab;
            if (levelId == GameLevelIds.LightingPerformanceLab)
            {
                _phase21BenchmarkStatus = "READY - CAPTURE CURRENT";
                if (!AutomatedLaunch) { _perfUncappedRequested=!EffectiveVSync; StartManualPerformanceProfile(); }
            }
        }
        else if (levelId == GameLevelIds.EmissiveLightingLab)
        {
            ApplyEmissionLabPreset(3); // Direct colored world illumination, no glow or bloom.
        }
        else if (levelId == GameLevelIds.LightTypesLab)
        {
            _lightingQuality = LightingQuality.High;
            _lightingEnabled = true;
            _l19dWorldLightsEnabled = true;
            _l20ReceiverMode = ReceiverProjectionMode.Clipped;
            _l20ReceiverNormals = _l20PolygonRims = true;
            _l20LightFilter = 1; // Start with just the sun, not all sources.
            _l19Hdr = true;
            // Teach light distribution and cast-shadow geometry first; prevent
            // emission from the bright fixtures contaminating the comparison.
            _l19Emission = false;
            _l19Bloom = false;
            _l19Exposure = 0.85f;
            _l19BloomStrength = .55f;
            _l19BloomThreshold = 1.15f;
            _l20BloomScatter = .55f;
            _l20BloomOnly = false;
        }
        else _l19dWorldLightsEnabled = true;
        if (!InteractiveLab && !AutomatedLaunch && EnginePerformanceOptions.SavedGraphics is { } prefs)
            _lightingQuality=prefs.Quality;
        _ldtkActiveWindIds.Clear();
        _ldtkWindDirty = false;
        _lastLdtkInteraction = "NONE";
        _ldtkVisuals = visuals;
        _ldtkCamera = null; // fresh camera on every successful scene activation
        _ldtkCharacterAnimations = null; // animation timelines are scene-owned
        _ldtkShowCollisionDebug = false;
        _ldtkDrawCollisionBackdrop = visuals?.Terrain?.DrawCollisionBackdrop ?? true;
        _windSelection = 0;
        _recentWorldEvents.Clear();
        _latestStateFeedback = "NONE";
        _latestAbilityFeedback = "NONE";
        _worldP1Respawns = 0;
        _worldP2Respawns = 0;
        _requestTestEnabled = false;
        _lastResolution = null;
        _tuningLabel = "Baseline";
        _paused = false;
        _titleRefreshCounter = 6;
    }

    private static string FindLdtkDirectory()
    {
        // Search from the application directory, not the process working
        // directory. Works with Visual Studio, CLI and local test launches.
        string? directory = AppContext.BaseDirectory;
        for (int depth = 0; depth < 12 && directory is not null; depth++)
        {
            string candidate = Path.Combine(directory, "Content", "Levels");
            if (Directory.Exists(candidate)) return candidate;
            directory = Directory.GetParent(directory)?.FullName;
        }
        throw new DirectoryNotFoundException("Missing Content/Levels directory beside the project.");
    }

    // The scene factory prepares every new gameplay world BEFORE the old
    // scene exits. SceneCoordinator alone owns the lifecycle/commit boundary.
    private sealed class HostSceneFactory(Game1 host) : IGameFlowSceneFactory
    {
        public bool Supports(MajorSceneKind kind, SceneDestination? destination)
        {
            if (kind == MajorSceneKind.Title) return destination is null;
            if (kind != MajorSceneKind.Gameplay || destination is null) return false;
            if (RouteLevelGym.Catalog.Supports(destination)) return true;
            if (host._ldtkCatalog.Supports(destination)) return true;
            return destination.EntranceId == GameLevelIds.DefaultEntrance &&
                destination.LevelId is (GameLevelIds.MovementGym or
                    GameLevelIds.SharedWorld or GameLevelIds.AuthoredWorld or
                    GameLevelIds.MotionLab or GameLevelIds.LightingBasics);
        }

        public bool TryPrepare(MajorSceneKind kind, SceneDestination? destination,
            out IGameFlowScene? scene, out string? error)
        {
            scene = null;
            if (!Supports(kind, destination))
            {
                error = "Invalid level or entrance ID";
                return false;
            }
            error = null;
            if (kind == MajorSceneKind.Gameplay &&
                destination!.LevelId is (GameLevelIds.RouteA or GameLevelIds.RouteB) &&
                host._failNextRoutePreparation)
            {
                host._failNextRoutePreparation = false;
                error = "TEST: simulated route content preparation failure";
                return false;
            }
            if (kind == MajorSceneKind.Title)
            {
                scene = new HostScene(MajorSceneKind.Title, null,
                    () =>
                    {
                        host.LeaveQualityLab();
                        host._worldClockTest = null;
                        host._authoredWorldMode = false;
                        host._motionLabMode = false;
                        host._routeLabMode = false;
                        host._ldtkLevelMode = false;
                        host._ldtkLevel = null;
                        host._ldtkConnectedLevel = null;
                        host._ldtkParticleZones = null;
                        host._ldtkLighting = null;
                        host._ldtkActiveWindIds.Clear();
                        host._ldtkVisuals = null;
                        host._ldtkCamera = null;
                        host._ldtkCharacterAnimations = null;
                        // Returning to Title ends the session; switching levels
                        // or restarting never does.
                        host._gameplaySession = null;
                    });
                return true;
            }
            // These fixtures already validate their geometry and identities.
            // Preserve the user's selected fault sensitivity between scenes.
            MovementGymSession preparedGym = new(
                tuning: host._gym.Tuning,
                faultSensitivity: host._gym.FaultSensitivity,
                assistancePolicy: host._gym.AssistancePolicy);
            // A session survives scene replacements, but creating a session
            // during preparation must not replace the active one on failure.
            GameplaySession preparedSession = host._gameplaySession ??
                new GameplaySession(++host._nextGameplaySessionId,
                [new GameSessionParticipant(1, 1001, host._inputRouter.SourceId),
                 new GameSessionParticipant(2, 1002, 22)]);
            IReadOnlyList<GameSessionParticipant> roster = preparedSession.PreviewRoster(
                host._worldClockTest?.PlayerSlots, host._inputRouter.SourceId);
            LdtkImportedLevel? importedLdtk = null;
            LdtkConnectedLevel? preparedConnectedLdtk = null;
            LdtkVisualSceneAssets? preparedVisuals = null;
            LdtkParticleSceneDefinition? preparedParticleZones = null;
            LdtkLightingScene? preparedLighting = null;
            WorldSession? preparedLdtkWorld = null;
            if (host._ldtkCatalog.Supports(destination!))
            {
                try
                {
                    // The registry chooses the file and authored identifier.
                    // Every importer uses the SAME fresh JSON snapshot in
                    // Prepare; GPU textures load only before scene activation.
                    LdtkPreparedContent content = host._ldtkCatalog.Prepare(destination!);
                    importedLdtk = content.Imported;
                    preparedConnectedLdtk = content.Connected;
                    preparedParticleZones = content.ParticleZones;
                    preparedLighting = content.Lighting;
                    if (content.Visuals.TileCount > 0 || content.Scenery.SpriteCount > 0 ||
                        content.Terrain.ArtworkCount > 0)
                        preparedVisuals = new LdtkVisualSceneAssets(
                            host.GraphicsDevice, content.FilePath,
                            content.Visuals, content.Scenery, content.Terrain,
                            content.Lighting);
                    preparedLdtkWorld = LdtkLevelGym.Create(importedLdtk,
                        preparedGym.AssistancePolicy, preparedGym.FaultSensitivity,
                        host._inputRouter.SourceId,
                        worldInstanceId: ++host._nextWorldInstanceId);
                    PreparePbWorld(preparedLdtkWorld,destination!.LevelId);
                }
                catch (Exception ex) when (ex is IOException or
                    UnauthorizedAccessException or System.Text.Json.JsonException or
                    KeyNotFoundException or FormatException or
                    ArgumentException or NotSupportedException or InvalidOperationException)
                {
                    preparedVisuals?.Dispose();
                    error = "LDtk import failed: " + ex.Message;
                    return false; // Existing scene remains untouched.
                }
            }
            WorldSession? preparedWorld = preparedLdtkWorld ?? (destination!.LevelId switch
            {
                GameLevelIds.SharedWorld => SharedWorldClockGym.Create(
                    preparedGym.AssistancePolicy, preparedGym.FaultSensitivity,
                    worldInstanceId: ++host._nextWorldInstanceId),
                GameLevelIds.AuthoredWorld => AuthoredWorldGym.Create(
                    preparedGym.AssistancePolicy, preparedGym.FaultSensitivity,
                    host._inputRouter.SourceId,
                    worldInstanceId: ++host._nextWorldInstanceId),
                GameLevelIds.MotionLab => MotionContributionGym.Create(
                    preparedGym.AssistancePolicy, preparedGym.FaultSensitivity,
                    host._inputRouter.SourceId,
                    worldInstanceId: ++host._nextWorldInstanceId),
                GameLevelIds.RouteA or GameLevelIds.RouteB => RouteLevelGym.Create(
                    destination, roster, preparedGym.AssistancePolicy,
                    preparedGym.FaultSensitivity,
                    worldInstanceId: ++host._nextWorldInstanceId),
                _ => null
            });
            scene = new HostScene(MajorSceneKind.Gameplay, destination,
                () =>
                {
                    host.ActivatePreparedScene(preparedGym, preparedWorld,
                        destination.LevelId, importedLdtk, preparedVisuals, preparedConnectedLdtk,
                        preparedParticleZones, preparedLighting);
                    preparedSession.EnterLevel(destination, preparedWorld?.PlayerSlots);
                    host._gameplaySession = preparedSession;
                }, preparedVisuals);
            return true;
        }
    }

    private sealed class HostScene(MajorSceneKind kind,
        SceneDestination? destination, Action enter,
        LdtkVisualSceneAssets? assets = null) : IGameFlowScene
    {
        public MajorSceneKind Kind => kind;
        public SceneDestination? Destination => destination;
        public void Enter() => enter();
        public void Exit() { } // No shared GPU assets are owned by these graybox fixtures.
        private readonly SceneResourceScope _scope = new();
        // The shared SpriteBatch/pixel live at application scope. Current
        // graybox worlds own no GPU assets. Future level-local assets belong
        // in this scene scope, never in a static cache by default.
        public void Dispose()
        {
            assets?.Dispose();
            _scope.Dispose();
        }
    }

    private RadialLight? SelectedShadowLight()
    {
        if (_lightingEvents is null) return null;
        IReadOnlyList<RadialLight> lights = _lightingEvents.CurrentAuthoredLights();
        if (lights.Count == 0) return null;
        int index = _shadowSelectedLightIndex % lights.Count;
        return lights[index];
    }

    private IReadOnlyList<string> GetAuthoredLightStatuses()
    {
        if (_lightingEvents is null || _lastLightingView is not LightingViewport viewport ||
            !_lightingTools.Open) return Array.Empty<string>();
        var result = new List<string>(4);
        var lights = _lightingEvents.CurrentAuthoredLights();
        for (int i = 0; i < Math.Min(lights.Count, 4); i++)
        {
            RadialLight light = lights[i];
            string name = light.Color.R > .95 && light.Color.G < .5 ? "RED" :
                light.Color.B > .9 && light.Color.R < .7 ? "BLUE" :
                light.Color.R > .9 && light.Color.B > .65 ? "PINK" :
                light.Color.G > .8 && light.Color.R < .8 ? "GREEN" : "LIGHT " + (i + 1);
            AuthoredLightVisibility visibility = LightingVisibilityDiagnostics.Classify(
                light, viewport, _lastSelectedLights, _lightingEnabled);
            result.Add(name + ": " + visibility.ToString().ToUpperInvariant());
        }
        return result;
    }

    private string CurrentLightTypeFilterLabel() => _l20LightFilter switch
    {
        0 => "ALL (STRESS)",
        1 => "SUN / LONG SHADOWS",
        2 => "MOON / COOL RAYS",
        3 => "POINT / TORCH",
        4 => "SPOT / SEARCHLIGHT",
        5 => "TUBE / EXTENDED",
        6 => "EMISSIVE / WORLD LIGHT",
        _ => "ALL"
    };

    private IReadOnlyList<string> CurrentLightTypeNotes() => _l20LightFilter switch
    {
        1 => ["SUN: parallel amber rays across the wall and the player.",
              "EXPECT: long shadows projected down and to the right.",
              "USE: daylight, outdoor scenes, time-of-day."],
        2 => ["MOON: cool blue directional wash, different direction.",
              "EXPECT: the entire scene is affected, not one circular pool.",
              "USE: night scenes, silhouettes, atmospheric readability."],
        3 => ["POINT: isolated local falloff and caster occlusion.",
              "EXPECT: a circular patch and shadowed region behind the prop.",
              "USE: torches, magic crystals, explosions."],
        4 => ["SPOT: strongly directional cone and soft aperture.",
              "EXPECT: light bends around YOU as you cross the beam.",
              "USE: flashlights, sentries, cinematic reveals."],
        5 => ["TUBE: capsule-shaped lighting extending along a fixture.",
              "EXPECT: wide elongated environmental illumination.",
              "SHADOWS: midpoint approximation; not true multisample area shadows."],
        6 => ["EMISSIVE: test bay near spawn; cyan LEFT, magenta RIGHT.",
              "Gray strip/item and PLAYER should pick up each hue; low blocker cuts cyan.",
              "Toggle WORLD LIGHTS for A/B; EMISSION and BLOOM are independent."],
        _ => ["ALL: performance / integration stress case.",
              "Watch the WALL, ground, blocker and PLAYER together.",
              "BLOOM ONLY shows glow separately from illumination."]
    };

    private string CurrentShowcaseFocusLabel() => _l19ShowcaseFocus switch
    {
        0 => "A SELF EMISSION",
        1 => "B BLOOM",
        2 => "C TRANSPARENCY",
        3 => "D HOLOGRAM / HDR",
        _ => "WHY / WHEN"
    };

    private IReadOnlyList<string> CurrentShowcaseNotes() => _l19ShowcaseFocus switch
    {
        0 =>
        [
            "A: LEFT emits. RIGHT is unpowered.",
            "USE: runes, crystals, powered signs.",
            "WHY: glow works without world lights.",
            "TRY: SELF EMISSION vs BASELINE."
        ],
        1 =>
        [
            "B: watch pink WALL and PLAYER light.",
            "USE: neon, lava, magic portals.",
            "WHY: halo sells powerful emitters.",
            "TRY: BLOOM HERO vs SHOWCASE BOOST."
        ],
        2 =>
        [
            "C: follow crosshairs THROUGH objects.",
            "USE: glass, ghosts, forcefields.",
            "WHY: opacity != shadow casting.",
            "TRY: WORLD LIGHTS; only SOLID casts."
        ],
        3 =>
        [
            "D: LEFT emits; RIGHT is unpowered.",
            "USE: holograms, scanners, screens.",
            "WHY: HDR + bloom retain hot glow.",
            "TRY: cyan light on WALL and PLAYER."
        ],
        _ =>
        [
            "SELF EMISSION = object glows by itself.",
            "BLOOM = bright pixels bleed softly into nearby space.",
            "ALPHA: see-through, shadow independent.",
            "NEXT: sunlight and spot light types."
        ]
    };

    private int _phase21LabMode = 2;
    private bool _phase21HullOverlay;
    private int _phase21BenchmarkFrame = -1;
    private readonly List<string> _phase21Benchmark = new();
    private string _phase21BenchmarkStatus = "READY - CAPTURE CURRENT";

    private string Phase21LabTag() => _phase21LabMode switch
    {
        1 => "AREA_POINT", 2 => "AREA_LINE", 3 => "AREA_DISC", 4 => "AREA_RECT",
        5 => "AREA_RECT|SHOWCASE|EMISSIVE", _ => "AREA_RECT|SHOWCASE|EMISSIVE|STRESS"
    };

    private void ApplyPhase21LabPreset(int preset)
    {
        ApplyEmissionLabPreset(3);
        _phase21LabMode = preset;
        _perfIsolation = 0;
        ResetPerfWindow();
        _lightingPresetId = "Phase21LabNeutral";
        _lightingStress = preset == 6;
        _l19Emission = _l19Bloom = preset >= 5;
        _phase21BenchmarkFrame = -1;
        _phase21BenchmarkStatus = "READY - CAPTURE CURRENT";
        _gameLighting?.CpuSubmitWindow.Clear();
    }

    private void RecordPerfFrame(bool lightingActive)
    {
        if (PerfCaptureActive && (!IsActive || _perfCaptureWidth!=GraphicsDevice.PresentationParameters.BackBufferWidth ||
            _perfCaptureHeight!=GraphicsDevice.PresentationParameters.BackBufferHeight))
        {
            CancelPerfCapture();
            _phase21BenchmarkStatus="CANCELLED - WINDOW FOCUS OR SIZE CHANGED";
            return;
        }
        if (_phase21BenchmarkFrame >= 0)
        {
            var diagnostics = lightingActive ? _gameLighting : null;
            int viewChanged = _lastLightingView != _poPreviousView ? 1 : 0;
            _poPreviousView = _lastLightingView;
            Aabb? playerBounds = _poAutoStarted ? _worldClockTest?.GetPlayer(1).Bounds : null;
            int playerChanged = playerBounds != _poPreviousPlayerBounds ? 1 : 0;
            _poPreviousPlayerBounds = playerBounds;
            int lightHash = _poAutoStarted ? PoLightGeometryHash() : 0;
            int lightsChanged = !_poPreviousLightHashValid || lightHash != _poPreviousLightHash ? 1 : 0;
            _poPreviousLightHash = lightHash; _poPreviousLightHashValid = true;
            long serial = diagnostics?.MaskInvalidationSerial ?? 0;
            int invalidated = serial != _poPreviousMaskSerial ? 1 : 0;
            _poPreviousMaskSerial = serial;
            if(PbTickBenchmark&&!_pbTickWarmupComplete)
            {
                long tick=_worldClockTest!.WorldTick;
                _pbWarmupStartTick??=tick;
                _phase21BenchmarkFrame++;
                if(tick>=PbTargetStartTick)
                {
                    if(tick!=PbTargetStartTick||tick-_pbWarmupStartTick.Value<PbWarmupTicks)
                        throw new InvalidOperationException("PB benchmark missed its absolute warmup boundary.");
                    _pbTickWarmupComplete=true;_poAutoWarmupFrames=_phase21BenchmarkFrame;
                    _pbMeasurementStartTick=tick;
                    var d=_worldClockTest.DynamicBodies;
                    _pbStartSweeps=d?.Metrics.Sweeps??0;_pbStartContacts=d?.Metrics.Contacts??0;_pbStartPairContacts=d?.PairContacts??0;_pbStartCharacterPushes=d?.CharacterPushes??0;_pbStartCharacterImpacts=d?.CharacterBoundaryImpacts??0;_pbStartDynamicMs=d?.TotalStepMilliseconds??0;_pbStartPlatformContacts=d?.PlatformContacts??0;_pbStartPlatformStops=_worldClockTest?.PropPlatformStops??0;_pbStartDestroyed=d?.DestroyedByPlatform??0;
                }
                return;
            }
            if (_poAutoStarted && !PbTickBenchmark && _phase21BenchmarkFrame < ActivePerfWarmup)
            {
                bool persist = lightingActive && (_perfMask & 64) != 0;
                bool ready = (!lightingActive || (viewChanged == 0 && lightsChanged == 0 && invalidated == 0 &&
                    (!persist || (diagnostics?.LastSharedMaskMisses ?? 0) == 0)));
                _poWarmupTrace.Add(string.Join("\t", _phase21BenchmarkFrame+1, viewChanged,lightsChanged,invalidated,
                    diagnostics?.LastPersistentMaskHits??0,diagnostics?.LastSharedMaskMisses??0,diagnostics?.LastMaskEvictions??0,
                    diagnostics?.LastFallbackCapacity??0,diagnostics?.LastStaticFieldHits??0,ready?1:0,playerChanged,
                    _lastLightingView?.Focus.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    _lastLightingView?.Focus.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? ""));
                if (_poWarmupPolicy is not null && _poWarmupPolicy.Observe(ready))
                {
                    _poAutoWarmupFrames = _phase21BenchmarkFrame + 1;
                    _poWarmupStatus = _poWarmupPolicy.Status;
                }
            }
            int warmup = ActivePerfWarmup;
            int total = warmup + ActivePerfRecorded;
            if (_phase21BenchmarkFrame >= warmup)
            {
                if (!PbTickBenchmark && _phase21BenchmarkFrame == warmup) _pbMeasurementStartTick = _worldClockTest?.WorldTick ?? 0;
                var invariant = System.Globalization.CultureInfo.InvariantCulture;
                var r = lightingActive ? _gameLighting : null;
                string F(double value) => value.ToString("F4", invariant);
                int logicalSamples = 0;
                string sampleCounts = "";
                if (_poAutoStarted && lightingActive)
                {
                    int count = _perfIsolation == 5 ? 1 : EnginePerformanceOptions.AreaSamples != 0
                        ? EnginePerformanceOptions.AreaSamples : AreaEmitterSampling.CountForRadialBudget(LightingFrameBudget.For(_lightingQuality).ShadowRadialSamples);
                    bool listCounts = PoFixture == "adaptive";
                    foreach (var light in _lastSelectedLights)
                    {
                        if (light.Shape != LightShape.Tube) continue;
                        int n = AreaEmitterSampling.ResolveCount(light,count);
                        logicalSamples += n;
                        if (listCounts) sampleCounts += (sampleCounts.Length == 0 ? "" : "|") + n;
                    }
                }
                _phase21Benchmark.Add(string.Join(",", _phase21BenchmarkFrame-warmup+1,
                    F(_perfIntervalMs), F(_perfLastUpdateMs), F(_perfLastDrawMs), _perfLastUpdates,
                    F(r?.LastSubmitMilliseconds ?? 0), r?.LastLights ?? 0,
                    r?.LastAreaSampleEvaluations ?? 0, r?.LastShadowTriangles ?? 0,
                    r?.LastMaterialVisibilityMasks ?? 0, r?.LastLegacyVisibilityMasks ?? 0,
                    F(r?.EstimatedTargetsMegabytes ?? 0), F(r?.LastLayerCpuMilliseconds ?? 0),
                    F(r?.LastMaterialCpuMilliseconds ?? 0), F(r?.LastMaterialMaskGeometryCpuMilliseconds ?? 0),
                    F(r?.LastLayerMaskGeometryCpuMilliseconds ?? 0), F(r?.LastPostProcessCpuMilliseconds ?? 0),
                    r?.LastLayerFieldBuilds ?? 0, r?.LastLayerFieldReuses ?? 0,
                    r?.LastVisibilityGeometryBuilds ?? 0, r?.LastVisibilityGeometryReuses ?? 0,
                    r?.LastAreaSamplesPruned ?? 0, r?.LastMaterialLightPasses ?? 0,
                    r?.LastBloomPasses ?? 0, GraphicsDevice.PresentationParameters.BackBufferWidth, GraphicsDevice.PresentationParameters.BackBufferHeight,
                    (IsPerformanceLab || IsQualityLab) ? _perfMask : EnginePerformanceOptions.ProductionLightingMask, (IsPerformanceLab || IsQualityLab) ? _perfIsolation : 0, r?.LastPersistentGeometryHits ?? 0, r?.LastHardMaskHits ?? 0, (IsPerformanceLab || IsQualityLab) && _perfFreeze ? 1 : 0,
                    F(_perfSimulationMs),F(_perfAudioMs),F(_perfParticleUpdateMs),F(_perfLightUpdateMs),F(_perfDiagnosticMs),F(_perfEventsMs),
                    F(_perfSceneDrawMs),F(_perfParticleDrawMs),F(_perfLightPrepareMs),F(_perfHudMs),
                    _perfFrameAllocatedBytes,_perfFrameGc0,_perfFrameGc1,_perfFrameGc2,
                    _perfDrawCalls,_perfPrimitives,_perfSprites,_perfTargetChanges,
                    r?.LastSharedMaskHits??0,r?.LastPersistentMaskHits??0,r?.LastSharedMaskMisses??0,r?.LastSharedMaskFallbacks??0,r?.SharedMaskTargets??0,r?.LastTargetBinds??0,
                    r?.LastSoftFilterPasses??0,r?.LastSoftFilterPixels??0,r?.LastMaterialShadePixels??0,
                    IsFixedTimeStep?1:0,_graphics.SynchronizeWithVerticalRetrace?1:0,F(_perfCameraMs),_perfDrawnParticles,_perfWorldEffects,_perfUiEffects,F(_poPresentWallMs),r?.LastFallbackCurrentFrame??0,r?.LastFallbackCapacity??0,
                    r?.LightWidth??0,r?.LightHeight??0,r?.MaskWidth??0,r?.MaskHeight??0,
                    r?.LastLightCompositePixels??0,r?.LastMaskRasterPixels??0,r?.LastClearPixels??0,
                    r?.LastCompatibleFieldHits??0,r?.LastRegionalMasks??0,r?.LastStaticFieldHits??0,r?.LastBloomPixels??0,
                    viewChanged,lightsChanged,invalidated,r?.LastMaskEvictions??0,playerChanged,
                    logicalSamples,sampleCounts,r?.LastStaticShadowFieldHits??0,
                    r?.LastGpuBufferedDraws??0,r?.LastGpuReferenceDraws??0,r?.LastGpuFallbackDraws??0,r?.LastGpuUploadBytes??0,r?.GpuBufferBytes??0,r?.LastGpuResidentHits??0,r?.LastGpuResidentMisses??0,r?.LastGpuResidentBypasses??0,r?.LastGpuIndexedDraws??0,r?.LastGpuIndexBypasses??0,r?.LastGpuIndexUploadBytes??0,r?.LastGpuLocalBuilds??0,r?.LastGpuLocalHits??0,r?.LastGpuLocalPriorHits??0,r?.LastGpuLocalBypasses??0,r?.GpuLocalBytes??0,r?.LastGpuSpecializedPasses??0,r?.LastGpuFusedLayers??0,r?.LastGpuDirectLights??0,r?.LastGpuFastBloom??0,_particleVfx?.LastGpuInstances??0,_particleVfx?.LastGpuDraws??0,_particleVfx?.LastGpuAnalytic??0,_particleVfx?.LastGpuFallbacks??0,_particleVfx?.LastGpuUploadBytes??0,F(_gpuTimer.LastMilliseconds),_gpuTimer.Samples,_gpuTimer.Skipped,F(r?.LastGpuShadowPrepareMs??0),F(r?.LastGpuShadowDrawMs??0),r?.LastGpuSkippedBinds??0,r?.LastGpuSkippedClearPixels??0));
            }
            _phase21BenchmarkFrame++;
            _phase21BenchmarkStatus = PbTickBenchmark ? $"RECORDING TICKS {_worldClockTest!.WorldTick-_pbMeasurementStartTick}/{PbRecordedTicks}" : "CAPTURE " +
                (_phase21BenchmarkFrame < warmup ? $"WARMUP {_phase21BenchmarkFrame}/{warmup}" :
                    $"RECORDING {_phase21BenchmarkFrame-warmup}/{total-warmup}");
            bool complete=PbTickBenchmark ? _worldClockTest!.WorldTick-_pbMeasurementStartTick>=PbRecordedTicks : _phase21BenchmarkFrame>=total;
            if (complete)
            {
                _phase21BenchmarkFrame = -1;
                try
                {
                    string folder = !string.IsNullOrWhiteSpace(_poAutoOutput) ? _poAutoOutput :
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PlatformerSandbox","LightingBenchmarks");
                    Directory.CreateDirectory(folder);
                    {
#if DEBUG
                        const string build = "Debug";
#else
                        const string build = "Release";
#endif
                        File.WriteAllLines(Path.Combine(folder,_perfRunId+"-metadata.txt"),new[]{
                            "Build: "+build,"Debugger: "+System.Diagnostics.Debugger.IsAttached,
                            "GPU backend: "+GpuBackendName,"GPU adapter: "+GraphicsDevice.Adapter.Description,"OS: "+Environment.OSVersion,
                            "Graphics profile: "+GraphicsDevice.GraphicsProfile,
                            "HiDef supported: "+GraphicsDevice.Adapter.IsProfileSupported(GraphicsProfile.HiDef),
                            "GPU shadow buffers requested: "+EnginePerformanceOptions.GpuShadowBuffers,
                            "GPU shadow buffer status: "+(_gameLighting?.GpuBufferStatus??"NOT EXERCISED"),
                            "GPU resident requested: "+EnginePerformanceOptions.GpuShadowResident,"GPU indexed requested: "+EnginePerformanceOptions.GpuShadowIndexed,
                            "GPU resident status: "+(_gameLighting?.GpuResidentStatus??"NOT EXERCISED"),"GPU index status: "+(_gameLighting?.GpuIndexStatus??"NOT EXERCISED"),
                            "GPU scoped_binds: "+EnginePerformanceOptions.GpuScopedBinds,"GPU material_clear: "+EnginePerformanceOptions.GpuMaterialClear,"GPU local_masks: "+EnginePerformanceOptions.GpuLocalMasks, "GPU specialized_materials: "+EnginePerformanceOptions.GpuSpecializedMaterials, "GPU fused_layer: "+EnginePerformanceOptions.GpuFusedLayer, "GPU batch_unshadowed: "+EnginePerformanceOptions.GpuBatchUnshadowed, "GPU fast_bloom: "+EnginePerformanceOptions.GpuFastBloom, "GPU instanced_particles: "+EnginePerformanceOptions.GpuInstancedParticles, "GPU particle stress: "+GpuStressCount,"GPU particle motion: "+GpuStressMotion,
                            "Process ID: "+Environment.ProcessId,"Executable: "+Environment.ProcessPath,"Light coverage: "+(UseManualLightCoverage?"Preserve":"Budget"),"GPU timestamp status: "+_gpuTimer.Status,"GPU GL renderer: "+_gpuTimer.Adapter,"GPU GL driver: "+_gpuTimer.Driver,
                            "Runtime: "+Environment.Version,"Logical processors: "+Environment.ProcessorCount,
                            "Fixed timestep: "+IsFixedTimeStep,"Requested VSync: "+_graphics.SynchronizeWithVerticalRetrace,
                            "Target elapsed: "+TargetElapsedTime,"Lighting quality: "+_lightingQuality,
                            "Scene: "+_poCaptureScene,"Lighting enabled: "+_poCaptureLighting,"Lighting preset: "+_poCapturePreset,
                            "Lab mode: "+_phase21LabMode,"Isolation: "+_perfIsolation,"Motion frozen: "+_perfFreeze,
                            "Spatial mode: "+SpatialQueryPolicy.Current,"Spatial queries: "+SpatialCollisionDiagnostics.Snapshot.Queries,
                            "PB measurement start tick: "+_pbMeasurementStartTick,"PB measurement end tick: "+(_worldClockTest?.WorldTick??0),
                            "Automatic case: "+Environment.GetEnvironmentVariable("PS_PO_AUTO_CASE"),
                            "Warmup frames: "+warmup,"Recorded frames: "+_phase21Benchmark.Count,
                            "PB tick benchmark: "+PbTickBenchmark,"PB warmup start tick: "+(_pbWarmupStartTick??0),
                            "PB workload version: 5", "PB measured platform commands: "+_pbMeasuredPlaybackRequests,
                            "PB measured deaths: "+_pbMeasuredDeaths,"PB measured respawns: "+_pbMeasuredRespawns,
                            "PB dynamic bodies: "+(_worldClockTest?.DynamicBodies?.Bodies.Count??0),
                            "PB dynamic sweeps: "+((_worldClockTest?.DynamicBodies?.Metrics.Sweeps??0)-_pbStartSweeps),
                            "PB dynamic contacts: "+((_worldClockTest?.DynamicBodies?.Metrics.Contacts??0)-_pbStartContacts),
                            "PB prop pair impacts: "+((_worldClockTest?.DynamicBodies?.PairContacts??0)-_pbStartPairContacts),
                            "PB character pushes: "+((_worldClockTest?.DynamicBodies?.CharacterPushes??0)-_pbStartCharacterPushes),
                            "PB character boundary impacts: "+((_worldClockTest?.DynamicBodies?.CharacterBoundaryImpacts??0)-_pbStartCharacterImpacts),
                            "PB platform prop contacts: "+((_worldClockTest?.DynamicBodies?.PlatformContacts??0)-_pbStartPlatformContacts),
                            "PB platform prop stops: "+((_worldClockTest?.PropPlatformStops??0)-_pbStartPlatformStops),
                            "PB platform destroyed props: "+((_worldClockTest?.DynamicBodies?.DestroyedByPlatform??0)-_pbStartDestroyed),
                            "PB dynamic CPU ms: "+FormattableString.Invariant($"{(_worldClockTest?.DynamicBodies?.TotalStepMilliseconds??0)-_pbStartDynamicMs:R}"),
                            "Warmup minimum: "+(_poAutoStarted?_poMinimumWarmup:warmup), "Warmup maximum: "+(_poAutoStarted?_poMaximumWarmup:warmup),
                            "Warmup status: "+_poWarmupStatus,"Fixture: "+(_poAutoStarted?PoFixture:InteractiveLab?_qualityLabFixture:"normal"),"Camera locked: "+(_poAutoStarted&&PoLockCamera), "Camera pan: "+(_poAutoStarted&&PoPanCamera || InteractiveLab&&_qualityLabPan), "Camera convergence: "+EnginePerformanceOptions.CameraConvergence,
                            "Fixture coordinates: "+((_poAutoStarted ? PoFixture : InteractiveLab ? _qualityLabFixture : "normal") == "normal" ? "authored" : BenchmarkLightingFixtureSet.CoordinateVersion),
                            "Overlay: "+_poCaptureOverlay,"HUD layout: compact_v2","Render cap: "+EffectiveRenderCap,
                            "Inactive cap: "+EnginePerformanceOptions.InactiveCap,
                            "PO snapshots: "+EnginePerformanceOptions.Snapshots,"PO HUD atlas: "+EnginePerformanceOptions.HudAtlas,
                            "PO artwork: "+EnginePerformanceOptions.ArtworkPreparation,"PO diagnostics: "+EnginePerformanceOptions.DiagnosticRecords,
                            "PO light validation: "+EnginePerformanceOptions.LightingValidation,"PO light buffers: "+EnginePerformanceOptions.LightingBuffers,
                            "PO light parameters: "+EnginePerformanceOptions.LightingParameters,"PO light samples: "+EnginePerformanceOptions.LightingSamples,
                            "Production lighting mask: "+EnginePerformanceOptions.ProductionLightingMask,
                            "PO B area samples: "+EnginePerformanceOptions.AreaSamples,
                            "PO B shared samples: "+EnginePerformanceOptions.SharedSamples,
                            "PO B hashed visibility: "+EnginePerformanceOptions.HashedVisibility,
                            "PO B visible layers: "+EnginePerformanceOptions.VisibleLayers,
                            "PO B prepared materials: "+EnginePerformanceOptions.PreparedMaterials,
                            "PO 10 light scale: "+EnginePerformanceOptions.Po10LightScale,
                            "PO 10 mask scale: "+EnginePerformanceOptions.Po10MaskScale,
                            "PO 10 material regions: "+EnginePerformanceOptions.Po10MaterialRegions,
                            "PO 10 light regions: "+EnginePerformanceOptions.Po10LightRegions,
                            "PO 10 shared fields: "+EnginePerformanceOptions.Po10SharedFields,
                            "PO 10 skip clears: "+EnginePerformanceOptions.Po10SkipClears,
                            "PO 10 cache capacity: "+EnginePerformanceOptions.Po10CacheCapacity,
                            "PO 10 shadow edge: "+_shadowEdgePreset,
                            "PO 10B bloom scale: "+EnginePerformanceOptions.Po10bBloomScale,
                            "PO 10B adaptive: "+EnginePerformanceOptions.Po10bAdaptiveSamples,
                            "PO 10B static fields: "+EnginePerformanceOptions.Po10bStaticFields,
                            "PO 10F static shadows: "+EnginePerformanceOptions.Po10fStaticShadowFields,
                            "PO 10F caster buffers: "+EnginePerformanceOptions.Po10fCasterBuffers,
                            "Draw intervals include pacing; EndDraw wall time may include swap/driver wait; neither is a GPU timestamp or physical display time. Particle draw includes nested lighting."});
                    }
                    if (_poAutoStarted) File.WriteAllLines(Path.Combine(folder,"warmup_trace.tsv"),
                        new[]{"frame\tview_changed\tlights_changed\tmask_invalidated\tpersistent_hits\tmask_misses\tmask_evictions\tcapacity_fallbacks\tstatic_hits\tready\tplayer_changed\tview_focus_x\tview_focus_y"}.Concat(_poWarmupTrace));
                    string name=_perfRunId+"-mode"+_phase21LabMode+"-"+_lightingQuality+"-mask"+((IsPerformanceLab || IsQualityLab) ? _perfMask : EnginePerformanceOptions.ProductionLightingMask)+"-isolation"+((IsPerformanceLab || IsQualityLab) ? _perfIsolation : 0)+"-case00.csv";
                    File.WriteAllLines(Path.Combine(folder,name),new[]{"frame,preceding_frame_ms,update_cpu_ms,previous_draw_cpu_ms,updates,cpu_submit_ms,logical_lights,area_sample_evaluations,triangles,material_masks,layer_masks,estimated_target_mb,layer_cpu_ms,material_cpu_ms,material_geometry_cpu_ms,layer_geometry_cpu_ms,post_cpu_ms,layer_builds,layer_reuses,geometry_builds,geometry_reuses,pruned_samples,material_passes,bloom_passes,width,height,optimization_mask,isolation,persistent_hits,hard_mask_hits,freeze_lights,simulation_cpu_ms,audio_cpu_ms,particle_update_cpu_ms,light_update_cpu_ms,diagnostic_cpu_ms,event_cpu_ms,scene_draw_cpu_ms,particle_draw_cpu_ms,light_prepare_cpu_ms,debug_hud_cpu_ms,thread_alloc_bytes,gc0,gc1,gc2,draw_calls,primitives,sprites,target_changes,shared_mask_hits,persistent_mask_hits,shared_mask_misses,shared_mask_fallbacks,shared_mask_targets,lighting_target_binds,soft_filter_passes,soft_filter_pixels,material_shade_pixels,fixed_step,vsync,camera_animation_cpu_ms,drawn_particles,active_world_effects,active_ui_effects,present_wall_ms,mask_fallback_current_frame,mask_fallback_capacity,light_width,light_height,mask_width,mask_height,light_composite_pixels,mask_raster_bound_pixels,selected_clear_pixels,compatible_field_hits,regional_masks,static_field_hits,bloom_pixels,view_changed,lights_changed,mask_invalidated,mask_evictions,player_changed,logical_area_samples,area_sample_counts,static_shadow_field_hits,gpu_buffered_draws,gpu_reference_draws,gpu_fallback_draws,gpu_upload_bytes,gpu_buffer_bytes,gpu_resident_hits,gpu_resident_misses,gpu_resident_bypasses,gpu_indexed_draws,gpu_index_bypasses,gpu_index_upload_bytes,gpu_local_builds,gpu_local_hits,gpu_local_prior_hits,gpu_local_bypasses,gpu_local_bytes,gpu_specialized_passes,gpu_fused_layers,gpu_direct_lights,gpu_fast_bloom,gpu_particle_instances,gpu_particle_draws,gpu_particle_analytic,gpu_particle_fallbacks,gpu_particle_upload_bytes,gpu_timestamp_ms,gpu_timestamp_samples,gpu_timestamp_skipped,gpu_shadow_prepare_wall_ms,gpu_shadow_draw_wall_ms,gpu_skipped_binds,gpu_skipped_clear_pixels"}.Concat(_phase21Benchmark));
                    _phase21BenchmarkStatus="SAVED: " + name;
                    if (_poAutoStarted) _poAutoSaved = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { _phase21BenchmarkStatus="BENCHMARK SAVE FAILED"; }
            }
        }
    }

    private void DrawPhase21LabHud(bool lightingActive)
    {
        if (lightingActive && _phase21HullOverlay && _lastLightingView is LightingViewport view && _lightingEvents?.CurrentScene() is LdtkLightingScene scene)
        {
            void Line(LightPoint a, LightPoint b, Color color)
            {
                Vector2 p = new((float)a.X, (float)a.Y), q = new((float)b.X, (float)b.Y);
                Vector2 delta = q-p;
                _spriteBatch.Draw(_pixel,p,null,color,(float)Math.Atan2(delta.Y,delta.X),Vector2.Zero,
                    new Vector2(delta.Length(),2),SpriteEffects.None,0);
            }
            foreach (LightingOccluder hull in scene.Occluders)
                for (int i=0;i<hull.Vertices.Length;i++) Line(view.Project(hull.Vertices[i]),
                    view.Project(hull.Vertices[(i+1)%hull.Vertices.Length]),hull.LitFaceDepthZ>0?Color.Orange:Color.LimeGreen);
            foreach (RadialLight light in _lastSelectedLights)
            {
                foreach (RadialLight sample in AreaEmitterSampling.Samples(light,
                    AreaEmitterSampling.ResolveCount(light, _perfIsolation == 5 ? 1 : EnginePerformanceOptions.AreaSamples != 0 ? EnginePerformanceOptions.AreaSamples : AreaEmitterSampling.CountForRadialBudget(LightingFrameBudget.For(_lightingQuality).ShadowRadialSamples))))
                {
                    LightPoint p = view.Project(sample.Position);
                    _spriteBatch.Draw(_pixel,new Rectangle((int)p.X-3,(int)p.Y-3,6,6),Color.Yellow);
                }
                if (light.Shape == LightShape.Directional) continue;
                double radius = (light.Radius+AreaEmitterSampling.Extent(light))*view.PixelsPerUnit;
                LightPoint center = view.Project(light.Position);
                for(int i=0;i<32;i++)
                {
                    double a=i*Math.PI/16, b=(i+1)*Math.PI/16;
                    Line(center+new LightPoint(Math.Cos(a)*radius,Math.Sin(a)*radius),
                        center+new LightPoint(Math.Cos(b)*radius,Math.Sin(b)*radius),new Color(170,170,100,150));
                }
            }
        }
        if (IsQualityLab || IsPerformanceLab) return;
        const int y=42;
        _spriteBatch.Draw(_pixel,new Rectangle(12,y,Math.Min(850,GraphicsDevice.Viewport.Width-24),86),new Color(15,19,24,235));
        GymDebugText.Draw(_spriteBatch,_pixel,20,y+7,"1 POINT / 2 LINE / 3 DISC / 4 RECT / 5 SHOWCASE / 6 STRESS",Color.White,1);
        GymDebugText.Draw(_spriteBatch,_pixel,20,y+21,"7 HULLS / 8 QUALITY / 9 CAPTURE",Color.LightGray,1);
        GymDebugText.Draw(_spriteBatch,_pixel,20,y+35,"MODE " + _phase21LabMode + " / " + _lightingQuality.ToString().ToUpperInvariant() + " / LIGHTS " + (lightingActive ? _lastSelectedLights.Count : 0) + " / SAMPLE WORK " + (lightingActive ? _gameLighting?.LastAreaSampleEvaluations ?? 0 : 0),new Color(167,232,235),1);
        GymDebugText.Draw(_spriteBatch,_pixel,20,y+49,"CPU MS AVG " + (lightingActive ? _gameLighting?.CpuSubmitWindow.Mean ?? 0 : 0).ToString("F2") + " P95 " + (lightingActive ? _poLightP95 : 0).ToString("F2") + " (NOT GPU TIME)",Color.LightGray,1);
        GymDebugText.Draw(_spriteBatch,_pixel,20,y+63,_phase21BenchmarkStatus,Color.LightGray,1);

    }

    // Presentation-only controls. A single layout describes both drawing and
    // hit testing, so resize/collapse/page changes cannot leave stale hitboxes.
    private static readonly string[] PerfWorkloads =
        { "POINT", "TUBE / LINE", "DISC", "RECTANGLE", "SHOWCASE", "STRESS" };
    private static readonly string[] PerfDiagnostics =
        { "FULL QUALITY", "LIGHTING OFF", "SHADOWS OFF", "BLOOM OFF", "PARTICLES OFF", "SINGLE SAMPLE", "DEV HUD OFF", "NO LIGHT/VFX", "NO WORLD DRAW" };
    private static readonly string[] PerfOptimizationNames =
        { "FRAME GEO CACHE", "LAYER FIELD REUSE", "SAMPLE CULLING", "PERSIST GEO CACHE", "GPU MASK REUSE", "SHARED SOFT MASKS", "PERSIST GPU MASKS" };
    private static readonly LightingQuality[] PerfQualities =
        { LightingQuality.Low, LightingQuality.Balanced, LightingQuality.High };
    private void PerfDrawText(Rectangle bounds,string text,Color color,int preferredScale=1,bool centered=false)
    {
        // GymDebugText is a 5x7 bitmap font with a six-pixel character advance.
        // Fit every label into its actual draw/hit rectangle after a resize.
        int scale = preferredScale;
        if (text.Length*6*scale>bounds.Width || 7*scale>bounds.Height) scale=1;
        int capacity = Math.Max(0,bounds.Width/(6*scale));
        if(capacity==0 || bounds.Height<7*scale) return;
        if(text.Length>capacity) text=capacity>3?text[..(capacity-3)]+"...":text[..capacity];
        int tx = centered ? bounds.X+(bounds.Width-text.Length*6*scale)/2 : bounds.X;
        int ty = bounds.Y+Math.Max(0,(bounds.Height-7*scale)/2);
        GymDebugText.Draw(_spriteBatch,_pixel,tx,ty,text,color,scale);
    }

    private bool _emissionLabDarkWall;

    private void ApplyEmissionLabPreset(int preset)
    {
        _emissionLabDarkWall = preset == 5;
        _lightingPresetId = "EmissiveLabNeutral";
        _lightingEnabled = true;
        _lightingQuality = LightingQuality.High;
        _lightingStress = _lightingInspectionLight = false;
        _materialLightMode = ProductionMaterialLightMode.AllEligible;
        _l19bMaterialPreset = -1;
        _l20LightFilter = 6;
        _l19dWorldLightsEnabled = preset is 3 or 4 or 5;
        _l19Emission = preset is 2 or 4;
        _l19Bloom = preset == 4;
        _l20BloomOnly = false;
        _l19Hdr = true;
        _l19Exposure = 1f;
        _l19BloomStrength = .45f;
        _l19BloomThreshold = 1.15f;
        _l20BloomScatter = .55f;
        _shadowEdgePreset = ShadowEdgePreset.Hard;
        _shadowQualityPolicy = ShadowQualityPolicy.Manual;
        _shadowFilterMode = ShadowFilterMode.UniformGaussian;
        if (_lightingEvents is not null)
        {
            _lightingEvents.Enabled = true;
            _lightingEvents.Quality = _lightingQuality;
        }
    }

    private void ApplyMaterialShowcasePreset(int preset)
    {
        _l19bMaterialPreset = preset;
        _lightingStress = false;
        switch (preset)
        {
            case 0: // Baseline
                _materialLightMode = ProductionMaterialLightMode.AllEligible;
                _l19Hdr = false;
                _l19Emission = false;
                _l19Bloom = false;
                _l19Exposure = 1f;
                _l19BloomThreshold = 1.75f;
                _l19BloomStrength = .25f;
                _l19ShowcaseFocus = 0;
                _l19dWorldLightsEnabled = true;
                break;
            case 1: // Self emission only
                _materialLightMode = ProductionMaterialLightMode.EmissionOnly;
                _l19Hdr = true;
                _l19Emission = true;
                _l19Bloom = false;
                _l19Exposure = 1f;
                _l19BloomThreshold = 1.75f;
                _l19BloomStrength = .25f;
                _l19ShowcaseFocus = 0;
                _l19dWorldLightsEnabled = false; // prove emitter needs no authored local light
                break;
            case 2: // Bloom hero
                _materialLightMode = ProductionMaterialLightMode.EmissionOnly;
                _l19Hdr = true;
                _l19Emission = true;
                _l19Bloom = true;
                _l19Exposure = .90f;
                _l19BloomThreshold = .65f;
                _l19BloomStrength = 1.25f;
                _l19ShowcaseFocus = 1;
                _l19dWorldLightsEnabled = false; // isolate bloom from magenta world light
                break;
            case 3: // Transparency
                _materialLightMode = ProductionMaterialLightMode.AllEligible;
                _l19Hdr = true;
                _l19Emission = false;
                _l19Bloom = false;
                _l19Exposure = 1f;
                _l19BloomThreshold = 1.05f;
                _l19BloomStrength = .25f;
                _l19ShowcaseFocus = 2;
                _l19dWorldLightsEnabled = true;
                break;
            case 4: // Ghost / opacity
                _materialLightMode = ProductionMaterialLightMode.AllEligible;
                _l19Hdr = true;
                _l19Emission = false;
                _l19Bloom = false;
                _l19Exposure = .95f;
                _l19BloomThreshold = 1.05f;
                _l19BloomStrength = .25f;
                _l19ShowcaseFocus = 2;
                _l19dWorldLightsEnabled = true;
                break;
            case 5: // Hologram / HDR
                _materialLightMode = ProductionMaterialLightMode.EmissionOnly;
                _l19Hdr = true;
                _l19Emission = true;
                _l19Bloom = true;
                _l19Exposure = 1.15f;
                _l19BloomThreshold = .85f;
                _l19BloomStrength = .80f;
                _l19ShowcaseFocus = 3;
                _l19dWorldLightsEnabled = true;
                break;
            case 6: // Showcase boost
                _materialLightMode = ProductionMaterialLightMode.AllEligible;
                _l19Hdr = true;
                _l19Emission = true;
                _l19Bloom = true;
                _l19Exposure = .80f;
                _l19BloomThreshold = .60f;
                _l19BloomStrength = 1.35f;
                _l19ShowcaseFocus = 4;
                _l19dWorldLightsEnabled = true;
                break;
            default:
                _l19bMaterialPreset = -1;
                break;
        }
    }

    private void ApplyLightingToolAction(LightingToolAction action)
    {
        if (action is LightingToolAction.ToggleHdr or
            LightingToolAction.ToggleEmission or LightingToolAction.ToggleBloom or
            LightingToolAction.ToggleMaterialLights or LightingToolAction.CycleExposure or
            LightingToolAction.CycleBloomThreshold or LightingToolAction.CycleBloomStrength)
            _l19bMaterialPreset = -1;
        switch (action)
        {
            case LightingToolAction.ToggleLights:
                _lightingEnabled = !_lightingEnabled;
                break;
            case LightingToolAction.ToggleShowcaseWorldLights:
                _l19dWorldLightsEnabled = !_l19dWorldLightsEnabled;
                _l19bMaterialPreset = -1;
                break;
            case LightingToolAction.NextPreset:
                if (_lightingCatalog is not null)
                {
                    string[] presets = new List<string>(_lightingCatalog.GetPresetIdsForScene(
                        _ldtkLighting?.LevelId)).ToArray();
                    if (presets.Length > 0)
                    {
                        int current = Array.IndexOf(presets, _lightingPresetId);
                        _lightingPresetId = presets[(current + 1) % presets.Length];
                        _lightingPresetNoticeSeconds = 4;
                    }
                }
                break;
            case LightingToolAction.NextQuality:
                _lightingQuality = _lightingQuality switch
                {
                    LightingQuality.Low => LightingQuality.Balanced,
                    LightingQuality.Balanced => LightingQuality.High,
                    _ => LightingQuality.Low
                };
                break;
            case LightingToolAction.ToggleMaterialLights:
                _materialLightMode = _materialLightMode switch
                {
                    ProductionMaterialLightMode.AllEligible => ProductionMaterialLightMode.CompareTwo,
                    ProductionMaterialLightMode.CompareTwo => ProductionMaterialLightMode.EmissionOnly,
                    _ => ProductionMaterialLightMode.AllEligible
                };
                break;
            case LightingToolAction.ToggleToolsPage:
                break; // The panel owns its page state.
            case LightingToolAction.CycleMaterialValidationPreset:
                ApplyMaterialShowcasePreset((_l19bMaterialPreset + 1 + 7) % 7);
                break;
            case LightingToolAction.CycleShowcaseFocus:
                _l19ShowcaseFocus = (_l19ShowcaseFocus + 1) % 5;
                break;
            case LightingToolAction.ToggleHdr:
                _l19Hdr = !_l19Hdr;
                break;
            case LightingToolAction.ToggleEmission:
                _l19Emission = !_l19Emission;
                break;
            case LightingToolAction.ToggleBloom:
                _l19Bloom = !_l19Bloom;
                break;
            case LightingToolAction.CycleExposure:
                _l19Exposure = _l19Exposure < 1f ? 1.15f :
                    _l19Exposure < 1.5f ? 1.7f : 0.8f;
                break;
            case LightingToolAction.CycleBloomThreshold:
                _l19BloomThreshold = _l19BloomThreshold < 1f ? 1.05f :
                    _l19BloomThreshold < 1.5f ? 1.75f : 0.65f;
                break;
            case LightingToolAction.CycleBloomStrength:
                _l19BloomStrength = _l19BloomStrength < 0.4f ? 0.45f :
                    _l19BloomStrength < 0.9f ? 1.0f : 0.25f;
                break;
            case LightingToolAction.ToggleReceiverNormals:
                if (_ldtkLighting?.LevelId == GameLevelIds.LightTypesLab) _l20ReceiverNormals = !_l20ReceiverNormals;
                break;
            case LightingToolAction.TogglePolygonRims:
                if (_ldtkLighting?.LevelId == GameLevelIds.LightTypesLab) _l20PolygonRims = !_l20PolygonRims;
                break;
            case LightingToolAction.CycleReceiverProjection:
                if (_ldtkLighting?.LevelId == GameLevelIds.LightTypesLab)
                    _l20ReceiverMode = _l20ReceiverMode switch
                    {
                        ReceiverProjectionMode.Flat => ReceiverProjectionMode.Clipped,
                        ReceiverProjectionMode.Clipped => ReceiverProjectionMode.Folded,
                        _ => ReceiverProjectionMode.Flat
                    };
                break;
            case LightingToolAction.CycleLightTypeFilter:
                if (_ldtkLighting?.LevelId == GameLevelIds.LightTypesLab)
                {
                    _l20LightFilter = (_l20LightFilter + 1) % 6;
                    _l19dWorldLightsEnabled = true;
                }
                break;
            case LightingToolAction.CycleBloomScatter:
                _l20BloomScatter = _l20BloomScatter < .30f ? .55f :
                    _l20BloomScatter < .70f ? .85f :
                    _l20BloomScatter < .98f ? 1.0f : 0.15f;
                break;
            case LightingToolAction.ToggleBloomOnly:
                _l20BloomOnly = !_l20BloomOnly;
                if (_l20BloomOnly) _l19Bloom = true;
                break;
            case LightingToolAction.CycleShadowEdges:
                _shadowEdgePreset = ShadowEdgeSettings.Next(_shadowEdgePreset);
                // A direct width choice should be immediately visible.
                _shadowQualityPolicy = ShadowQualityPolicy.Manual;
                break;
            case LightingToolAction.ToggleShadowFilter:
                _shadowFilterMode = ShadowContactControls.Next(_shadowFilterMode);
                break;
            case LightingToolAction.ToggleShadowQualityPolicy:
                _shadowQualityPolicy = ShadowContactControls.Next(_shadowQualityPolicy);
                break;
            case LightingToolAction.SelectShadowLight:
                _shadowSelectedLightIndex++;
                break;
            case LightingToolAction.CycleShadowEmitter:
                if (SelectedShadowLight() is RadialLight selected)
                {
                    float current = _shadowEmitterOverrides.TryGetValue(selected.Id,
                        out float sourceRadius) ? sourceRadius : 8f;
                    _shadowEmitterOverrides[selected.Id] = ShadowContactControls.NextEmitter(current);
                }
                break;
            case LightingToolAction.ToggleReducedFlashes:
                _reducedLightingFlashes = !_reducedLightingFlashes;
                break;
            case LightingToolAction.ToggleStress:
                _lightingStress = !_lightingStress;
                break;
            case LightingToolAction.ToggleWallMotion:
                if (_lightingEvents is not null)
                    _lightingEvents.WallMotionEnabled = !_lightingEvents.WallMotionEnabled;
                break;
            case LightingToolAction.ToggleInspectionLight:
                if (_ldtkLighting?.LevelId == "LIGHTING_FLAT_TEST")
                    _lightingInspectionLight = !_lightingInspectionLight;
                break;
            case LightingToolAction.CycleHud:
                _debugHudMode = GameDebugHudModes.Next(_debugHudMode);
                _debugHudUserSelected = true;
                break;
            case LightingToolAction.ToggleCollision:
                _ldtkShowCollisionDebug = !_ldtkShowCollisionDebug;
                break;
            case LightingToolAction.ToggleBackdrop:
                _ldtkDrawCollisionBackdrop = !_ldtkDrawCollisionBackdrop;
                break;
            case LightingToolAction.ToggleAudioMute:
                if (_audio is not null)
                {
                    _audio.SetMute(!_audio.IsMuted);
                    RefreshMusicVolume();
                    ShowAudioMessage(_audio.IsMuted ? "AUDIO MUTED" : "AUDIO UNMUTED");
                }
                break;
            case LightingToolAction.VolumeDown:
                SetDeveloperMasterVolume(-.1f);
                break;
            case LightingToolAction.VolumeUp:
                SetDeveloperMasterVolume(.1f);
                break;
            case LightingToolAction.ExportReport:
                _diagnostics?.ExportReport();
                break;
        }
        if (_lightingEvents is not null)
        {
            _lightingEvents.Enabled = _lightingEnabled;
            _lightingEvents.Quality = _lightingQuality;
            _lightingEvents.ReducedFlashes = _reducedLightingFlashes;
        }
    }

    private void SetDeveloperMasterVolume(float step)
    {
        if (_audio is null) return;
        float next = Math.Clamp(_audio.Mixer.MasterVolume + step, 0f, 1f);
        _audio.SetMasterVolume(next);
        RefreshMusicVolume();
        try
        {
            AudioSettingsStore.Save(_audioSettingsPath,
                AudioUserSettings.FromMixer(_audio.Mixer));
            ShowAudioMessage("MASTER VOLUME " + (int)Math.Round(next * 100) + "%");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Debug.WriteLine("Audio settings save failed: " + ex);
            ShowAudioMessage("VOLUME UPDATED - SAVE FAILED");
        }
    }

    private void HandleSceneNavigation(KeyboardState keys, GamePadState gamepad,
        bool uiClicked, Point mousePosition)
    {
        // Prevent a key press from acting on both a transition and the
        // arriving scene. Also reject modal navigation during covered loads.
        if (_flow.IsTransitionActive) return;
        bool newStart = JustPressed(gamepad, Buttons.Start);
        bool newBack = JustPressed(keys, Keys.Escape) ||
            JustPressed(gamepad, Buttons.B);
        bool newConfirm = JustPressed(keys, Keys.Enter) ||
            JustPressed(keys, Keys.Space) && _flow.IsMenuOpen ||
            JustPressed(gamepad, Buttons.A);
        bool newUp = JustPressed(keys, Keys.Up) || JustPressed(keys, Keys.W) ||
            JustPressed(gamepad, Buttons.DPadUp);
        bool newDown = JustPressed(keys, Keys.Down) || JustPressed(keys, Keys.S) ||
            JustPressed(gamepad, Buttons.DPadDown);

        if (_flow.ActiveScene == MajorSceneKind.Title && !_flow.Ui.HasOverlay &&
            JustPressed(keys, Keys.Q))
            _flow.Execute(GameUiCommand.Quit);
        if (uiClicked && _flow.CurrentMenu is GameUiScreen openMenu)
        {
            int index = GameMenuLayout.Hit(GraphicsDevice.Viewport.Width,
                GraphicsDevice.Viewport.Height, openMenu, _flow.MenuItems.Count,
                _flow.SelectionIndex, mousePosition.X, mousePosition.Y);
            if (index >= 0)
            {
                while (_flow.SelectionIndex != index) _flow.Navigate(1);
                _flow.ConfirmSelection();
                return;
            }
            // Controls and other overlays also support the Escape/Back action.
            if (openMenu == GameUiScreen.Controls)
            {
                var bounds = GameMenuLayout.Get(GraphicsDevice.Viewport.Width,
                    GraphicsDevice.Viewport.Height, openMenu, _flow.MenuItems.Count,
                    _flow.SelectionIndex);
                if (new Rectangle(bounds.Left + 18, bounds.Top + 187,
                    bounds.Width - 36, 23).Contains(mousePosition))
                {
                    _flow.Back();
                    return;
                }
            }
        }
        if (newStart || JustPressed(keys, Keys.P))
        {
            if (_flow.ActiveScene == MajorSceneKind.Gameplay)
            {
                _flow.PauseOrResume();
                _audio?.Play(new AudioCueRequest("ui.confirm", "ui"));
                PlayParticleUi(ParticleUiFeedback.Confirm);
            }
            return;
        }
        if (newBack)
        {
            _flow.Back();
            _audio?.Play(new AudioCueRequest("ui.back", "ui"));
            PlayParticleUi(ParticleUiFeedback.Back);
            return;
        }
        if (!_flow.IsMenuOpen) return;
        if (newUp || newDown)
        {
            _flow.Navigate(newUp ? -1 : 1);
            _audio?.Play(new AudioCueRequest("ui.navigate", "ui"));
            PlayParticleUi(ParticleUiFeedback.Navigate);
        }
        else if (newConfirm)
        {
            _flow.ConfirmSelection();
            _audio?.Play(new AudioCueRequest("ui.confirm", "ui"));
            PlayParticleUi(ParticleUiFeedback.Confirm);
        }
    }

    private bool JustPressed(GamePadState current, Buttons button) =>
        current.IsButtonDown(button) && !_previousGamepad.IsButtonDown(button);

    private void DrawTitleBackdrop(SpriteBatch batch, Texture2D pixel)
    {
        int width = GraphicsDevice.Viewport.Width;
        int height = GraphicsDevice.Viewport.Height;
        batch.Draw(pixel, new Rectangle(0, 0, width, height), new Color(18, 23, 35));
        batch.Draw(pixel, new Rectangle(0, height - 84, width, 84), new Color(39, 57, 72));
        batch.Draw(pixel, new Rectangle(0, height - 85, width, 4), new Color(63, 171, 158));
        int left = Math.Max(18, width / 2 - 300);
        GymDebugText.Draw(batch, pixel, left, Math.Max(28, height / 2 - 239),
            "PLATFORMER SANDBOX", new Color(99, 217, 183), 3);
        GymDebugText.Draw(batch, pixel, left, Math.Max(66, height / 2 - 199),
            "SCENE FLOW LAB - TITLE / PLAY / PAUSE", new Color(190, 206, 223), 1);
        if (_ldtkCatalog.RejectedProjects.Count > 0)
            GymDebugText.Draw(batch, pixel, left, Math.Max(85, height / 2 - 175),
                "LDTK VALIDATION: " + _ldtkCatalog.RejectedProjects.Count +
                " PROJECT(S) REJECTED - CHECK CONTENT/LEVELS",
                new Color(255, 168, 122), 1);
    }

    private void DrawFlowMenu(SpriteBatch batch, Texture2D pixel)
    {
        GameUiScreen? screen = _flow.CurrentMenu;
        if (screen is null) return;
        if (screen == GameUiScreen.Graphics)
        {
            batch.Draw(pixel,new Rectangle(0,0,GraphicsDevice.Viewport.Width,GraphicsDevice.Viewport.Height),new Color(6,11,20)*0.78f);
            DrawGraphicsMenu(); return;
        }
        int viewWidth = GraphicsDevice.Viewport.Width;
        int viewHeight = GraphicsDevice.Viewport.Height;
        if (screen != GameUiScreen.Title)
            batch.Draw(pixel, new Rectangle(0, 0, viewWidth, viewHeight),
                new Color(6, 11, 20) * 0.78f);
        var bounds = GameMenuLayout.Get(viewWidth, viewHeight, screen.Value,
            _flow.MenuItems.Count, _flow.SelectionIndex);
        int width = bounds.Width, x = bounds.Left, height = bounds.Height, y = bounds.Top;
        batch.Draw(pixel, new Rectangle(x, y, width, height),
            new Color(20, 30, 44) * 0.98f);
        batch.Draw(pixel, new Rectangle(x, y, 5, height), new Color(82, 202, 169));
        string heading = screen switch
        {
            GameUiScreen.Title => "PLATFORMER SANDBOX",
            GameUiScreen.LevelSelect => "SELECT LEVEL",
            GameUiScreen.Pause => "PAUSED - WORLD TICKS FROZEN",
            GameUiScreen.Settings => "SETTINGS - APPLICATION SCOPE",
            _ => "CONTROLS - SCENE FLOW"
        };
        GymDebugText.Draw(batch, pixel, x + 22, y + 20,
            heading, new Color(103, 214, 183), 2);
        if (screen == GameUiScreen.Controls)
        {
            string[] help =
            [
                "CLICK BUTTONS OR UP DOWN: SELECT",
                "ENTER OR PAD A: CONFIRM",
                "ESC OR PAD B: BACK",
                "P OR PAD START: PAUSE RESUME",
                "TITLE: SELECT LEVEL, THEN CHOOSE SCENE",
                "IN GAME: P PAUSE  T RESTART  R RESPAWN",
                "LDTK: CLICK LAB PANEL OR DEV TOOLS"
            ];
            for (int i = 0; i < help.Length; i++)
                GymDebugText.Draw(batch, pixel, x + 24, y + 57 + 18 * i,
                    help[i], new Color(191, 209, 225), 1);
            batch.Draw(pixel, new Rectangle(x + 18, y + 187, width - 36, 23),
                new Color(42, 84, 91));
            GymDebugText.Draw(batch, pixel, x + 27, y + 192,
                "BACK", new Color(250, 220, 139), 2);
        }
        else
        {
            IReadOnlyList<GameUiMenuItem> entries = _flow.MenuItems;
            int itemY = y + 62;
            const int rowHeight = 30;
            int visibleRows = bounds.Rows;
            int selection = _flow.SelectionIndex;
            int first = bounds.First;
            for (int i = first; i < Math.Min(entries.Count, first + visibleRows); i++)
            {
                int rowY = itemY + (i - first) * rowHeight;
                bool focused = selection == i;
                if (focused)
                    batch.Draw(pixel, new Rectangle(x + 18, rowY - 4,
                        width - 36, 25), new Color(42, 84, 91));
                GymDebugText.Draw(batch, pixel, x + 27, rowY,
                    (focused ? "> " : "  ") + entries[i].Label,
                    focused ? new Color(250, 220, 139) : new Color(189, 207, 223), 2);
            }
            if (screen is GameUiScreen.LevelSelect)
                GymDebugText.Draw(batch, pixel, x + width - 87, y + 26,
                    (selection + 1) + "/" + entries.Count,
                    new Color(164, 187, 207), 1);
        }
        GymDebugText.Draw(batch, pixel, x + 22, y + height - 29,
            screen == GameUiScreen.Title
                ? "UP/DOWN SELECT  ENTER/A CONFIRM  Q QUIT"
                : screen is GameUiScreen.LevelSelect
                ? "UP/DOWN BROWSE  ENTER/A PLAY  ESC/B BACK"
                : "ENTER SELECT  ESC BACK  P RESUME",
            new Color(161, 184, 201), 1);
        if (_flow.LastFailure is string error)
            GymDebugText.Draw(batch, pixel, x + 22, y + height - 13,
                error.ToUpperInvariant(), new Color(245, 155, 124), 1);
    }

    private void DrawTransitionCurtain(SpriteBatch batch, Texture2D pixel)
    {
        SceneTransitionCurtain curtain = _flow.Curtain;
        int width = GraphicsDevice.Viewport.Width;
        int height = GraphicsDevice.Viewport.Height;
        if (curtain.Opacity > 0f)
        {
            batch.Draw(pixel, new Rectangle(0, 0, width, height),
                Color.Black * curtain.Opacity);
            if (curtain.Opacity >= 0.80f)
            {
                string status = curtain.Phase switch
                {
                    SceneCurtainPhase.FadingOut => "LEAVING SCENE",
                    SceneCurtainPhase.Covered => "PREPARING DESTINATION",
                    SceneCurtainPhase.FadingIn when curtain.LastSucceeded == false =>
                        "PREPARATION FAILED - RETURNING TO PREVIOUS SCENE",
                    _ => "ENTERING SCENE"
                };
                GymDebugText.Draw(batch, pixel, Math.Max(10, width / 2 - 270),
                    Math.Max(10, height / 2 - 14), status,
                    new Color(240, 221, 168), 2);
            }
        }
        // A genuine observed prepare duration, not a fabricated percentage.
        // An invalid destination preserves the active scene and shows this
        // notice after the cover is released, including in gameplay (where
        // there is no menu to carry the existing error line).
        if (!curtain.IsActive && _flow.FailureNoticeSecondsRemaining > 0 &&
            _flow.LastFailure is string error)
        {
            batch.Draw(pixel, new Rectangle(18, 36, Math.Max(1, Math.Min(width - 36, 920)), 44),
                new Color(94, 34, 37) * 0.97f);
            GymDebugText.Draw(batch, pixel, 28, 47,
                "LOAD FAILED: " + error.ToUpperInvariant(),
                new Color(255, 200, 176), 1);
            GymDebugText.Draw(batch, pixel, 28, 64,
                "PREVIOUS SCENE RESTORED - RETRY EXIT 1/2 OR USE PAUSE MENU",
                new Color(224, 217, 196), 1);
        }
    }

    private void DrawFlowHint(SpriteBatch batch, Texture2D pixel)
    {
        int x = 18;
        int y = Math.Max(0, GraphicsDevice.Viewport.Height - 22);
        batch.Draw(pixel, new Rectangle(x - 4, y - 4, 590, 20),
            new Color(17, 25, 37) * 0.85f);
        GymDebugText.Draw(batch, pixel, x, y,
            _routeLabMode ? "P PAUSE  R/U RESPAWN  T RESTART  1/2 EXITS  F9 SMOKE" :
                "P PAUSE  ESC MENU  CLICK DEV TOOLS",
            new Color(124, 221, 180), 1);
    }

    private void DrawCompactDebugHud(SpriteBatch batch, Texture2D pixel)
    {
        string scene = _ldtkLevelMode
            ? "LDTK " + (_ldtkLevel?.World.LevelId ?? "UNKNOWN")
            : _routeLabMode ? "ROUTE LAB"
            : _motionLabMode ? "MOTION LAB"
            : _authoredWorldMode ? "AUTHORED WORLD"
            : _worldClockTest is not null ? "SHARED WORLD" : "MOVEMENT GYM";
        string state = _worldClockTest is WorldSession world
            ? "TICK " + world.WorldTick + "  P:" + world.PlayerCount
            : "CRUSH:" + _gym.CrushRespawns;
        string toggle = _ldtkLevelMode ? "CLICK DEV TOOLS" : "TILDE HUD";
        string summary = scene + "  -  " + state + "  -  " + toggle;
        int width = Math.Max(1, Math.Min(GraphicsDevice.Viewport.Width - 24, 590));
        batch.Draw(pixel, new Rectangle(12, 12, width, 25),
            new Color(16, 25, 37) * 0.80f);
        batch.Draw(pixel, new Rectangle(12, 12, 3, 25),
            new Color(108, 224, 178));
        GymDebugText.Draw(batch, pixel, 23, 19, summary,
            new Color(206, 231, 227), 1);
    }

    private void ResetRequestTestBaseline()
    {
        _gym.Reset();
        _lastResolution = null;
        _lastKeyboardDirection = HorizontalDirection.Neutral;
        _titleRefreshCounter = 6;
    }

    private void DrawCharacterStateHud(SpriteBatch batch, Texture2D pixel, WorldSession world)
    {
        int x = Math.Max(12, GraphicsDevice.Viewport.Width - 365);
        int y = 309;
        batch.Draw(pixel, new Rectangle(x, y, 350, 169),
            new Color(17, 25, 37) * 0.96f);
        batch.Draw(pixel, new Rectangle(x, y, 4, 169), new Color(237, 174, 109));
        GymDebugText.Draw(batch, pixel, x + 12, y + 8,
            "A09 CHARACTER STATES", new Color(237, 174, 109), 2);
        GymDebugText.Draw(batch, pixel, x + 12, y + 30,
            "Z P1 ACTION   X P1 STUN  C CLEAR", new Color(196, 217, 235), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 45,
            "O P2 STUN (90 TICKS)", new Color(196, 217, 235), 1);
        int row = 65;
        foreach (WorldPlayerSnapshot player in world.Snapshot().Players)
        {
            CharacterStateSnapshot state = player.State;
            GymDebugText.Draw(batch, pixel, x + 12, y + row,
                "P" + player.PlayerId + " " + state.Locomotion + " / " + state.Action,
                player.PlayerId == 1 ? new Color(68, 214, 177) :
                    player.PlayerId == 2 ? new Color(161, 145, 246) :
                    new Color(242, 220, 93), 1);
            GymDebugText.Draw(batch, pixel, x + 12, y + row + 14,
                "   " + state.Condition + "  STUN LEFT: " + state.StunTicksRemaining,
                new Color(196, 217, 235), 1);
            row += 29;
        }
        GymDebugText.Draw(batch, pixel, x + 12, y + 153,
            _latestStateFeedback.ToUpperInvariant(), new Color(237, 174, 109), 1);
    }

    private void DrawAbilityHud(SpriteBatch batch, Texture2D pixel, WorldSession world)
    {
        int x = Math.Max(12, GraphicsDevice.Viewport.Width - 365);
        int y = 485;
        batch.Draw(pixel, new Rectangle(x, y, 350, 195),
            new Color(17, 25, 37) * 0.96f);
        batch.Draw(pixel, new Rectangle(x, y, 4, 195), new Color(110, 192, 242));
        GymDebugText.Draw(batch, pixel, x + 12, y + 8,
            "A10 ABILITY COORDINATOR", new Color(110, 192, 242), 2);
        GymDebugText.Draw(batch, pixel, x + 12, y + 30,
            "B P1 PULSE   G P1 FOCUS   Y P2 PULSE", new Color(196, 217, 235), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 45,
            "H CANCEL PULSE   K CANCEL FOCUS", new Color(196, 217, 235), 1);
        int line = 64;
        foreach (WorldPlayerSnapshot player in world.Snapshot().Players)
        {
            if (line > 144) break;
            CharacterAbilitySnapshot abilities = world.GetPlayerAbilities(player.PlayerId);
            GymDebugText.Draw(batch, pixel, x + 12, y + line,
                "P" + player.PlayerId + " ENERGY " + abilities.Energy + "/100",
                player.PlayerId == 1 ? new Color(68, 214, 177) :
                    player.PlayerId == 2 ? new Color(161, 145, 246) :
                    new Color(242, 220, 93), 1);
            GymDebugText.Draw(batch, pixel, x + 12, y + line + 14,
                " PULSE A" + abilities.Pulse.ActiveTicksRemaining +
                " C" + abilities.Pulse.CooldownTicksRemaining +
                "  FOCUS A" + abilities.Focus.ActiveTicksRemaining +
                " C" + abilities.Focus.CooldownTicksRemaining,
                new Color(196, 217, 235), 1);
            line += 29;
        }
        GymDebugText.Draw(batch, pixel, x + 12, y + 166,
            _latestAbilityFeedback.ToUpperInvariant(), new Color(110, 192, 242), 1);
    }

    private void DrawMotionLabHud(SpriteBatch batch, Texture2D pixel,
        WorldSession world)
    {
        int x = 18;
        int y = 468;
        batch.Draw(pixel, new Rectangle(x, y, 570, 185),
            new Color(17, 25, 37) * 0.96f);
        batch.Draw(pixel, new Rectangle(x, y, 4, 185), new Color(245, 190, 99));
        GymDebugText.Draw(batch, pixel, x + 12, y + 8,
            "MOTION LAB  F1 EXIT  F2 AUTHORED", new Color(245, 190, 99), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 30,
            "Q WIND: " + (_windSelection == 1 ? "LEFT 75%" :
                _windSelection == 2 ? "RIGHT 75%" : "OFF"),
            new Color(196, 217, 235), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 47,
            "E HIT OPPOSITE FACING   B PULSE DASH   X STUN",
            new Color(196, 217, 235), 1);
        WorldPlayerSnapshot p = world.GetPlayer(1);
        WorldMotionSnapshot motion = world.GetPlayerMotion(1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 68,
            "PLAYER X VELOCITY: " + p.Velocity.X.ToString("F2"),
            new Color(68, 214, 177), 2);
        GymDebugText.Draw(batch, pixel, x + 12, y + 95,
            "WIND: " + motion.EnvironmentVelocityX.ToString("F2") +
            "  IMPULSE: " + motion.ImpulseVelocityX.ToString("F2"),
            new Color(196, 217, 235), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 113,
            "DASH: " + motion.DashVelocityX.ToString("F2") +
            "  EXTERNAL: " + motion.CombinedExternalVelocityX.ToString("F2"),
            new Color(196, 217, 235), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 132,
            "FACING: " + (motion.FacingDirection > 0 ? "RIGHT" : "LEFT") +
            "  WALK MAX: " + MotionContributionGym.NormalWalkingSpeed.ToString("F2"),
            new Color(196, 217, 235), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 153,
            "WALK WITH/AGAINST WIND, JUMP, DASH INTO WALL",
            new Color(245, 190, 99), 1);
    }

    private void DrawWorldEventHud(SpriteBatch batch, Texture2D pixel, WorldSession world)
    {
        int x = 18;
        int y = _authoredWorldMode ? 271 : 230;
        int width = 570;
        batch.Draw(pixel, new Rectangle(x, y, width, 189),
            new Color(17, 25, 37) * 0.96f);
        batch.Draw(pixel, new Rectangle(x, y, 4, 189),
            new Color(238, 190, 105));
        GymDebugText.Draw(batch, pixel, x + 12, y + 9,
            "WORLD EVENTS  F3 CLEAR HISTORY", new Color(238, 190, 105), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 26,
            "RUN: " + world.WorldInstanceId + "  TICK: " + world.WorldTick +
            "  LOG: " + _recentWorldEvents.Count,
            new Color(187, 206, 222), 1);
        if (_recentWorldEvents.Count == 0)
            GymDebugText.Draw(batch, pixel, x + 12, y + 49,
                "NO COMMITTED EVENTS YET", new Color(187, 206, 222), 1);
        for (int i = 0; i < _recentWorldEvents.Count; i++)
        {
            WorldGameplayEvent e = _recentWorldEvents[_recentWorldEvents.Count - 1 - i];
            string source = e.TriggerId is int trigger
                ? "ZONE:" + trigger
                : e.ColliderId is int collider
                    ? "COL:" + collider : "";
            string line = "T" + e.WorldTick + " S" + e.Sequence +
                " P" + e.PlayerId + " " + e.Kind + " " + source;
            GymDebugText.Draw(batch, pixel, x + 12, y + 49 + i * 16,
                line, i == 0 ? new Color(124, 224, 179) :
                    new Color(187, 206, 222), 1);
        }
    }

    private void DrawRouteLabHud(SpriteBatch batch, Texture2D pixel, WorldSession world)
    {
        int x = 18;
        int y = Math.Max(427, GraphicsDevice.Viewport.Height - 190);
        batch.Draw(pixel, new Rectangle(x, y, 715, 155),
            new Color(17, 25, 37) * 0.96f);
        batch.Draw(pixel, new Rectangle(x, y, 4, 155), new Color(115, 220, 169));
        GymDebugText.Draw(batch, pixel, x + 12, y + 8,
            "SM PHASE 6  ROUTE / SESSION / SCENE VALIDATION",
            new Color(115, 220, 169), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 25,
            "SESSION: " + _gameplaySession?.SessionId +
            "  ENTRIES: " + _gameplaySession?.LevelEntryCount +
            "  WORLD ID: " + world.WorldInstanceId +
            "  TICK: " + world.WorldTick, new Color(205, 221, 237), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 42,
            "LEVEL: " + _flow.Scenes.ActiveDestination?.LevelId +
            "  ENTRANCE: " + _flow.Scenes.ActiveDestination?.EntranceId,
            new Color(240, 207, 128), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 59,
            "P1: TOUCH GATES  1/2 EXIT  F10 FAIL NEXT LOAD  F9 AUTO TEST",
            new Color(205, 221, 237), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 76,
            "R/U RESPAWN  T RESTART  F10 FAILURE THEN 1/2 RETRY",
            new Color(205, 221, 237), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 91,
            _lastRouteStatus + (_flow.LastPreparationMilliseconds is double ms
                ? "  PREP+COMMIT " + ms.ToString("F2") + " MS" : ""),
            new Color(161, 217, 204), 1);
        SceneFlowValidationMetrics metrics = _flow.Validation;
        GymDebugText.Draw(batch, pixel, x + 12, y + 108,
            "FLOW: QUEUED " + metrics.Queued + " OK " + metrics.SuccessfulCommits +
            " FAIL " + metrics.FailedPreparations + " BUSY " + metrics.Busy +
            " DUP " + metrics.Duplicates + " INVALID " + metrics.Invalid,
            new Color(194, 208, 232), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 124,
            "SCENES: LIVE " + _flow.Scenes.LiveSceneInstances +
            " PEAK " + _flow.Scenes.PeakLiveSceneInstances +
            "  PREP MS: AVG " + metrics.MeanPreparationMilliseconds.ToString("F2") +
            " MAX " + metrics.MaximumPreparationMilliseconds.ToString("F2"),
            new Color(194, 208, 232), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 140,
            "F9 SMOKE (ESC CANCEL): " + _sceneFlowSmoke.Status,
            _sceneFlowSmoke.HasRun
                ? (_sceneFlowSmoke.Passed ? new Color(105, 231, 162) :
                    _sceneFlowSmoke.IsRunning ? new Color(241, 209, 126) :
                    new Color(244, 141, 127)) : new Color(161, 217, 204), 1);
    }

    private SceneFlowProbeSnapshot? CurrentRouteProbe()
    {
        if (!_routeLabMode || _worldClockTest is not WorldSession world ||
            _gameplaySession is not GameplaySession session ||
            _flow.Scenes.ActiveDestination is not SceneDestination destination)
            return null;
        return new SceneFlowProbeSnapshot(destination.LevelId, destination.EntranceId,
            session.SessionId, session.LevelEntryCount,
            world.WorldInstanceId, world.WorldTick);
    }

    private void DrawRequestTestHud(SpriteBatch batch, Texture2D pixel)
    {
        if (!_requestTestEnabled)
            return;

        // Top-right panel keeps the collision/crush and fault diagnostics at
        // top-left readable. This is HUD-only and does not alter gameplay.
        int x = Math.Max(12, GraphicsDevice.Viewport.Width - 365);
        int y = 42;
        batch.Draw(pixel, new Rectangle(x, y, 350, 144),
            new Color(17, 25, 37) * 0.95f);
        batch.Draw(pixel, new Rectangle(x, y, 4, 144),
            new Color(125, 181, 247));
        GymDebugText.Draw(batch, pixel, x + 12, y + 9,
            "REQUEST TEST: ON", new Color(125, 181, 247), 2);
        GymDebugText.Draw(batch, pixel, x + 12, y + 30,
            "F10 EXIT  F11 CASE  F12 ORDER", new Color(185, 204, 229), 1);

        string scenario = _requestScenario switch
        {
            MovementRequestGymScenario.ControlLock => "LOCK NEUTRAL",
            MovementRequestGymScenario.ForceLeft => "FORCE LEFT",
            MovementRequestGymScenario.ForceRight => "FORCE RIGHT",
            MovementRequestGymScenario.EqualPriorityTie => "TIE ID30 RIGHT",
            MovementRequestGymScenario.KeyboardOnly => "KEYBOARD ONLY",
            _ => "UNKNOWN"
        };
        GymDebugText.Draw(batch, pixel, x + 12, y + 48,
            "CASE: " + scenario, new Color(243, 196, 103), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 63,
            "INPUT: " + _lastKeyboardDirection, new Color(187, 206, 222), 1);
        if (_lastResolution is MovementResolution result)
        {
            GymDebugText.Draw(batch, pixel, x + 12, y + 78,
                "RESOLVED: " + result.Intent.Direction,
                new Color(74, 218, 165), 1);
            GymDebugText.Draw(batch, pixel, x + 12, y + 93,
                "WIN ID: " + (result.WinningSourceId?.ToString() ?? "NONE") +
                "  PRIORITY: " + (result.WinningPriority?.ToString() ?? "NONE"),
                new Color(187, 206, 222), 1);
            GymDebugText.Draw(batch, pixel, x + 12, y + 108,
                "JUMP PRESS: " + (result.Intent.JumpPressed ? "YES" : "NO") +
                "  RELEASE: " + (result.Intent.JumpReleased ? "YES" : "NO"),
                new Color(187, 206, 222), 1);
        }
        GymDebugText.Draw(batch, pixel, x + 12, y + 123,
            "ORDER: " + (_reverseRequestOrder ? "REVERSE" : "NORMAL"),
            new Color(187, 206, 222), 1);
    }

    private void DrawDeviceInputHud(SpriteBatch batch, Texture2D pixel)
    {
        int x = Math.Max(12, GraphicsDevice.Viewport.Width - 365);
        int y = _requestTestEnabled ? 196 : 42;
        batch.Draw(pixel, new Rectangle(x, y, 350, 99),
            new Color(17, 25, 37) * 0.95f);
        batch.Draw(pixel, new Rectangle(x, y, 4, 99),
            new Color(118, 207, 188));
        GymDebugText.Draw(batch, pixel, x + 12, y + 8,
            "A11 INPUT TEST  F8 SOURCE", new Color(118, 207, 188), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 27,
            "SOURCE: " + (_inputRouter.Source == LocalInputSource.Keyboard
                ? "KEYBOARD" : "GAMEPAD 1"), new Color(187, 206, 222), 1);
        GymDebugText.Draw(batch, pixel, x + 12, y + 42,
            "GAMEPAD: " + (_gamepadOneConnected ? "CONNECTED" : "DISCONNECTED"),
            new Color(187, 206, 222), 1);
        if (_lastPlayerInput is LocalPlayerInput command)
        {
            GymDebugText.Draw(batch, pixel, x + 12, y + 57,
                "MOVE: " + command.Direction + "  JUMP HELD: " +
                (command.JumpHeld ? "YES" : "NO"),
                new Color(187, 206, 222), 1);
            GymDebugText.Draw(batch, pixel, x + 12, y + 72,
                "PRESS: " + (command.JumpPressed ? "YES" : "NO") +
                "  RELEASE: " + (command.JumpReleased ? "YES" : "NO"),
                new Color(187, 206, 222), 1);
        }
        else
        {
            GymDebugText.Draw(batch, pixel, x + 12, y + 57,
                "NO COMPLETED INPUT STEP", new Color(187, 206, 222), 1);
        }
    }

    private bool JustPressed(KeyboardState keys, Keys key) =>
        keys.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);

    private void ApplyPreset(MovementGymTuning tuning, string label)
    {
        _gym.ApplyTuning(tuning);
        _tuningLabel = label;
        _titleRefreshCounter = 6;
    }

    private void AdjustParameter(int step)
    {
        if (_gym.Tuning.TryAdjust(_selectedParameter, step, out MovementGymTuning next))
        {
            _gym.ApplyTuning(next);
            _tuningLabel = "Custom";
            _titleRefreshCounter = 6;
        }
    }

    private void UpdateWindowTitle()
    {
        if (_flow.IsTransitionActive)
        {
            Window.Title = "PlatformerSandbox | TRANSITION " +
                _flow.Curtain.Phase +
                " | World paused | Preparing at safe update boundary";
            return;
        }
        if (_flow.ActiveScene == MajorSceneKind.Title)
        {
            Window.Title = "PlatformerSandbox | TITLE | Select Level (Enter) | " +
                "Click menu buttons or use Up/Down | Esc Back";
            return;
        }
        if (_flow.IsMenuOpen)
        {
            Window.Title = "PlatformerSandbox | " + _flow.CurrentMenu +
                " | Simulation frozen | P/Start Resume | Esc/B Back";
            return;
        }
        if (IsLightingBasicsScene)
        {
            Window.Title = "PlatformerSandbox | LIGHTING BASICS | " +
                "Click inspector for controls | Esc Pause";
            return;
        }
        if (_worldClockTest is WorldSession world)
        {
            // LDtk-authored levels may have only one registered player.
            // Do not assume every WorldSession is the two-player test gym.
            if (_ldtkLevelMode)
            {
                Window.Title =
                    $"PlatformerSandbox | LDTK {_flow.Scenes.ActiveDestination?.LevelId ?? _ldtkLevel?.World.LevelId} | " +
                    $"tick={world.WorldTick} players={world.PlayerCount} " +
                    $"colliders={_ldtkLevel?.ColliderCount} " +
                    $"visualTiles={_ldtkVisuals?.Definition.TileCount ?? 0} | " +
                    (InteractiveLab ? "A/D Space R Respawn Esc Menu | Lab panel" : "A/D Space R Respawn T Reload P Pause | Click DEV TOOLS");
                return;
            }
            if (_routeLabMode)
            {
                Window.Title =
                    $"PlatformerSandbox | ROUTES {_flow.Scenes.ActiveDestination?.LevelId}/" +
                    $"{_flow.Scenes.ActiveDestination?.EntranceId} session={_gameplaySession?.SessionId} " +
                    $"entry={_gameplaySession?.LevelEntryCount} tick={world.WorldTick} " +
                    "P1 A/D Space P2 J/L I | R/U Respawn T Restart 1/2 Exits F9 SM Smoke";
                return;
            }
            if (_motionLabMode)
            {
                Window.Title =
                    $"PlatformerSandbox | MOTION LAB | tick={world.WorldTick} " +
                    $"wind={world.GetPlayerMotion(1).EnvironmentVelocityX:F2} | " +
                    "F1 EXIT Q WIND E KNOCKBACK B PULSE DASH R RESPAWN";
                return;
            }
            if (_authoredWorldMode)
            {
                Window.Title =
                    $"PlatformerSandbox | AUTHORED WORLD {world.Definition?.LevelId} | " +
                    $"tick={world.WorldTick} players={world.PlayerCount} | " +
                    $"P1 spawn={world.GetPlayerSlot(1).SpawnId} " +
                    $"P1 source={world.GetPlayerSlot(1).InputSourceId} | " +
                    "F4 Exit F2 Mode R/U Respawn T Restart N Join M Leave V Spawn " +
                    "B/G/H/K P1 Abilities Y P2 Pulse F3 Events";
                return;
            }
            // Only the dedicated shared-world fixture requires both players.
            var p1 = world.GetPlayer(SharedWorldClockGym.FirstPlayerId);
            var p2 = world.GetPlayer(SharedWorldClockGym.SecondPlayerId);
            Window.Title =
                $"PlatformerSandbox | SHARED WORLD TEST | tick={world.WorldTick} " +
                $"time={world.WorldTimeSeconds:F3} | " +
                $"P1 x={p1.Bounds.Left:F2} support={p1.MovingSupportId} " +
                $"P2 x={p2.Bounds.Left:F2} support={p2.MovingSupportId} | " +
                "F4 Exit  R P1 Respawn  U P2 Respawn  T Restart World " +
                "J/L P2 Move I P2 Jump P Pause F8 Device " +
                "B/G/H/K P1 Abilities Y P2 Pulse";
            return;
        }
        var v = _gym.PlayerVelocity;
        var p = _gym.PlayerBounds;
        string mode = _paused ? "PAUSED" :
            _gym.IsJumping ? (_gym.IsJumpEarlyReleased ? "JUMP-CUT" : "JUMP") :
            _gym.IsRidingMovingPlatform ? "MOVING-FLOOR" :
            _gym.IsGrounded ? "GROUND" : "FALL";
        MovementGymDiagnostics diagnostics = _gym.Diagnostics;
        double selectedValue = _gym.Tuning.Get(_selectedParameter);
        Window.Title =
            $"PlatformerSandbox | {_tuningLabel} | {mode} " +
            $"| x={p.Left:F2} y={p.Bottom:F2} vx={v.X:F2} vy={v.Y:F2} " +
            $"| free-stop={diagnostics.FreeStoppingDistanceUnits:F2}u " +
            $"({diagnostics.FreeStoppingTimeSeconds:F2}s) " +
            $"| {_selectedParameter}={selectedValue:F3} " +
            $"| Platforms={_gym.PlatformMode} " +
            $"| Fault={_gym.FaultSensitivity} Recoveries={_gym.LastStepRecoveryCount} " +
            $"Fallbacks={_gym.CheckpointFallbackCount} Crushes={_gym.CrushRespawns} " +
            $"CollisionAssist={(_gym.AssistancePolicy.AnyEnabled ? "ON" : "OFF")} " +
            $"AssistCount={_gym.TotalAssists} " +
            "| F1/F2/F3: Presets  F5/F6/F7: Fault Mode  Tab: Setting  -/+: Adjust  " +
            "F4: Shared World  F8: Input Source  F9: Assist  F10: Request Test  F11: Case  F12: Reverse Order  " +
            $"Input={_inputRouter.Source} PadConnected={_gamepadOneConnected} " +
            $"RequestTest={(_requestTestEnabled ? _requestScenario.ToString() : "OFF")}" +
            "  A/D: Move  Space: Jump  R: Reset  P: Pause";
    }
}
