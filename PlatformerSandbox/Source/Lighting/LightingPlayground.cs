using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gyms;

namespace PlatformerSandbox.Lighting;

/// <summary>
/// L01 phases 1-3 development laboratory: light accumulation, a CPU normal-map
/// reference shader, and GPU hard-shadow projection. It never touches WorldSession.
/// </summary>
internal sealed class LightingPlayground : IDisposable
{
    private sealed class MaterialPreview : IDisposable
    {
        public required LightingMaterialDefinition Definition;
        public required int Width;
        public required int Height;
        public required Color[] Albedo;
        public Color[]? Normal;
        public Color[]? Emission;
        public Color[]? Mask;
        public required Texture2D Output;
        public required Color[] Buffer;
        public void Dispose() => Output.Dispose();
    }

    private readonly GraphicsDevice _device;
    private readonly LightingCatalog _catalog;
    private readonly string _folder;
    private readonly Texture2D _falloff;
    private readonly Texture2D _shadowBlob;
    private readonly BasicEffect _geometryEffect;
    private readonly BlendState _additive = new()
    {
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.One,
        ColorBlendFunction = BlendFunction.Add,
        AlphaBlendFunction = BlendFunction.Add
    };
    private readonly BlendState _multiply = new()
    {
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.Zero,
        ColorBlendFunction = BlendFunction.Add,
        AlphaBlendFunction = BlendFunction.Add
    };
    private RenderTarget2D? _sceneTarget;
    private RenderTarget2D? _lightTarget;
    private RenderTarget2D? _singleLightTarget;
    private LightWorld _lights = new(32);
    private LightingEnvironment _scene;
    private readonly List<MaterialPreview> _materials = new();
    private LightPoint _focus = new(0, 0);
    private double _pixelsPerUnit = 55;
    private KeyboardState _previousKeys;
    private int _selectedLight;
    private bool _normals = true;
    private bool _shadows = true;
    private bool _lightingEnabled = true;
    private bool _materialsDirty = true;
    private bool _showHelp = true;
    private int _bufferMode;
    private bool _disposed;
    private readonly Stopwatch _stopwatch = new();
    public bool ExitRequested { get; private set; }
    public int VisibleLights { get; private set; }
    public int ProjectedShadowQuads { get; private set; }
    public double LastFrameMilliseconds { get; private set; }
    public double LastMaterialMilliseconds { get; private set; }
    public string SceneId => _scene.Id;

    public LightingPlayground(GraphicsDevice device, string manifestPath)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _folder = Path.GetDirectoryName(manifestPath)
            ?? throw new ArgumentException("Lighting content folder missing.", nameof(manifestPath));
        _catalog = LightingCatalog.FromFile(manifestPath);
        _scene = _catalog.Get("CAVERN");
        _falloff = CreateFalloff(device);
        _shadowBlob = CreateShadowBlob(device);
        _geometryEffect = new BasicEffect(device)
        {
            VertexColorEnabled = true,
            TextureEnabled = false,
            World = Matrix.Identity,
            View = Matrix.Identity
        };
        try { ChangeScene("CAVERN"); }
        catch { Dispose(); throw; }
        _previousKeys = Keyboard.GetState();
    }

    private void ChangeScene(string id)
    {
        LightingEnvironment next = _catalog.Get(id);
        // Load all authored assets before replacing active scene materials.
        var nextMaterials = new List<MaterialPreview>();
        try
        {
            foreach (LightingMaterialDefinition definition in next.Materials)
                nextMaterials.Add(LoadMaterial(definition));
        }
        catch
        {
            foreach (MaterialPreview item in nextMaterials) item.Dispose();
            throw;
        }
        foreach (MaterialPreview item in _materials) item.Dispose();
        _materials.Clear();
        _materials.AddRange(nextMaterials);
        _scene = next;
        _lights.Clear();
        foreach (RadialLight light in _scene.Lights) _lights.TryAdd(light);
        _selectedLight = 0;
        _focus = new LightPoint(0, 0);
        _materialsDirty = true;
    }

    private MaterialPreview LoadMaterial(LightingMaterialDefinition definition)
    {
        Color[] Load(string filename, out int width, out int height)
        {
            using var stream = File.OpenRead(Path.Combine(_folder, "Textures", filename));
            using Texture2D texture = Texture2D.FromStream(_device, stream);
            width = texture.Width;
            height = texture.Height;
            var pixels = new Color[width * height];
            texture.GetData(pixels);
            return pixels;
        }
        Color[] albedo = Load(definition.Albedo, out int w, out int h);
        Color[]? Optional(string? name)
        {
            if (name is null) return null;
            Color[] result = Load(name, out int ow, out int oh);
            if (ow != w || oh != h)
                throw new ArgumentException($"Material '{definition.Id}': normal/emission/mask dimensions must equal albedo.");
            return result;
        }
        Color[]? normal = Optional(definition.Normal);
        Color[]? emission = Optional(definition.Emission);
        Color[]? mask = Optional(definition.Mask);
        return new MaterialPreview
        {
            Definition = definition,
            Width = w, Height = h,
            Albedo = albedo, Normal = normal, Emission = emission, Mask = mask,
            Output = new Texture2D(_device, w, h, false, SurfaceFormat.Color),
            Buffer = new Color[w * h]
        };
    }

    private static Texture2D CreateFalloff(GraphicsDevice device)
    {
        const int size = 128;
        var tex = new Texture2D(device, size, size, false, SurfaceFormat.Color);
        var data = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = (x + .5 - size / 2.0) / (size / 2.0);
                double dy = (y + .5 - size / 2.0) / (size / 2.0);
                double t = Math.Max(0, 1 - Math.Sqrt(dx * dx + dy * dy));
                byte b = (byte)Math.Clamp((int)Math.Round(t * t * 255), 0, 255);
                data[y * size + x] = new Color(b, b, b, (byte)255);
            }
        tex.SetData(data);
        return tex;
    }

    private static Texture2D CreateShadowBlob(GraphicsDevice device)
    {
        const int size = 64;
        var texture = new Texture2D(device, size, size, false, SurfaceFormat.Color);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double nx = (x + .5 - size / 2.0) / (size / 2.0);
                double ny = (y + .5 - size / 2.0) / (size / 2.0);
                double value = Math.Pow(Math.Max(0, 1 - nx * nx - ny * ny), 2);
                pixels[y * size + x] = new Color((byte)0, (byte)0, (byte)0,
                    (byte)Math.Clamp((int)Math.Round(value * 155), 0, 255));
            }
        texture.SetData(pixels);
        return texture;
    }

    private bool Pressed(KeyboardState keys, Keys key) =>
        keys.IsKeyDown(key) && _previousKeys.IsKeyUp(key);

    public void Update(double seconds, KeyboardState keys, MouseState mouse, int width, int height)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!double.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        double dt = Math.Min(.1, seconds);
        if (Pressed(keys, Keys.Escape)) ExitRequested = true;
        if (Pressed(keys, Keys.F1)) ChangeScene("CAVERN");
        if (Pressed(keys, Keys.F2)) ChangeScene("ATRIUM");
        if (Pressed(keys, Keys.D1)) { _lightingEnabled = !_lightingEnabled; _materialsDirty = true; }
        if (Pressed(keys, Keys.D2)) { _normals = !_normals; _materialsDirty = true; }
        if (Pressed(keys, Keys.D3)) { _shadows = !_shadows; _materialsDirty = true; }
        if (Pressed(keys, Keys.D4)) _bufferMode = (_bufferMode + 1) % 3;
        if (Pressed(keys, Keys.H)) _showHelp = !_showHelp;
        if (Pressed(keys, Keys.Tab) && _lights.Count > 0)
            _selectedLight = (_selectedLight + 1) % _lights.Count;
        if (Pressed(keys, Keys.R)) ChangeScene(_scene.Id);

        double panX = (keys.IsKeyDown(Keys.D) ? 1 : 0) - (keys.IsKeyDown(Keys.A) ? 1 : 0);
        double panY = (keys.IsKeyDown(Keys.W) ? 1 : 0) - (keys.IsKeyDown(Keys.S) ? 1 : 0);
        _focus = new LightPoint(_focus.X + panX * dt * 4.5, _focus.Y + panY * dt * 4.5);
        double zoom = (keys.IsKeyDown(Keys.E) ? 1 : 0) - (keys.IsKeyDown(Keys.Q) ? 1 : 0);
        _pixelsPerUnit = Math.Clamp(_pixelsPerUnit * Math.Exp(zoom * dt * 1.3), 24, 105);
        if (mouse.LeftButton == ButtonState.Pressed && _lights.Count > 0 && width > 0 && height > 0)
        {
            LightingViewport view = new(width, height, _focus, _pixelsPerUnit);
            RadialLight selected = _lights.Lights[_selectedLight];
            _lights.Replace(selected.Id, selected with
            {
                Position = view.Unproject(new LightPoint(mouse.X, mouse.Y))
            });
            _materialsDirty = true;
        }
        _previousKeys = keys;
    }

    private void EnsureTargets(int width, int height)
    {
        if (_sceneTarget is not null && _sceneTarget.Width == width && _sceneTarget.Height == height) return;
        _device.SetRenderTarget(null);
        _sceneTarget?.Dispose();
        _lightTarget?.Dispose();
        _singleLightTarget?.Dispose();
        _sceneTarget = new RenderTarget2D(_device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
        _lightTarget = new RenderTarget2D(_device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
        _singleLightTarget = new RenderTarget2D(_device, width, height, false, SurfaceFormat.Color, DepthFormat.None);
        _geometryEffect.Projection = Matrix.CreateOrthographicOffCenter(0, width, height, 0, 0, 1);
    }

    private static Color ToColor(LightRgb rgb) => new((float)Math.Clamp(rgb.R, 0, 1),
        (float)Math.Clamp(rgb.G, 0, 1), (float)Math.Clamp(rgb.B, 0, 1));
    private static LightRgb ToRgb(Color c) => new(c.R / 255.0, c.G / 255.0, c.B / 255.0);

    public void Draw(SpriteBatch batch, Texture2D pixel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int width = _device.Viewport.Width, height = _device.Viewport.Height;
        if (width <= 0 || height <= 0) return;
        EnsureTargets(width, height);
        LightingViewport view = new(width, height, _focus, _pixelsPerUnit);
        IReadOnlyList<RadialLight> visible = _lightingEnabled
            ? _lights.Visible(view, 12) : Array.Empty<RadialLight>();
        VisibleLights = visible.Count;
        ProjectedShadowQuads = 0;
        _stopwatch.Restart();

        // L01 P1: scene color and illumination are distinct render targets.
        _device.SetRenderTarget(_sceneTarget);
        _device.Clear(new Color(15, 22, 35));
        batch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.PointClamp);
        DrawBackdrop(batch, pixel, view);
        batch.End();

        _device.SetRenderTarget(_lightTarget);
        _device.Clear(_lightingEnabled ? ToColor(_scene.Ambient) : Color.White);
        if (_lightingEnabled)
            foreach (RadialLight light in visible)
            {
                // Each shadow-casting light uses a temporary light surface so
                // blocker silhouettes remove ONLY that light, not neighboring lights or ambient.
                _device.SetRenderTarget(_singleLightTarget);
                _device.Clear(Color.Black);
                batch.Begin(blendState: _additive, samplerState: SamplerState.LinearClamp);
                LightPoint p = view.Project(light.Position);
                float r = (float)(light.Radius * view.PixelsPerUnit);
                var tint = new Color(
                    (float)Math.Clamp(light.Color.R * light.Intensity, 0, 1),
                    (float)Math.Clamp(light.Color.G * light.Intensity, 0, 1),
                    (float)Math.Clamp(light.Color.B * light.Intensity, 0, 1));
                batch.Draw(_falloff, new Rectangle((int)Math.Round(p.X - r),
                    (int)Math.Round(p.Y - r), Math.Max(1, (int)Math.Round(r * 2)),
                    Math.Max(1, (int)Math.Round(r * 2))), tint);
                batch.End();
                if (_shadows && light.CastShadows)
                    DrawProjectedShadows(light, view);
                _device.SetRenderTarget(_lightTarget);
                batch.Begin(blendState: _additive, samplerState: SamplerState.PointClamp);
                batch.Draw(_singleLightTarget!, new Rectangle(0, 0, width, height), Color.White);
                batch.End();
            }

        // L01 P2: material preview is a forward path with optional normal,
        // emission, and influence mask assets. The reference evaluator runs on CPU
        // so it works without an external MGFX shader compiler.
        if (_materialsDirty)
        {
            Stopwatch matWatch = Stopwatch.StartNew();
            // The material/light world positions have not changed when just
            // moving the camera. Avoid re-uploading CPU reference maps each frame.
            IReadOnlyList<RadialLight> materialLights = _lightingEnabled
                ? _lights.Lights : Array.Empty<RadialLight>();
            foreach (MaterialPreview material in _materials)
                UpdateMaterialTexture(material, _lightingEnabled, _normals, materialLights);
            matWatch.Stop();
            LastMaterialMilliseconds = matWatch.Elapsed.TotalMilliseconds;
            _materialsDirty = false;
        }

        _device.SetRenderTarget(null);
        _device.Clear(Color.Black);
        batch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.PointClamp);
        batch.Draw(_bufferMode == 1 ? _sceneTarget! : _bufferMode == 2 ? _lightTarget! : _sceneTarget!,
            new Rectangle(0, 0, width, height), Color.White);
        batch.End();
        if (_bufferMode == 0)
        {
            // Source light map multiplied by destination scene RGB.
            batch.Begin(blendState: _multiply, samplerState: SamplerState.PointClamp);
            batch.Draw(_lightTarget!, new Rectangle(0, 0, width, height), Color.White);
            batch.End();
        }
        if (_bufferMode != 2)
        {
            // Occluders are real visible wall/column surfaces, NOT just outlines.
            // Draw them AFTER the backdrop shadow receiver has been composited:
            // a wall must not receive its own projected shadow. The demonstration
            // material panels are drawn on top as the nearest artist-authored layer.
            DrawVisibleOccluderSurfaces(view, visible);
            batch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.PointClamp);
            DrawVisibleOccluderEdges(batch, pixel, view, visible);
            DrawDecorativeContactShadows(batch, view);
            DrawMaterials(batch, view);
            batch.End();
        }
        // Light gizmos and HUD remain deliberately unlit (forward/UI).
        batch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.PointClamp);
        DrawDebugGizmos(batch, pixel, view);
        batch.End();
        _stopwatch.Stop();
        LastFrameMilliseconds = _stopwatch.Elapsed.TotalMilliseconds;
    }

    private void DrawBackdrop(SpriteBatch batch, Texture2D pixel, LightingViewport view)
    {
        int w = view.Width, h = view.Height;
        batch.Draw(pixel, new Rectangle(0, 0, w, h), new Color(77, 96, 113));
        // Tiles illustrate light attenuation; all are visual, not physics geometry.
        for (int gx = -18; gx <= 18; gx++)
            for (int gy = -9; gy <= 9; gy++)
            {
                LightPoint pt = view.Project(new LightPoint(gx, gy));
                int px = (int)Math.Round(pt.X), py = (int)Math.Round(pt.Y);
                if (px < -100 || px > w + 100 || py < -100 || py > h + 100) continue;
                batch.Draw(pixel, new Rectangle(px, py, 2, 2), new Color(167, 175, 176) * .4f);
            }
        DrawWorldLine(batch, pixel, view, new LightPoint(-20, -1.2),
            new LightPoint(20, -1.2), 7, new Color(100, 118, 132));
        // Solid occluding geometry is rendered in a forward wall pass after the
        // backdrop light map, not baked into this shadow-receiving background.
    }

    private void DrawWorldLine(SpriteBatch batch, Texture2D pixel, LightingViewport view,
        LightPoint a, LightPoint b, float thickness, Color color)
    {
        LightPoint s = view.Project(a), e = view.Project(b);
        float dx = (float)(e.X - s.X), dy = (float)(e.Y - s.Y);
        float length = MathF.Sqrt(dx * dx + dy * dy);
        if (length < .01f) return;
        batch.Draw(pixel, new Vector2((float)s.X, (float)s.Y), null, color,
            MathF.Atan2(dy, dx), new Vector2(0, .5f),
            new Vector2(length, thickness), SpriteEffects.None, 0);
    }

    private void DrawProjectedShadows(RadialLight light, LightingViewport view)
    {
        var triangles = new List<VertexPositionColor>();
        foreach (LightingOccluder blocker in _scene.Occluders)
        {
            // Skip blockers well outside the radial influence. No global
            // partition yet: the bounded laboratory has <= 64 authored hulls.
            if (!blocker.Vertices.Any(p =>
                ShadowDistance(p, light.Position) <= light.Radius * 1.5)) continue;
            // The opaque hull and extruded shadow wedges form ONE blocker mask.
            // Without the hull fill, adjacent wedges leave pinholes and thin light
            // leaks at sharp/concave corners as the light moves at subpixel offsets.
            if (ShadowGeometry.Contains(blocker, light.Position)) continue;
            foreach (LightTriangle triangle in ShadowGeometry.Triangulate(blocker))
                AddTriangle(triangle.A, triangle.B, triangle.C);
            foreach (ShadowQuad q in ShadowGeometry.Project(light, blocker))
            {
                ProjectedShadowQuads++;
                AddTriangle(q.A, q.B, q.FarB);
                AddTriangle(q.A, q.FarB, q.FarA);
            }
        }
        if (triangles.Count == 0) return;
        VertexPositionColor[] vertices = triangles.ToArray();
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        foreach (EffectPass pass in _geometryEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.TriangleList, vertices,
                0, vertices.Length / 3);
        }
        return;

        void AddTriangle(LightPoint a, LightPoint b, LightPoint c)
        {
            triangles.Add(new VertexPositionColor(ToVector(a), Color.Black));
            triangles.Add(new VertexPositionColor(ToVector(b), Color.Black));
            triangles.Add(new VertexPositionColor(ToVector(c), Color.Black));
        }
        Vector3 ToVector(LightPoint pt)
        {
            LightPoint s = view.Project(pt);
            return new Vector3((float)s.X, (float)s.Y, 0);
        }
    }

    /// <summary>
    /// The visible face of a wall is a separate, foreground lighting receiver.
    /// It must be drawn after the backdrop light/shadow mask; otherwise the very
    /// shadow cast by the wall erases the light on the wall's own front surface.
    /// This is a lab proxy surface, not a depth-aware production lighting pass.
    /// </summary>
    private void DrawVisibleOccluderSurfaces(LightingViewport view,
        IReadOnlyList<RadialLight> visible)
    {
        var vertices = new List<VertexPositionColor>();
        foreach (LightingOccluder blocker in _scene.Occluders)
            foreach (LightTriangle triangle in ShadowGeometry.Triangulate(blocker))
                AddSubdivided(triangle.A, triangle.B, triangle.C, 0);
        if (vertices.Count == 0) return;
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        VertexPositionColor[] mesh = vertices.ToArray();
        foreach (EffectPass pass in _geometryEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.TriangleList, mesh,
                0, mesh.Length / 3);
        }
        return;

        void AddSubdivided(LightPoint a, LightPoint b, LightPoint c, int depth)
        {
            // Tessellation is needed for vertex-colored radial lighting: a tall
            // column's center must illuminate even when all four outer corners
            // lie far from the light. This is only a lab proxy mesh.
            double ab = ShadowGeometry.DistanceSquared(a, b);
            double bc = ShadowGeometry.DistanceSquared(b, c);
            double ca = ShadowGeometry.DistanceSquared(c, a);
            if (depth >= 7 || Math.Max(ab, Math.Max(bc, ca)) <= .65 * .65)
            {
                Add(a); Add(b); Add(c);
                return;
            }
            if (ab >= bc && ab >= ca)
            {
                LightPoint midpoint = new((a.X + b.X) * .5, (a.Y + b.Y) * .5);
                AddSubdivided(a, midpoint, c, depth + 1);
                AddSubdivided(midpoint, b, c, depth + 1);
            }
            else if (bc >= ca)
            {
                LightPoint midpoint = new((b.X + c.X) * .5, (b.Y + c.Y) * .5);
                AddSubdivided(a, b, midpoint, depth + 1);
                AddSubdivided(a, midpoint, c, depth + 1);
            }
            else
            {
                LightPoint midpoint = new((c.X + a.X) * .5, (c.Y + a.Y) * .5);
                AddSubdivided(a, b, midpoint, depth + 1);
                AddSubdivided(midpoint, b, c, depth + 1);
            }
        }

        void Add(LightPoint point)
        {
            LightPoint s = view.Project(point);
            vertices.Add(new VertexPositionColor(new Vector3((float)s.X, (float)s.Y, 0),
                ShadeStone(point, visible, 1f)));
        }
    }

    private void DrawVisibleOccluderEdges(SpriteBatch batch, Texture2D pixel,
        LightingViewport view, IReadOnlyList<RadialLight> visible)
    {
        foreach (LightingOccluder blocker in _scene.Occluders)
        {
            for (int i = 0; i < blocker.Vertices.Length; i++)
            {
                LightPoint a = blocker.Vertices[i];
                LightPoint b = blocker.Vertices[(i + 1) % blocker.Vertices.Length];
                LightPoint mid = new((a.X + b.X) * .5, (a.Y + b.Y) * .5);
                DrawWorldLine(batch, pixel, view, a, b, 5, ShadeStone(mid, visible, .67f));
            }
        }
    }

    private Color ShadeStone(LightPoint point, IReadOnlyList<RadialLight> visible, float shade)
    {
        // Wall faces receive diffuse-like 2D light independent of their own
        // shadow projection. The global shadow mask is only for the backdrop.
        LightRgb illumination = _lightingEnabled ? _scene.Ambient : LightRgb.White;
        if (_lightingEnabled)
            foreach (RadialLight light in visible)
                if ((light.Channels & LightChannel.Terrain) != 0)
                    illumination += light.Color * RadialLighting.Attenuation(light, point);
        return new Color(
            (float)Math.Clamp(illumination.R * .50 * shade, 0, 1),
            (float)Math.Clamp(illumination.G * .57 * shade, 0, 1),
            (float)Math.Clamp(illumination.B * .62 * shade, 0, 1));
    }

    private static double ShadowDistance(LightPoint a, LightPoint b)
    {
        double dx = a.X - b.X, dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void UpdateMaterialTexture(MaterialPreview preview, bool lit, bool normals,
        IReadOnlyList<RadialLight> lights)
    {
        var def = preview.Definition;
        int w = preview.Width, h = preview.Height;
        double rotation = def.RotationDegrees * Math.PI / 180;
        double cos = Math.Cos(rotation), sin = Math.Sin(rotation);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                double lx = ((x + .5) / w - .5) * def.Width * (def.FlipX ? -1 : 1);
                double ly = (.5 - (y + .5) / h) * def.Height;
                LightPoint point = new(def.Center.X + lx * cos - ly * sin,
                    def.Center.Y + lx * sin + ly * cos);
                LightRgb rgb = LightingMaterialSampler.Shade(
                    lit ? def : def with { Mode = LightingMaterialMode.Unlit },
                    ToRgb(preview.Albedo[i]), normals && preview.Normal is not null
                        ? ToRgb(preview.Normal[i]) : null,
                    preview.Emission is null ? null : ToRgb(preview.Emission[i]),
                    preview.Mask is null ? 1 : preview.Mask[i].R / 255.0,
                    point, _scene.Ambient, lights, _shadows ? _scene.Occluders : null);
                preview.Buffer[i] = ToColor(rgb);
            }
        preview.Output.SetData(preview.Buffer);
    }

    private void DrawDecorativeContactShadows(SpriteBatch batch, LightingViewport view)
    {
        // P3 cheap artist-authored contact/blob shadow; independent of geometric
        // light occlusion and intentionally layered beneath forward materials.
        foreach (MaterialPreview preview in _materials)
        {
            LightPoint p = view.Project(new LightPoint(preview.Definition.Center.X,
                preview.Definition.Center.Y - preview.Definition.Height * .58));
            int width = Math.Max(1, (int)(preview.Definition.Width * view.PixelsPerUnit * 1.05));
            int height = Math.Max(1, (int)(view.PixelsPerUnit * .35));
            batch.Draw(_shadowBlob, new Rectangle((int)(p.X - width / 2.0),
                (int)(p.Y - height / 2.0), width, height), Color.White);
        }
    }

    private void DrawMaterials(SpriteBatch batch, LightingViewport view)
    {
        foreach (MaterialPreview preview in _materials)
        {
            var m = preview.Definition;
            LightPoint p = view.Project(m.Center);
            batch.Draw(preview.Output, new Vector2((float)p.X, (float)p.Y), null,
                Color.White, (float)(-m.RotationDegrees * Math.PI / 180),
                new Vector2(preview.Width / 2f, preview.Height / 2f),
                new Vector2((float)(m.Width * view.PixelsPerUnit / preview.Width),
                    (float)(m.Height * view.PixelsPerUnit / preview.Height)),
                m.FlipX ? SpriteEffects.FlipHorizontally : SpriteEffects.None, 0);
        }
    }

    private void DrawDebugGizmos(SpriteBatch batch, Texture2D pixel, LightingViewport view)
    {
        for (int i = 0; i < _lights.Count; i++)
        {
            RadialLight l = _lights.Lights[i];
            LightPoint p = view.Project(l.Position);
            Color color = ToColor(l.Color);
            int sx = (int)Math.Round(p.X), sy = (int)Math.Round(p.Y);
            batch.Draw(pixel, new Rectangle(sx - 5, sy - 5, 10, 10), color);
            if (i == _selectedLight)
            {
                batch.Draw(pixel, new Rectangle(sx - 8, sy - 8, 16, 2), Color.White);
                batch.Draw(pixel, new Rectangle(sx - 8, sy + 6, 16, 2), Color.White);
            }
        }
        if (!_showHelp) return;
        int w = view.Width;
        batch.Draw(pixel, new Rectangle(9, 8, Math.Max(1, Math.Min(w - 18, 920)), 120),
            new Color(6, 12, 25) * .91f);
        int x = 20;
        GymDebugText.Draw(batch, pixel, x, 16,
            "L01 PHASES 1-3 - " + _scene.Id + " - LIGHTING LAB", Color.White, 2);
        GymDebugText.Draw(batch, pixel, x, 37,
            "F1/F2 SCENE  WASD PAN  Q/E ZOOM  CLICK MOVE LIGHT  TAB SELECT", Color.LightSkyBlue, 1);
        GymDebugText.Draw(batch, pixel, x, 53,
            "1 LIGHT " + (_lightingEnabled ? "ON" : "OFF") +
            "  2 NORMAL " + (_normals ? "ON" : "OFF") +
            "  3 SHADOW " + (_shadows ? "ON" : "OFF") +
            "  4 BUFFER " + _bufferMode, Color.LightSkyBlue, 1);
        GymDebugText.Draw(batch, pixel, x, 69,
            "R RESET  H HELP  ESC EXIT  WHITE BOX=SELECTED LIGHT", Color.LightSkyBlue, 1);
        GymDebugText.Draw(batch, pixel, x, 85,
            "VISIBLE " + VisibleLights + "  SHADOW QUADS " + ProjectedShadowQuads +
            "  FRAME MS " + LastFrameMilliseconds.ToString("F2") +
            "  MATERIAL MS " + LastMaterialMilliseconds.ToString("F2"), Color.LightGreen, 1);
        GymDebugText.Draw(batch, pixel, x, 102,
            "PANELS: NEUTRAL / NORMALS / ROTATED FLIPPED / EMISSION+MASK", Color.LightGray, 1);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _device.SetRenderTarget(null);
        foreach (MaterialPreview item in _materials) item.Dispose();
        _materials.Clear();
        _sceneTarget?.Dispose();
        _lightTarget?.Dispose();
        _singleLightTarget?.Dispose();
        _falloff.Dispose();
        _shadowBlob.Dispose();
        _geometryEffect.Dispose();
        _additive.Dispose();
        _multiply.Dispose();
    }
}
