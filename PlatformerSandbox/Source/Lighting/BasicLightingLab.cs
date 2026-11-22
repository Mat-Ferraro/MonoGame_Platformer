using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gyms;

namespace PlatformerSandbox.Lighting;

/// <summary>
/// Isolated lab for Stage 3-8 lighting with bounded convex shadow blockers.
/// All surfaces use the GameplayLightingRenderer's per-pixel GPU compositor.
/// No LDtk, physics, concave polygons or postprocessing here.
/// </summary>
internal sealed class BasicLightingLab : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly GameplayLightingRenderer _renderer;
    private readonly BasicLightingPreviewState _state = new();
    private readonly BasicLightingInspector _inspector = new();
    private readonly Texture2D _sampleTexture;
    private readonly Texture2D _normalTexture;
    private readonly Texture2D _edgeTexture;
    private readonly Texture2D _rectangleFace;
    private readonly Texture2D _triangleFace;
    private readonly Texture2D _trapezoidFace;
    private bool _previousMouseDown;
    private bool _draggingBlocker;
    private LightPoint _dragOffset;
    private KeyboardState _lastKeys;
    private bool _showHint = true;
    private bool _disposed;

    public bool ExitRequested { get; private set; }

    public BasicLightingLab(GraphicsDevice device, ContentManager content)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        ArgumentNullException.ThrowIfNull(content);
        // The shader is built by Content/Builder and shared with the rendering path.
        // ContentManager owns this Effect, so the renderer must not dispose it.
        Effect shader = content.Load<Effect>("Effects/L_BASIC_SurfaceLighting");
        _renderer = new GameplayLightingRenderer(device, shader);
        _sampleTexture = MakeTestSurface(device);
        _normalTexture = MakeNormalMap(device);
        _edgeTexture = MakeEdgeMap(device);
        _rectangleFace = MakeBlockerFace(device, BasicShadowShapeKind.Rectangle);
        _triangleFace = MakeBlockerFace(device, BasicShadowShapeKind.Triangle);
        _trapezoidFace = MakeBlockerFace(device, BasicShadowShapeKind.Trapezoid);
        _lastKeys = Keyboard.GetState();
    }

    // Straight-alpha test art: a border, a cross, and transparent cut-outs.
    // It is drawn to the albedo target using NonPremultiplied blending, then
    // its final opacity is applied ONCE at the compositor boundary.
    private static Texture2D MakeTestSurface(GraphicsDevice device)
    {
        const int size = 64;
        var result = new Texture2D(device, size, size, false, SurfaceFormat.Color);
        var data = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool hole = (x is >= 19 and <= 28 && y is >= 11 and <= 21)
                    || (x is >= 37 and <= 46 && y is >= 37 and <= 47);
                Color color;
                if (hole) color = Color.Transparent;
                else if (x < 4 || y < 4 || x >= 60 || y >= 60)
                    color = new Color(48, 58, 73);
                else if (Math.Abs(x - 32) < 3 || Math.Abs(y - 32) < 3)
                    color = new Color(191, 125, 74);
                else
                    color = ((x / 8 + y / 8) & 1) == 0
                        ? new Color(163, 172, 187) : new Color(118, 134, 153);
                data[y * size + x] = color;
            }
        result.SetData(data);
        return result;
    }

    // Deterministic, generated maps — no external image authoring tools required.
    // Their pixel coordinates exactly match the 64x64 albedo sample.
    private static bool Occupied(int x, int y)
    {
        if (x < 0 || x >= 64 || y < 0 || y >= 64) return false;
        return !(x is >= 19 and <= 28 && y is >= 11 and <= 21)
            && !(x is >= 37 and <= 46 && y is >= 37 and <= 47);
    }

    private static byte Channel(double value) =>
        (byte)Math.Clamp((int)Math.Round((Math.Clamp(value, -1, 1) + 1) * 127.5), 0, 255);

    private static Texture2D MakeNormalMap(GraphicsDevice device)
    {
        const int size = 64;
        var result = new Texture2D(device, size, size, false, SurfaceFormat.Color);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // A readable raised/indented pattern with measurable shading
                // when the light moves. The underlying diffuse art stays fixed.
                double dx = .55 * Math.Sin(x * Math.PI / 16.0);
                double dy = .55 * Math.Sin(y * Math.PI / 16.0);
                if (x < 6) dx -= .75;
                if (x > 57) dx += .75;
                if (y < 6) dy -= .75;
                if (y > 57) dy += .75;
                double inv = 1.0 / Math.Sqrt(dx * dx + dy * dy + 1);
                pixels[y * size + x] = new Color(Channel(dx * inv),
                    Channel(dy * inv), Channel(inv), (byte)255);
            }
        result.SetData(pixels);
        return result;
    }

    private static Texture2D MakeEdgeMap(GraphicsDevice device)
    {
        const int size = 64;
        var result = new Texture2D(device, size, size, false, SurfaceFormat.Color);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                if (!Occupied(x, y))
                {
                    pixels[y * size + x] = new Color(128, 128, 128, 0);
                    continue;
                }
                // Signed contour direction: outward from solid art towards
                // transparent space, including the two interior cut-out holes.
                int rx = Occupied(x + 3, y) ? 0 : 1;
                int lx = Occupied(x - 3, y) ? 0 : 1;
                int by = Occupied(x, y + 3) ? 0 : 1;
                int ay = Occupied(x, y - 3) ? 0 : 1;
                double nx = rx - lx, ny = by - ay;
                double length = Math.Sqrt(nx * nx + ny * ny);
                byte strength = length > 0 ? (byte)230 : (byte)0;
                pixels[y * size + x] = new Color(
                    Channel(length > 0 ? nx / length : 0),
                    Channel(length > 0 ? ny / length : 0), (byte)128, strength);
            }
        result.SetData(pixels);
        return result;
    }

    // Neutral lab-only silhouette textures. No baked directional highlight,
    // rim strip, or seam: all visible brightness is from the light shader.
    private static Texture2D MakeBlockerFace(GraphicsDevice device, BasicShadowShapeKind kind)
    {
        const int size = 128;
        var texture = new Texture2D(device, size, size, false, SurfaceFormat.Color);
        var pixels = new Color[size * size];
        var silhouette = new BasicShadowShape(new BasicShadowRectangle(0, 0, 1, 1), kind);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = silhouette.Contains(new LightPoint(
                    (x + .5) / size, (y + .5) / size))
                    ? new Color(105, 115, 127) : Color.Transparent;
        texture.SetData(pixels);
        return texture;
    }

    private Texture2D BlockerFace(BasicShadowShapeKind kind) => kind switch
    {
        BasicShadowShapeKind.Triangle => _triangleFace,
        BasicShadowShapeKind.Trapezoid => _trapezoidFace,
        _ => _rectangleFace
    };

    private bool Pressed(KeyboardState current, Keys key) =>
        current.IsKeyDown(key) && _lastKeys.IsKeyUp(key);

    public void Update(KeyboardState keyboard, MouseState mouse,
        int width, int height, bool interactive = true, bool standalone = true,
        double elapsedSeconds = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        if (width > 0 && height > 0)
            _state.SetShadowViewportAspectRatio((double)width / height);
        bool down = mouse.LeftButton == ButtonState.Pressed;
        bool clicked = down && !_previousMouseDown;
        if (!interactive)
        {
            _inspector.Update(mouse, width, height, _state, interactive: false);
            _previousMouseDown = down;
            _draggingBlocker = false;
            _lastKeys = keyboard;
            return;
        }
        if (standalone && Pressed(keyboard, Keys.Escape)) ExitRequested = true;
        if (Pressed(keyboard, Keys.R)) _state.Reset();
        if (Pressed(keyboard, Keys.H)) _showHint = !_showHint;
        bool uiOwnsMouse = _inspector.Update(mouse, width, height, _state);
        if (!down) _draggingBlocker = false;
        if (!uiOwnsMouse && down && width > 0 && height > 0)
        {
            double nx = (double)mouse.X / width, ny = (double)mouse.Y / height;
            if (clicked)
            {
                // Direct click selects a visible blocker and starts a drag.
                // Shift-drag still moves the selected blocker from empty space.
                if (_state.SelectShadowBlockerAt(nx, ny))
                {
                    _draggingBlocker = true;
                    _dragOffset = new LightPoint(nx - _state.SelectedShadowBlocker.X,
                        ny - _state.SelectedShadowBlocker.Y);
                }
                else if ((keyboard.IsKeyDown(Keys.LeftShift) ||
                          keyboard.IsKeyDown(Keys.RightShift)) && _state.SelectedShadowBlockerEnabled)
                {
                    _draggingBlocker = true;
                    _dragOffset = new LightPoint(_state.SelectedShadowBlocker.Width / 2,
                        _state.SelectedShadowBlocker.Height / 2);
                }
            }
            if (_draggingBlocker && _state.SelectedShadowBlockerEnabled)
                _state.SetSelectedShadowBlockerPosition(nx - _dragOffset.X,
                    ny - _dragOffset.Y);
            else if (!_draggingBlocker)
                _state.SetLightPositionNormalized(nx, ny);
        }
        // Render submission below reads current shapes, so moving shadows,
        // per-light occlusion and depth-aware rims share one moving silhouette.
        // Pause/menu never advances lab motion (early return above).
        _state.AdvanceBlockerMotion(elapsedSeconds,
            _draggingBlocker && down ? _state.SelectedShadowBlockerIndex : -1);
        _previousMouseDown = down;
        _lastKeys = keyboard;
    }

    public void Draw(SpriteBatch batch, Texture2D pixel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(pixel);
        int width = _device.PresentationParameters.BackBufferWidth;
        int height = _device.PresentationParameters.BackBufferHeight;
        if (width < 1 || height < 1) return;
        _state.SetShadowViewportAspectRatio((double)width / height);

        var view = new LightingViewport(width, height, new LightPoint(0, 0), 1);
        _renderer.BeginBasicMaterialFrameWithShapes(view, _state.AmbientRgb,
            _state.BuildMaterialLights(view), _state.ActiveShadowShapes);
        try
        {
            _renderer.DrawBasicMaterial(batch, new BasicSurfaceMaterial(
                BasicSurfaceLightingMode.Lit, BasicLightReceiverChannels.Both, 1),
                b => DrawBackground(b, pixel, width, height));
            int size = SampleSize(width, height);
            int sampleY = SampleY(height, size);
            for (int i = 0; i < BasicLightingPreviewState.SurfaceCount; i++)
            {
                int index = i; // local copy for the draw callback
                int x = SampleX(width, size, index);
                BasicSurfaceMaterial material = _state.GetSurface(index);
                Rectangle destination = new(x, sampleY, size, size);
                _renderer.DrawBasicMaterial(batch, material,
                    b => b.Draw(_sampleTexture, destination, Color.White),
                    b => b.Draw(_normalTexture, destination, Color.White),
                    b => b.Draw(_edgeTexture, destination, Color.White));
            }
            // Render matching convex silhouettes rather than rectangular proxies;
            // the underlying shader receives exactly the same vertex definitions.
            // Active shadow slots are COMPACTED; inactive authoring slots have no
            // GPU entry, so track their identity rather than assuming i == GPU slot.
            int activeShadowSlot = 0;
            for (int i = 0; i < BasicLightingPreviewState.ShadowBlockerCount; i++)
            {
                if (!_state.GetShadowBlockerEnabled(i)) continue;
                BasicShadowShape shape = _state.GetShadowShape(i);
                Rectangle blockerArea = BlockerPixels(shape.Bounds, width, height);
                var blockerFace = new BasicSurfaceMaterial(
                    BasicSurfaceLightingMode.Lit, BasicLightReceiverChannels.Both, 1)
                    { ReceivesShadows = false };
                Texture2D face = BlockerFace(shape.Kind);
                _renderer.DrawBasicMaterial(batch, blockerFace,
                    b => DrawBlockerFace(b, face, shape, width, height, blockerArea),
                    wallOverlap: _state.GetWallLightOverlap(shape,
                        _state.GetShadowBlockerDepthLayer(i)),
                    wallRimContext: _state.GetWallRimContext(shape),
                    wallRimShape: shape,
                    wallRimShadowSlot: activeShadowSlot);
                activeShadowSlot++;
            }
            _renderer.EndBasicMaterialFrame(batch, _state.DebugView, _state.SelectedLightId);
        }
        catch
        {
            _renderer.Abort();
            throw;
        }

        batch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.PointClamp);
        if (_state.DebugView is BasicLightingDebugView.Final or BasicLightingDebugView.Albedo)
        {
            GymDebugText.Draw(batch, pixel, 14, 70, "SHADOW BLOCKERS (CLICK TO SELECT)",
                new Color(192, 213, 222), 1);
            int sampleTop = SampleY(height, SampleSize(width, height));
            GymDebugText.Draw(batch, pixel, 14, Math.Max(78, sampleTop - 22),
                "MATERIAL SAMPLES A-D", new Color(192, 213, 222), 1);
        }
        if (_state.DebugView is BasicLightingDebugView.Final or BasicLightingDebugView.Albedo)
        {
            int size = SampleSize(width, height);
            int y = SampleY(height, size);
            for (int i = 0; i < BasicLightingPreviewState.SurfaceCount; i++)
            {
                int x = SampleX(width, size, i);
                var material = _state.GetSurface(i);
                if (_state.SelectedSurfaceIndex == i)
                    DrawOutline(batch, pixel, x - 3, y - 3, size + 6, size + 6, new Color(104, 237, 184));
                string mode = material.Mode == BasicSurfaceLightingMode.Unlit ? "UNLIT" :
                    material.Receives switch
                    {
                        BasicLightReceiverChannels.Both => "BOTH",
                        BasicLightReceiverChannels.Light1 => "L1",
                        BasicLightReceiverChannels.Light2 => "L2",
                        _ => "AMBIENT"
                    };
                string shading = material.Shading switch
                {
                    BasicSurfaceShadingMode.Normal => "NORM",
                    BasicSurfaceShadingMode.Rim => "RIM",
                    BasicSurfaceShadingMode.Hybrid => "HYBR",
                    _ => "FLAT"
                };
                GymDebugText.Draw(batch, pixel, x + 2,
                    Math.Min(height - 12, y + size + 9),
                    $"{(char)('A' + i)} {mode} {shading}", Color.White, 1);
            }
        }
        if (_state.ShowBlockerOutlines)
        for (int i = 0; i < BasicLightingPreviewState.ShadowBlockerCount; i++)
        {
            if (!_state.GetShadowBlockerEnabled(i)) continue;
            BasicShadowShape shape = _state.GetShadowShape(i);
            LightPoint[] points = shape.Vertices();
            Color outline = i == _state.SelectedShadowBlockerIndex
                ? new Color(115, 251, 200) : new Color(246, 200, 108);
            for (int j = 0; j < points.Length; j++)
            {
                LightPoint a = points[j], b = points[(j + 1) % points.Length];
                DrawLine(batch, pixel, new Vector2((float)(a.X * width), (float)(a.Y * height)),
                    new Vector2((float)(b.X * width), (float)(b.Y * height)), outline);
            }
            Rectangle bounds = BlockerPixels(shape.Bounds, width, height);
            GymDebugText.Draw(batch, pixel, bounds.X + bounds.Width / 2,
                bounds.Y + bounds.Height / 2, (i + 1).ToString(), outline, 1);
        }
        if (_state.DebugView != BasicLightingDebugView.Albedo)
        {
            // Editor gizmo, deliberately outside the light/material passes.
            LightPoint selected = _state.LightPositionNormalized;
            int lx = (int)Math.Round(selected.X * width);
            int ly = (int)Math.Round(selected.Y * height);
            Fill(batch, pixel, lx - 7, ly, 15, 1, Color.White);
            Fill(batch, pixel, lx, ly - 7, 1, 15, Color.White);
        }
        if (_showHint)
        {
            Fill(batch, pixel, 8, height - 32, Math.Min(width - 16, 670), 23,
                new Color(14, 20, 27, 220));
            GymDebugText.Draw(batch, pixel, 15, height - 25,
                "MATERIAL SAMPLES A-D  |  CLICK WALL TO MOVE  |  SHOW OUTLINES IN LIGHT RULES",
                new Color(219, 230, 239), 1);
        }
        _inspector.Draw(batch, pixel, width, height, _state);
        batch.End();
    }

    /// <summary>
    /// Rotating a GPU polygon without rotating its visible face creates false
    /// shadows. SpriteBatch and BasicShadowShape.Vertices use the same center,
    /// angle and pixel-space dimensions (independent of viewport aspect ratio).
    /// The unrotated path remains bit-for-bit equivalent to Stage 13.
    /// </summary>
    private static void DrawBlockerFace(SpriteBatch batch, Texture2D face,
        BasicShadowShape shape, int width, int height, Rectangle destination)
    {
        if (shape.RotationDegrees % 360 == 0)
        {
            batch.Draw(face, destination, Color.White);
            return;
        }
        BasicShadowRectangle bounds = shape.Bounds;
        var center = new Vector2((float)((bounds.X + bounds.Width * .5) * width),
            (float)((bounds.Y + bounds.Height * .5) * height));
        var scale = new Vector2((float)(bounds.Width * width / face.Width),
            (float)(bounds.Height * height / face.Height));
        batch.Draw(face, center, null, Color.White,
            (float)(shape.RotationDegrees * Math.PI / 180.0),
            new Vector2(face.Width * .5f, face.Height * .5f),
            scale, SpriteEffects.None, 0);
    }

    private static Rectangle BlockerPixels(BasicShadowRectangle rect, int width, int height)
    {
        int x = (int)Math.Round(rect.X * width);
        int y = (int)Math.Round(rect.Y * height);
        int right = (int)Math.Round(rect.Right * width);
        int bottom = (int)Math.Round(rect.Bottom * height);
        return new Rectangle(x, y, Math.Max(1, right - x), Math.Max(1, bottom - y));
    }

    private static void DrawLine(SpriteBatch batch, Texture2D pixel,
        Vector2 from, Vector2 to, Color color)
    {
        Vector2 delta = to - from;
        float length = delta.Length();
        if (length < .1f) return;
        batch.Draw(pixel, from, null, color, (float)Math.Atan2(delta.Y, delta.X),
            Vector2.Zero, new Vector2(length, 2), SpriteEffects.None, 0);
    }

    private static int SampleSize(int width, int height) =>
        Math.Clamp(Math.Min(width / 12, height / 5), 40, 100);

    private static int SampleY(int height, int size) =>
        Math.Clamp(height * 5 / 8, 52, Math.Max(52, height - size - 26));

    private static int SampleX(int width, int size, int index) =>
        Math.Clamp((int)(width * (.13 + index * .185)) - size / 2, 4, Math.Max(4, width - size - 4));

    private static void Fill(SpriteBatch batch, Texture2D pixel,
        int x, int y, int width, int height, Color color)
    {
        if (width > 0 && height > 0)
            batch.Draw(pixel, new Rectangle(x, y, width, height), color);
    }

    private static void DrawOutline(SpriteBatch batch, Texture2D pixel,
        int x, int y, int width, int height, Color color)
    {
        Fill(batch, pixel, x, y, width, 2, color);
        Fill(batch, pixel, x, y + height - 2, width, 2, color);
        Fill(batch, pixel, x, y, 2, height, color);
        Fill(batch, pixel, x + width - 2, y, 2, height, color);
    }

    private static void DrawBackground(SpriteBatch batch, Texture2D pixel, int width, int height)
    {
        Fill(batch, pixel, 0, 0, width, height, new Color(112, 122, 132));
        const int tileW = 64, tileH = 46;
        for (int ty = 0; ty < height; ty += tileH)
            for (int tx = 0; tx < width; tx += tileW)
            {
                int offset = ((tx / tileW + ty / tileH) & 1) == 0 ? 0 : 13;
                Fill(batch, pixel, tx + 2, ty + 2, tileW - 3, tileH - 3,
                    new Color(109 + offset, 118 + offset, 127 + offset));
            }
        int floorY = height * 4 / 5;
        Fill(batch, pixel, 0, floorY, width, height - floorY, new Color(104, 109, 117));
        Fill(batch, pixel, 0, floorY, width, 5, new Color(148, 151, 158));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sampleTexture.Dispose();
        _normalTexture.Dispose();
        _edgeTexture.Dispose();
        _rectangleFace.Dispose();
        _triangleFace.Dispose();
        _trapezoidFace.Dispose();
        _renderer.Dispose();
    }
}
