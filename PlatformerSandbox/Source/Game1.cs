using System;
using System.IO;
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
public sealed class Game1 : Game
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
    private LightingGameCatalog? _lightingCatalog;
    private GameplayLightingPresenter? _lightingEvents;
    private LdtkLightingScene? _ldtkLighting;
    private string? _lightingWarning;
    private string _lightingPresetId = "Illustrated";
    private double _lightingPresetNoticeSeconds;
    private LightingQuality _lightingQuality = LightingQuality.Balanced;
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
            PreferredBackBufferWidth = 1280,
            PreferredBackBufferHeight = 720,
            SynchronizeWithVerticalRetrace = true
        };
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
                    Path.Combine(AppContext.BaseDirectory, "Content", "Particles"));
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
                    Content.Load<Effect>("Effects/L_BASIC_SurfaceLighting"));
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
                Id = "stress_" + i, Owner = "stress",
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

    private Color ShadeGameplaySurface(string iid, IReadOnlyList<RadialLight> lights,
        LightingArtPreset art, LdtkLightingScene? scene)
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
                options.ReceivesShadows, iid, art, lights, scene.Occluders, layer)
            : null;
        LightRgb sampled = options.LightingMode is LightingSurfaceMode.Silhouette or
            LightingSurfaceMode.Unlit
            ? LightRgb.White
            : LightingWorldSampler.Sample(point, options.Channel,
                options.ReceivesShadows, iid, art, lights, scene.Occluders, layer);
        return GameplayLightingRenderer.ToColor(
            LightingSurfaceShading.ResolveTint(options, layer, sampled, reveal));
    }

    private Color ShadeGameplayHero(LightPoint point, IReadOnlyList<RadialLight> lights,
        LightingArtPreset art, LdtkLightingScene? scene)
    {
        if (scene is null) return Color.White;
        SceneLightingLayer playerLayer = scene.Layers.Gameplay;
        if (playerLayer.Mode == RenderLayerLightingMode.Unlit) return Color.White;
        LightRgb rgb = LightingWorldSampler.Sample(point, LightChannel.Hero,
            playerLayer.ReceivesShadows, null, art, lights, scene.Occluders, playerLayer);
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
            HandleSceneNavigation(labKeys, labPad, uiClicked, uiPoint);
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
        if (_audio is not null)
        {
            UpdateAudioListener();
            _audio.Update(gameTime.ElapsedGameTime.TotalSeconds);
        }
        _music?.Update(gameTime.ElapsedGameTime.TotalSeconds);
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
        if (_ldtkLevelMode && _flow.ActiveScene == MajorSceneKind.Gameplay &&
            !_flow.IsMenuOpen && !_flow.IsTransitionActive)
        {
            LightingToolAction action = _lightingTools.Hit(GraphicsDevice.Viewport.Width,
                GraphicsDevice.Viewport.Height, uiPoint.X, uiPoint.Y, uiClicked);
            if (action != LightingToolAction.None)
            {
                ApplyLightingToolAction(action);
                toolClicked = true;
            }
        }
        if (!cancelledSmoke && !_sceneFlowSmoke.IsRunning)
            HandleSceneNavigation(keys, gamepadState, uiClicked && !toolClicked, uiPoint);
        if (_flow.IsMenuOpen || _flow.ActiveScene != MajorSceneKind.Gameplay)
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
        _diagnostics?.CaptureScene(_flow, _worldClockTest, _gym);
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
            _lightingTools.Close();
            _lastLightingView = null;
            _lastSelectedLights = Array.Empty<RadialLight>();
            ActivateParticleWorld();
            ActivateLightingWorld();
            // Construct after the scene has committed. The lab shares the
            // normal menu, but its GPU resources are presentation-owned.
            if (IsLightingBasicsScene)
                _lightingBasicsLab ??= new BasicLightingLab(GraphicsDevice, Content);
        }
        // UI cosmetic clock continues during menus/covered scene transitions.
        // World cosmetics freeze whenever the authoritative world is suspended.
        _particleVfx?.Presenter.Update(gameTime.ElapsedGameTime.TotalSeconds, _paused);
        _lightingEvents?.Update(gameTime.ElapsedGameTime.TotalSeconds, _paused);
        // Use committed player location for cosmetic culling; no world mutation.
        if (_particleVfx is not null && _worldClockTest is WorldSession particleWorld &&
            particleWorld.HasPlayer(1))
            _particleVfx.UpdateEnvironment(particleWorld.GetPlayer(1).Bounds.Center,
                gameTime.ElapsedGameTime.TotalSeconds, _paused);
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
                _previousKeyboard = keys;
                _previousGamepad = gamepadState;
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
        if (unmodifiedLevelDigit && _ldtkConnectedLevel is LdtkConnectedLevel authoredLevel &&
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
            _previousKeyboard = keys;
            _previousGamepad = gamepadState;
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
                if (_authoredWorldMode || _motionLabMode || _routeLabMode || _ldtkLevelMode)
                    world.RespawnPlayer(1);
                else
                    world.RespawnPlayer(SharedWorldClockGym.FirstPlayerId,
                        SharedWorldClockGym.SpawnBounds(world, SharedWorldClockGym.FirstPlayerId));
                _worldP1Respawns++;
            }
            else
                _gym.Reset();
        }
        if (_worldClockTest is WorldSession testWorld &&
            testWorld.HasPlayer(2) && JustPressed(keys, Keys.U))
        {
            if (_authoredWorldMode || _motionLabMode || _routeLabMode)
                testWorld.RespawnPlayer(2);
            else
                testWorld.RespawnPlayer(SharedWorldClockGym.SecondPlayerId,
                    SharedWorldClockGym.SpawnBounds(testWorld, SharedWorldClockGym.SecondPlayerId));
            _worldP2Respawns++;
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
                if (JustPressed(keys, Keys.B))
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
                _diagnostics?.BeginWorldStep(_flow, clockWorld, _gym.FaultSensitivity,
                    _worldCommands, _characterStateRequests, _characterAbilityRequests,
                    _motionRequests);
                WorldStepResult committed = clockWorld.Step(_worldCommands,
                    _characterStateRequests, _characterAbilityRequests,
                    _motionRequests);
                _diagnostics?.CompleteWorldStep(_flow, clockWorld, _gym.FaultSensitivity, committed);
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
                _particleVfx?.Presenter.PresentCommitted(committed);
                foreach (WorldGameplayEvent gameplayEvent in committed.Events)
                {
                    // A01: only post-commit world events create game audio.
                    // This never calls from WorldSession.Step() or a solver.
                    _audioEvents?.Present(gameplayEvent);
                    _lightingEvents?.Present(gameplayEvent);
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
            }
            else
            {
                _diagnostics?.BeginGymStep(_flow, _gym, input);
                _gym.Step(MovementGymSession.FixedStepSeconds, _inputRequests,
                    out MovementResolution committedResolution);
                _diagnostics?.CompleteGymStep(_flow, _gym);
                _lastResolution = committedResolution;
            }
            _lastPlayerInput = input;
            _lastKeyboardDirection = input.Direction;
        }

        _previousKeyboard = keys;
        _previousGamepad = gamepadState;
        if (++_titleRefreshCounter >= 6)
        {
            _titleRefreshCounter = 0;
            UpdateWindowTitle();
        }
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
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
            bool lightingWorldActive = _gameLighting is not null &&
                _lightingCatalog is not null && _lightingEvents?.Scene is not null &&
                _lightingEnabled && _ldtkLevelMode && _ldtkLevel is not null &&
                _worldClockTest is not null && _flow.ActiveScene == MajorSceneKind.Gameplay;
            // L02 captures each authored render depth plane individually, so
            // the active layer (not the whole scene) is the light receiver.
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            bool showFullHud = _debugHudMode == GameDebugHudMode.Full;
            if (_flow.ActiveScene == MajorSceneKind.Title)
                DrawTitleBackdrop(_spriteBatch, _pixel);
            else
            {
                if (_worldClockTest is WorldSession clockWorld)
                {
                    if (_ldtkLevelMode && _ldtkLevel is LdtkImportedLevel imported)
                    {
                        Camera2DView camera = GetLdtkCameraView(imported, clockWorld,
                            _paused ? 0 : gameTime.ElapsedGameTime.TotalSeconds);
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
                        Action<ParticleVisualLayer>? layerHook = _particleVfx is null
                            ? null : layer => _particleVfx.DrawLayer(_spriteBatch, particleView, layer);
                        LightingArtPreset? art = lightingWorldActive
                            ? _lightingCatalog!.GetPreset(_lightingPresetId) : null;
                        IReadOnlyList<RadialLight> lights = lightingWorldActive
                            ? _lightingEvents!.BuildVisible(
                                GameplayLightingRenderer.View(camera), EnumerateParticleLights(),
                                LightingStressCandidates(camera))
                            : Array.Empty<RadialLight>();
                        if (lightingWorldActive)
                        {
                            _lastLightingView = GameplayLightingRenderer.View(camera);
                            _lastSelectedLights = lights;
                        }
                        // Artwork keeps animating when lighting is disabled through DEV TOOLS.
                        LdtkLightingScene? frameLighting = _lightingEvents?.CurrentScene();
                        if (lightingWorldActive)
                        {
                            LightingFrameBudget shadowBudget = LightingFrameBudget.For(_lightingQuality);
                            ShadowEdgePreset effectiveShadowPreset =
                                _shadowQualityPolicy == ShadowQualityPolicy.FollowLightingQuality
                                    ? ShadowContactControls.QualityPreset(_lightingQuality)
                                    : _shadowEdgePreset;
                            _gameLighting!.ShadowSoftnessPixels =
                                ShadowEdgeSettings.EffectiveWidth(effectiveShadowPreset, shadowBudget);
                            _gameLighting.FilterMode = _shadowFilterMode;
                            _gameLighting.ShadowEmitterOverrides = _shadowEmitterOverrides;
                            _gameLighting.BeginLayeredFrame(lights, shadowBudget);
                        }
                        LdtkLevelRenderer.Draw(_spriteBatch, _pixel,
                            GraphicsDevice.Viewport, clockWorld, imported, _paused,
                            _ldtkVisuals, _ldtkShowCollisionDebug, showFullHud,
                            _ldtkDrawCollisionBackdrop, _ldtkConnectedLevel,
                            camera, _ldtkCharacterAnimations, _ldtkCharacterSprites,
                            layerHook,
                            null,
                            lightingWorldActive ? frameLighting : null,
                            lightingWorldActive ? id => ShadeGameplaySurface(id, lights, art!, frameLighting) : null,
                            lightingWorldActive ? point => ShadeGameplayHero(point, lights, art!, frameLighting) : null,
                            lightingWorldActive ? _gameLighting!.ContactShadowTexture : null,
                            lightingWorldActive ? () => _gameLighting!.BeginLayer() : null,
                            lightingWorldActive ? (view, layer) => _gameLighting!.ComposeLayer(
                                _spriteBatch, view, lights, frameLighting!.Occluders,
                                art!, LightingFrameBudget.For(_lightingQuality), layer) : null,
                            lightingWorldActive ? () => _gameLighting!.EndLayeredFrame(_spriteBatch) : null,
                            lightingWorldActive ? (submission, region, albedo, normals, edges) =>
                                _gameLighting!.DrawProductionMaterialSurface(_spriteBatch,
                                    GameplayLightingRenderer.View(camera), submission, region, lights,
                                    frameLighting!.Occluders, art!,
                                    frameLighting.Layers.Get(submission.LayerId),
                                    albedo, normals, edges) : null,
                            frameLighting);
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
                else if (_debugHudMode == GameDebugHudMode.Compact)
                    DrawCompactDebugHud(_spriteBatch, _pixel);
            }
            if (_ldtkLevelMode && _flow.ActiveScene == MajorSceneKind.Gameplay &&
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
                    _lightingEnabled, _lightingPresetId, _lightingQuality.ToString(),
                    ShadowEdgeSettings.Label(effectiveEdge),
                    _shadowFilterMode == ShadowFilterMode.ContactHardening ? "CONTACT" : "UNIFORM",
                    _shadowQualityPolicy == ShadowQualityPolicy.FollowLightingQuality ? "AUTO" : "MANUAL",
                    editLightName.ToUpperInvariant(), emitterPx.ToString("F0") + "PX",
                    _reducedLightingFlashes, _lightingStress,
                    _lightingEvents?.WallMotionEnabled ?? false, _lightingInspectionLight,
                    _debugHudMode.ToString(), _ldtkShowCollisionDebug,
                    _ldtkDrawCollisionBackdrop, _audio?.IsMuted ?? false,
                    _audio is null ? 0 : (int)Math.Round(_audio.Mixer.MasterVolume * 100),
                    lightStatuses);
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
            if (_particleVfx is not null && _debugHudMode == GameDebugHudMode.Full)
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
                _ldtkLevelMode && _debugHudMode == GameDebugHudMode.Full)
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
                    " P95 " + cpu.P95.ToString("F2") +
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
            if (_diagnostics is not null)
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
            _ldtkCamera = new Camera2D();
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
        _ldtkConnectedLevel = connectedLdtk;
        _ldtkParticleZones = particleZones;
        _ldtkLighting = lighting;
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

    private void ApplyLightingToolAction(LightingToolAction action)
    {
        switch (action)
        {
            case LightingToolAction.ToggleLights:
                _lightingEnabled = !_lightingEnabled;
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
                "LDTK: CLICK DEV TOOLS FOR LIGHT AND HUD"
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
            if (screen == GameUiScreen.LevelSelect)
                GymDebugText.Draw(batch, pixel, x + width - 87, y + 26,
                    (selection + 1) + "/" + entries.Count,
                    new Color(164, 187, 207), 1);
        }
        GymDebugText.Draw(batch, pixel, x + 22, y + height - 29,
            screen == GameUiScreen.Title
                ? "UP/DOWN SELECT  ENTER/A CONFIRM  Q QUIT"
                : screen == GameUiScreen.LevelSelect
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
                    $"PlatformerSandbox | LDTK {_ldtkLevel?.World.LevelId} | " +
                    $"tick={world.WorldTick} players={world.PlayerCount} " +
                    $"colliders={_ldtkLevel?.ColliderCount} " +
                    $"visualTiles={_ldtkVisuals?.Definition.TileCount ?? 0} | " +
                    "A/D Space  R Respawn T Reload P Pause | Click DEV TOOLS";
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
