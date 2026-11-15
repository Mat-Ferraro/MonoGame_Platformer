using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.Input.Requests;
using PlatformerSandbox.Gameplay.Input.Local;
using PlatformerSandbox.Input;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Diagnostics;
using PlatformerSandbox.Simulation.CharacterMotor;
using PlatformerSandbox.Gyms;

namespace PlatformerSandbox;

/// <summary>
/// Thin MonoGame host for the first playable movement test gym.
/// Device polling and presentation live here; the Gameplay and Simulation
/// projects do not reference MonoGame or own any graphics state.
/// </summary>
public sealed class Game1 : Game
{
    private readonly GraphicsDeviceManager _graphics;
    // Gameplay assistance starts enabled in the *playable* gym. Unit-test
    // fixtures may still explicitly select the unassisted baseline.
    private readonly MovementGymSession _gym = new(
        assistancePolicy: MovementGymSession.PlayableAssistancePreset);
    private readonly List<MovementRequest> _inputRequests = new(5);
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

    public Game1()
    {
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
    }

    protected override void Initialize()
    {
        _previousKeyboard = Keyboard.GetState();
        _inputRouter.SelectSource(LocalInputSource.Keyboard,
            _inputBindings.CaptureKeyboard(_previousKeyboard));
        UpdateWindowTitle();
        base.Initialize();
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _pixel = new Texture2D(GraphicsDevice, 1, 1);
        _pixel.SetData(new[] { Color.White });
    }

    protected override void Update(GameTime gameTime)
    {
        KeyboardState keys = Keyboard.GetState();
        GamePadState gamepadState = GamePad.GetState(PlayerIndex.One);
        LocalDeviceInput keyboard = _inputBindings.CaptureKeyboard(keys);
        LocalDeviceInput gamepad = _inputBindings.CaptureGamepad(gamepadState);
        _gamepadOneConnected = gamepad.IsConnected;
        if (keys.IsKeyDown(Keys.Escape))
        {
            Exit();
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
            ResetRequestTestBaseline();
            _lastPlayerInput = null;
        }

        // These controls edit gameplay configuration; they never mutate the
        // motor's velocity directly. Any change resets to the same spawn.
        if (JustPressed(keys, Keys.F1))
            ApplyPreset(MovementGymTuning.Baseline, "Baseline");
        if (JustPressed(keys, Keys.F2))
            ApplyPreset(MovementGymTuning.Responsive, "Responsive");
        if (JustPressed(keys, Keys.F3))
            ApplyPreset(MovementGymTuning.Snappy, "Snappy");
        if (JustPressed(keys, Keys.F5))
            _gym.SetFaultSensitivity(SimulationFaultSensitivity.Strict);
        if (JustPressed(keys, Keys.F6))
            _gym.SetFaultSensitivity(SimulationFaultSensitivity.Diagnostic);
        if (JustPressed(keys, Keys.F7))
            _gym.SetFaultSensitivity(SimulationFaultSensitivity.Resilient);
        if (JustPressed(keys, Keys.F9))
            _gym.SetAssistancePolicy(_gym.AssistancePolicy.AnyEnabled
                ? CollisionAssistancePolicy.Disabled
                : MovementGymSession.PlayableAssistancePreset);

        // Request resolver gym is presentation/test input only. Each change
        // resets the level so A/B comparisons start from identical geometry.
        if (JustPressed(keys, Keys.F10))
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
        if (JustPressed(keys, Keys.Tab))
            _selectedParameter = (MovementGymParameter)(
                ((int)_selectedParameter + 1) % Enum.GetValues<MovementGymParameter>().Length);
        if (JustPressed(keys, Keys.OemMinus) || JustPressed(keys, Keys.Subtract))
            AdjustParameter(-1);
        if (JustPressed(keys, Keys.OemPlus) || JustPressed(keys, Keys.Add))
            AdjustParameter(1);

        if (JustPressed(keys, Keys.R))
            _gym.Reset();
        if (JustPressed(keys, Keys.P))
        {
            _paused = !_paused;
            if (!_paused)
            {
                // Resume is a new input-context boundary. A button held
                // during pause must not become an unsolicited fresh press.
                _inputRouter.SelectSource(_inputRouter.Source,
                    _inputRouter.Source == LocalInputSource.Keyboard
                        ? keyboard : gamepad);
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
            _gym.Step(MovementGymSession.FixedStepSeconds, _inputRequests,
                out MovementResolution committedResolution);
            _lastResolution = committedResolution;
            _lastPlayerInput = input;
            _lastKeyboardDirection = input.Direction;
        }

        _previousKeyboard = keys;
        if (++_titleRefreshCounter >= 6)
        {
            _titleRefreshCounter = 0;
            UpdateWindowTitle();
        }
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(18, 23, 35));
        if (_spriteBatch is not null && _pixel is not null)
        {
            _spriteBatch.Begin(samplerState: SamplerState.PointClamp);
            MovementGymRenderer.Draw(_spriteBatch, _pixel,
                GraphicsDevice.Viewport, _gym, _paused);
            DrawRequestTestHud(_spriteBatch, _pixel);
            DrawDeviceInputHud(_spriteBatch, _pixel);
            _spriteBatch.End();
        }
        base.Draw(gameTime);
    }

    protected override void UnloadContent()
    {
        _pixel?.Dispose();
        _spriteBatch?.Dispose();
        base.UnloadContent();
    }

    private void ResetRequestTestBaseline()
    {
        _gym.Reset();
        _lastResolution = null;
        _lastKeyboardDirection = HorizontalDirection.Neutral;
        _titleRefreshCounter = 6;
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
            "F8: Input Source  F9: Assist  F10: Request Test  F11: Case  F12: Reverse Order  " +
            $"Input={_inputRouter.Source} PadConnected={_gamepadOneConnected} " +
            $"RequestTest={(_requestTestEnabled ? _requestScenario.ToString() : "OFF")}" +
            "  A/D: Move  Space: Jump  R: Reset  P: Pause";
    }
}