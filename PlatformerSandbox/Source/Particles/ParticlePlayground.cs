using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation.Particles;
using PlatformerSandbox.Gyms;

namespace PlatformerSandbox.Particles;

/// <summary>
/// P02 isolated visual laboratory. Runs the real P01 headless pools/emitters,
/// but never creates or mutates WorldSession, physics or gameplay input.
/// In P03/P04 this renderer will also be usable by real scene presenters.
/// </summary>
internal sealed class ParticlePlayground : IDisposable
{
    private static readonly string[] PresetIds =
    [
        "lab.dust", "lab.sparks", "lab.smoke", "lab.motes", "lab.uiSparkles"
    ];

    private static readonly string[] CompositeIds =
    [
        "fx.landing", "fx.impact", "fx.fire", "fx.spores", "fx.uiFanfare"
    ];

    private readonly MonoGameParticleRenderer _renderer;
    private readonly ParticleCompositeRuntime _composites;
    private readonly ParticleDrawSource[] _compositeSources = new ParticleDrawSource[24 * 8];
    private int _compositeSourceCount;
    private ParticleCompositeInstance? _lastComposite;
    private bool _followLastComposite;
    private readonly ParticleSystem[] _pools = new ParticleSystem[5];
    private readonly ParticleEmitter[] _emitters = new ParticleEmitter[5];
    private readonly ParticleDrawSource[] _sources = new ParticleDrawSource[5];
    private KeyboardState _previousKeyboard;
    private MouseState _previousMouse;
    private Vector2D _cameraFocus = new(0, 0);
    private double _pixelsPerUnit = 38;
    private int _selected;
    private bool _worldPaused;
    private bool _hideHelp;
    private bool _disposed;
    private long _stressRuns;
    private readonly System.Diagnostics.Stopwatch _updateWatch = new();
    private readonly System.Diagnostics.Stopwatch _drawWatch = new();
    private double _lastUpdateMs;
    private double _lastDrawMs;

    public bool ExitRequested { get; private set; }

    public ParticlePlayground(GraphicsDevice device, string manifestPath)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("Manifest path is required.", nameof(manifestPath));
        ParticleEmitterLibrary library = ParticleEmitterLibrary.FromJson(File.ReadAllText(manifestPath));
        string compositePath = Path.Combine(Path.GetDirectoryName(manifestPath)!,
            "P03_CompositeEffects.json");
        ParticleCompositeCatalog catalog = ParticleCompositeCatalog.FromJson(
            File.ReadAllText(compositePath));
        _composites = new ParticleCompositeRuntime(catalog, maxInstances: 24,
            seed: 0xBEEF2026UL);
        _renderer = new MonoGameParticleRenderer(device);
        try
        {
            var layers = new[]
            {
                ParticleVisualLayer.World,
                ParticleVisualLayer.Foreground,
                ParticleVisualLayer.Background,
                ParticleVisualLayer.Background,
                ParticleVisualLayer.Ui
            };
            var blends = new[]
            {
                ParticleBlendMode.Alpha, ParticleBlendMode.Additive,
                ParticleBlendMode.Alpha, ParticleBlendMode.Alpha, ParticleBlendMode.Additive
            };
            var sprites = new[]
            {
                ParticleSpriteShape.SolidDisc, ParticleSpriteShape.Spark,
                ParticleSpriteShape.SoftDisc, ParticleSpriteShape.SoftDisc,
                ParticleSpriteShape.Spark
            };
            for (int i = 0; i < _pools.Length; i++)
            {
                _pools[i] = new ParticleSystem(i == 4 ? 256 : 512,
                    faultPolicy: ParticleFaultPolicy.Strict);
                _emitters[i] = new ParticleEmitter(_pools[i], library.Get(PresetIds[i]),
                    seed: 0xA019C0DEUL + (ulong)i * 7919);
                _sources[i] = new ParticleDrawSource(_pools[i], layers[i],
                    i == 4 ? ParticleCoordinateSpace.Screen : ParticleCoordinateSpace.World,
                    blends[i], sprites[i]);
            }
            // Visible immediately, even before interacting with the laboratory.
            _emitters[0].Burst(new Vector2D(-2.2, -1.8));
            _emitters[1].Burst(new Vector2D(1.8, 0.2));
            _emitters[2].Burst(new Vector2D(-3.7, 0));
            _composites.TryPlay("fx.landing", new Vector2D(0, -1.5), out _lastComposite);
        }
        catch
        {
            _renderer.Dispose();
            throw;
        }
        _previousKeyboard = Keyboard.GetState();
        _previousMouse = Mouse.GetState();
    }

    public void Update(double elapsed, KeyboardState keys, MouseState mouse,
        int viewportWidth, int viewportHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(elapsed) || elapsed < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsed));
        _updateWatch.Restart();
        double dt = Math.Min(elapsed, .1);
        if (Pressed(keys, Keys.Escape)) ExitRequested = true;
        if (Pressed(keys, Keys.P)) _worldPaused = !_worldPaused;
        if (Pressed(keys, Keys.H)) _hideHelp = !_hideHelp;
        if (Pressed(keys, Keys.C))
        {
            foreach (ParticleSystem pool in _pools) pool.Clear();
            _composites.Clear();
            _lastComposite = null;
        }
        if (Pressed(keys, Keys.X)) _lastComposite?.Stop();
        if (Pressed(keys, Keys.F)) _followLastComposite = !_followLastComposite;
        if (Pressed(keys, Keys.D1)) _selected = 0;
        if (Pressed(keys, Keys.D2)) _selected = 1;
        if (Pressed(keys, Keys.D3)) _selected = 2;
        if (Pressed(keys, Keys.D4)) _selected = 3;
        if (Pressed(keys, Keys.D5)) _selected = 4;
        if (Pressed(keys, Keys.D6)) _selected = 5;
        if (Pressed(keys, Keys.D7)) _selected = 6;
        if (Pressed(keys, Keys.D8)) _selected = 7;
        if (Pressed(keys, Keys.D9)) _selected = 8;
        if (Pressed(keys, Keys.D0)) _selected = 9;

        // Layer/blend inspection is limited to the selected emitter. Existing
        // particles move with their emitter's new *render* style, never physics.
        if (Pressed(keys, Keys.Tab) && _selected < 4)
        {
            ParticleDrawSource style = _sources[_selected];
            ParticleVisualLayer next = style.Layer switch
            {
                ParticleVisualLayer.Background => ParticleVisualLayer.World,
                ParticleVisualLayer.World => ParticleVisualLayer.Foreground,
                _ => ParticleVisualLayer.Background
            };
            _sources[_selected] = style with { Layer = next };
        }
        if (Pressed(keys, Keys.B) && _selected < 5)
        {
            ParticleDrawSource style = _sources[_selected];
            _sources[_selected] = style with
            {
                Blend = style.Blend == ParticleBlendMode.Alpha
                    ? ParticleBlendMode.Additive : ParticleBlendMode.Alpha
            };
        }

        double panX = (keys.IsKeyDown(Keys.D) ? 1 : 0) - (keys.IsKeyDown(Keys.A) ? 1 : 0);
        double panY = (keys.IsKeyDown(Keys.W) ? 1 : 0) - (keys.IsKeyDown(Keys.S) ? 1 : 0);
        _cameraFocus = new Vector2D(_cameraFocus.X + panX * dt * 7,
            _cameraFocus.Y + panY * dt * 7);
        double zoomDirection = (keys.IsKeyDown(Keys.E) ? 1 : 0) -
            (keys.IsKeyDown(Keys.Q) ? 1 : 0);
        _pixelsPerUnit = Math.Clamp(_pixelsPerUnit * Math.Exp(zoomDirection * dt * 1.3),
            16, 110);

        if (viewportWidth > 0 && viewportHeight > 0)
        {
            ParticleViewport viewport = CreateView(viewportWidth, viewportHeight);
            if (Pressed(keys, Keys.Space))
                BurstSelected(IsScreenSelected()
                    ? new Vector2D(viewportWidth * .73, 148) : _cameraFocus);
            if (mouse.LeftButton == ButtonState.Pressed &&
                _previousMouse.LeftButton != ButtonState.Pressed)
                BurstSelected(IsScreenSelected()
                    ? new Vector2D(mouse.X, mouse.Y)
                    : viewport.ToWorld(mouse.X, mouse.Y));
            if (mouse.RightButton == ButtonState.Pressed &&
                _previousMouse.RightButton != ButtonState.Pressed)
                _emitters[4].Burst(new Vector2D(mouse.X, mouse.Y));
            if (Pressed(keys, Keys.G))
            {
                _stressRuns++;
                // Exercise both the P01 particle cap and P03 composite-instance cap.
                for (int i = 0; i < 80; i++)
                    _emitters[1].Burst(new Vector2D(_cameraFocus.X +
                        ((i % 8) - 3.5) * .7, _cameraFocus.Y + (i / 8) * .18));
                for (int i = 0; i < 60; i++)
                    _composites.TryPlay("fx.impact", new Vector2D(
                        _cameraFocus.X + (i % 10 - 5) * .35,
                        _cameraFocus.Y + (i / 10) * .25), out _);
            }
            // UI emitters use independent presentation time; world-space
            // particles freeze while P is active, even as the camera pans.
            if (_followLastComposite && _lastComposite is not null)
            {
                Vector2D anchor = _lastComposite.Definition.Parts![0].Space ==
                    ParticleCoordinateSpace.Screen
                        ? new Vector2D(mouse.X, mouse.Y) : _cameraFocus;
                _lastComposite.MoveSource(anchor);
            }
            _composites.Update(dt, worldPaused: _worldPaused);
            _emitters[4].Update(new Vector2D(viewportWidth * .8, 113), dt);
            _pools[4].Update(dt);
            if (!_worldPaused)
            {
                _emitters[3].Update(_cameraFocus, dt);
                for (int i = 0; i < 4; i++) _pools[i].Update(dt);
            }
        }
        _previousKeyboard = keys;
        _previousMouse = mouse;
        _updateWatch.Stop();
        _lastUpdateMs = _updateWatch.Elapsed.TotalMilliseconds;
    }

    public void Draw(GraphicsDevice device, SpriteBatch batch, Texture2D pixel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(pixel);
        Viewport vp = device.Viewport;
        if (vp.Width <= 0 || vp.Height <= 0) return;
        _drawWatch.Restart();
        ParticleViewport view = CreateView(vp.Width, vp.Height);
        _renderer.BeginFrame();
        RefreshCompositeDrawSources();
        DrawBackdrop(batch, pixel, view);
        _renderer.DrawLayer(batch, view, _sources, ParticleVisualLayer.Background);
        _renderer.DrawLayer(batch, view,
            _compositeSources.AsSpan(0, _compositeSourceCount), ParticleVisualLayer.Background);
        DrawMiddleGround(batch, pixel, view);
        _renderer.DrawLayer(batch, view, _sources, ParticleVisualLayer.World);
        _renderer.DrawLayer(batch, view,
            _compositeSources.AsSpan(0, _compositeSourceCount), ParticleVisualLayer.World);
        DrawWorldReference(batch, pixel, view);
        _renderer.DrawLayer(batch, view, _sources, ParticleVisualLayer.Foreground);
        _renderer.DrawLayer(batch, view,
            _compositeSources.AsSpan(0, _compositeSourceCount), ParticleVisualLayer.Foreground);
        DrawHud(batch, pixel, view);
        _renderer.DrawLayer(batch, view, _sources, ParticleVisualLayer.Ui);
        _renderer.DrawLayer(batch, view,
            _compositeSources.AsSpan(0, _compositeSourceCount), ParticleVisualLayer.Ui);
        _drawWatch.Stop();
        _lastDrawMs = _drawWatch.Elapsed.TotalMilliseconds;
    }

    private ParticleViewport CreateView(int width, int height) =>
        new(width, height, _cameraFocus, _pixelsPerUnit);

    private bool Pressed(KeyboardState keys, Keys key) =>
        keys.IsKeyDown(key) && _previousKeyboard.IsKeyUp(key);

    private bool IsScreenSelected() => _selected is 4 or 9;

    private void BurstSelected(Vector2D origin)
    {
        if (_selected < 5)
        {
            // Single-emitter P02 controls remain available for regression.
            _emitters[_selected].Burst(origin);
        }
        else
            _composites.TryPlay(CompositeIds[_selected - 5], origin, out _lastComposite);
    }

    private void RefreshCompositeDrawSources()
    {
        _compositeSourceCount = 0;
        foreach (ParticleCompositeInstance instance in _composites.Instances)
            foreach (ParticleEffectPart part in instance.Parts)
            {
                ParticleEffectPartDefinition style = part.Definition;
                _compositeSources[_compositeSourceCount++] = new ParticleDrawSource(
                    part.Particles, style.Layer, style.Space, style.Blend, style.Sprite, style.Lighting);
            }
    }

    private static void DrawBackdrop(SpriteBatch batch, Texture2D pixel, ParticleViewport v)
    {
        batch.Begin(blendState: BlendState.AlphaBlend,
            samplerState: SamplerState.PointClamp);
        batch.Draw(pixel, new Rectangle(0, 0, v.Width, v.Height), new Color(15, 20, 34));
        for (int n = 0; n < 14; n++)
        {
            int y = n * v.Height / 14;
            int h = Math.Max(1, (n + 1) * v.Height / 14 - y);
            batch.Draw(pixel, new Rectangle(0, y, v.Width, h),
                new Color(15 + n, 20 + n, 34 + n * 2));
        }
        batch.End();
    }

    private static void DrawMiddleGround(SpriteBatch batch, Texture2D pixel,
        ParticleViewport v)
    {
        batch.Begin(blendState: BlendState.AlphaBlend);
        // The floor and silhouettes deliberately cover background VFX while
        // world particles render on top of this terrain and behind the marker.
        WorldRect(batch, pixel, v, -100, -80, 200, 77,
            new Color(35, 49, 63));
        WorldRect(batch, pixel, v, -100, -3.05, 200, .11,
            new Color(78, 105, 113));
        for (int i = -10; i <= 10; i += 2)
        {
            double x = i * 3.3;
            WorldRect(batch, pixel, v, x, -3.0, .25, 2 + (i * i % 5),
                new Color(36, 54, 67));
        }
        // A camera-aligned world-space axis is a useful parallax/zoom probe.
        WorldRect(batch, pixel, v, -.02, -2.9, .04, 13,
            new Color(73, 91, 104) * .38f);
        WorldRect(batch, pixel, v, -18, -.02, 36, .04,
            new Color(73, 91, 104) * .38f);
        batch.End();
    }

    private static void DrawWorldReference(SpriteBatch batch, Texture2D pixel,
        ParticleViewport v)
    {
        batch.Begin(blendState: BlendState.AlphaBlend);
        // World marker occludes background/world effects; foreground goes over it.
        WorldRect(batch, pixel, v, -.38, -1.95, .76, 1.95,
            new Color(73, 180, 171));
        WorldRect(batch, pixel, v, -.28, -.34, .13, .13,
            new Color(233, 248, 231));
        WorldRect(batch, pixel, v, .12, -.34, .13, .13,
            new Color(233, 248, 231));
        batch.End();
    }

    private static void WorldRect(SpriteBatch batch, Texture2D pixel,
        ParticleViewport v, double x, double y, double width, double height,
        Color color)
    {
        ParticleScreenPoint leftTop = v.ToScreen(new Vector2D(x, y + height),
            ParticleCoordinateSpace.World);
        int xPx = (int)Math.Round(leftTop.X);
        int yPx = (int)Math.Round(leftTop.Y);
        int wPx = Math.Max(1, (int)Math.Round(width * v.PixelsPerWorldUnit));
        int hPx = Math.Max(1, (int)Math.Round(height * v.PixelsPerWorldUnit));
        batch.Draw(pixel, new Rectangle(xPx, yPx, wPx, hPx), color);
    }

    private void DrawHud(SpriteBatch batch, Texture2D pixel, ParticleViewport v)
    {
        if (_hideHelp) return;
        long dropped = 0;
        int active = 0;
        int budget = 0;
        for (int i = 0; i < _pools.Length; i++)
        {
            ParticleDiagnostics d = _pools[i].Diagnostics;
            dropped += d.DroppedAtCapacity + d.DroppedInvalid;
            active += d.ActiveParticles;
            budget += d.Capacity;
        }
        foreach (ParticleCompositeInstance instance in _composites.Instances)
            foreach (ParticleEffectPart part in instance.Parts)
            {
                ParticleDiagnostics d = part.Particles.Diagnostics;
                active += d.ActiveParticles;
                dropped += d.DroppedAtCapacity + d.DroppedInvalid;
                budget += d.Capacity;
            }
        string selection = _selected < 5
            ? "EMITTER " + PresetIds[_selected] + "   " + _sources[_selected].Layer +
              "/" + _sources[_selected].Blend
            : "COMPOSITE " + CompositeIds[_selected - 5] + "   " +
              _composites.Catalog.Get(CompositeIds[_selected - 5]).Parts!.Length + " PARTS";
        batch.Begin(blendState: BlendState.AlphaBlend);
        int panelWidth = Math.Max(1, Math.Min(1160, v.Width - 16));
        batch.Draw(pixel, new Rectangle(8, 8, panelWidth, 189),
            new Color(10, 15, 25) * .94f);
        GymDebugText.Draw(batch, pixel, 18, 17,
            "P03 COMPOSITE PARTICLE PLAYGROUND", new Color(233, 208, 144), 2);
        GymDebugText.Draw(batch, pixel, 18, 42,
            "SINGLE EMITTERS 1 DUST  2 SPARKS  3 SMOKE  4 MOTES  5 UI", new Color(197, 221, 229), 1);
        GymDebugText.Draw(batch, pixel, 18, 55,
            "COMPOSITES 6 LANDING  7 IMPACT  8 FIRE  9 SPORES  0 UI FANFARE", new Color(197, 221, 229), 1);
        GymDebugText.Draw(batch, pixel, 18, 68,
            "LEFT CLICK/SPACE PLAY   RIGHT CLICK UI SPARKLES   F FOLLOW SOURCE", new Color(197, 221, 229), 1);
        GymDebugText.Draw(batch, pixel, 18, 81,
            "X STOP LAST EFFECT   C CLEAR ALL   G STRESS   H HIDE HUD", new Color(197, 221, 229), 1);
        GymDebugText.Draw(batch, pixel, 18, 94,
            "WASD CAMERA  Q/E ZOOM  TAB LAYER/B BLEND (SINGLES)  P PAUSE WORLD  ESC EXIT",
            new Color(197, 221, 229), 1);
        GymDebugText.Draw(batch, pixel, 18, 112, selection,
            new Color(237, 203, 137), 1);
        GymDebugText.Draw(batch, pixel, 18, 125,
            $"ACTIVE {active}/{budget}  INSTANCES {_composites.ActiveInstances}/{_composites.MaxInstances}  DROPPED {dropped} + {_composites.DroppedAtCapacity} INST",
            new Color(174, 233, 181), 1);
        GymDebugText.Draw(batch, pixel, 18, 138,
            $"WORLD {(_worldPaused ? "PAUSED" : "RUNNING")}  UI RUNNING  SOURCE {(_followLastComposite ? "FOLLOW" : "FIXED")}",
            new Color(174, 233, 181), 1);
        GymDebugText.Draw(batch, pixel, 18, 151,
            $"ZOOM {_pixelsPerUnit:0.0} PX/U  DRAWN {_renderer.LastDrawCount}  STRESS {_stressRuns}",
            new Color(174, 233, 181), 1);
        GymDebugText.Draw(batch, pixel, 18, 164,
            $"UPDATE {_lastUpdateMs:0.00} MS  DRAW {_lastDrawMs:0.00} MS",
            new Color(174, 198, 218), 1);
        batch.End();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderer.Dispose();
    }
}
