using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.Motion.Profiles;
using PlatformerSandbox.Simulation.Diagnostics;
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
    private readonly MovementGymSession _gym = new();
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
        if (keys.IsKeyDown(Keys.Escape))
        {
            Exit();
            return;
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
            _paused = !_paused;

        if (!_paused)
        {
            bool left = keys.IsKeyDown(Keys.A) || keys.IsKeyDown(Keys.Left);
            bool right = keys.IsKeyDown(Keys.D) || keys.IsKeyDown(Keys.Right);
            HorizontalDirection direction = left == right
                ? HorizontalDirection.Neutral
                : left ? HorizontalDirection.Left : HorizontalDirection.Right;
            bool jumpPressed = JustPressed(keys, Keys.Space);
            bool jumpReleased = keys.IsKeyUp(Keys.Space) &&
                _previousKeyboard.IsKeyDown(Keys.Space);

            // Gameplay authority advances exactly once for every fixed Update.
            // MonoGame handles catch-up updates; rendering is observational.
            _gym.Step(MovementGymSession.FixedStepSeconds, direction,
                jumpPressed, jumpReleased);
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
            $"| Fault={_gym.FaultSensitivity} Recoveries={_gym.LastStepRecoveryCount} " +
            "| F1/F2/F3: Presets  F5/F6/F7: Fault Mode  Tab: Setting  -/+: Adjust  " +
            "A/D: Move  Space: Jump  R: Reset  P: Pause  Gold: Moving Platform";
    }
}
