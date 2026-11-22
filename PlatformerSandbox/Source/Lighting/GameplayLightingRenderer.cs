using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Foundation.Presentation;
using PlatformerSandbox.Foundation.Presentation.Lighting;

namespace PlatformerSandbox.Lighting;

/// <summary>
/// Phase 4-6 production adapter. World terrain is rendered to an off-screen color
/// surface, followed by per-light visibility masks, then composed BEFORE hero,
/// translucent foreground art and UI. No simulation or collision ownership.
/// </summary>
internal sealed class GameplayLightingRenderer : IDisposable
{
    private readonly GraphicsDevice _device;
    private readonly Texture2D _falloff;
    private readonly Texture2D _posterizedFalloff;
    private readonly Texture2D _contactShadow;
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
    private RenderTarget2D? _world;
    private RenderTarget2D? _illumination;
    private RenderTarget2D? _individual;
    private RenderTarget2D? _visibility;
    private RenderTarget2D? _litLayer;
    private RenderTarget2D? _layeredCanvas;
    private RenderTarget2D? _basicAlbedoCanvas;
    private RenderTarget2D? _basicNormalMap;
    private RenderTarget2D? _basicEdgeMap;
    private readonly Effect? _basicSurfaceEffect;
    private bool _basicFrameActive;
    private LightingViewport _basicViewport;
    private LightRgb _basicAmbient;
    private IReadOnlyList<BasicMaterialLight>? _basicLights;
    private IReadOnlyList<BasicShadowShape>? _basicShadowShapes;
    // Additively expose ONLY occupied pixels of the current lit layer.
    // DestinationAlpha masks out transparent space so foreground layers do
    // not brighten the already-composited scenery behind them.
    private readonly BlendState _layerExposure = new()
    {
        ColorSourceBlend = Blend.SourceAlpha,
        ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
        ColorBlendFunction = BlendFunction.Add,
        AlphaBlendFunction = BlendFunction.Add
    };
    private readonly BlendState _multiplyKeepAlpha = new()
    {
        ColorSourceBlend = Blend.DestinationColor,
        ColorDestinationBlend = Blend.Zero,
        AlphaSourceBlend = Blend.Zero,
        AlphaDestinationBlend = Blend.One,
        ColorBlendFunction = BlendFunction.Add,
        AlphaBlendFunction = BlendFunction.Add
    };
    private readonly HashSet<string> _shadowQuota = new(StringComparer.Ordinal);
    private readonly HashSet<string> _shadowCounted = new(StringComparer.Ordinal);
    private bool _disposed;
    private bool _capturing;

    public Texture2D ContactShadowTexture => _contactShadow;
    public double LastSubmitMilliseconds { get; private set; }
    public int LastLights { get; private set; }
    public int LastShadowLights { get; private set; }
    public int LastShadowTriangles { get; private set; }
    public long Frames { get; private set; }
    public double AverageSubmitMilliseconds { get; private set; }
    public double PeakSubmitMilliseconds { get; private set; }
    public double EstimatedTargetsMegabytes => _world is null ? 0 :
        _world.Width * (double)_world.Height * 4 * 7 / (1024 * 1024);

    public GameplayLightingRenderer(GraphicsDevice device, Effect? basicSurfaceEffect = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        // Effect is owned by the host's ContentManager; renderer does not dispose it.
        _basicSurfaceEffect = basicSurfaceEffect;
        _falloff = MakeFalloff(false);
        _posterizedFalloff = MakeFalloff(true);
        _contactShadow = MakeContactShadow();
        _geometryEffect = new BasicEffect(device)
        {
            VertexColorEnabled = true,
            TextureEnabled = false,
            World = Matrix.Identity,
            View = Matrix.Identity
        };
    }

    private Texture2D MakeFalloff(bool stepped)
    {
        const int size = 128;
        var tex = new Texture2D(_device, size, size, false, SurfaceFormat.Color);
        var data = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = (x + .5 - size / 2.0) / (size / 2.0);
                double dy = (y + .5 - size / 2.0) / (size / 2.0);
                double t = Math.Max(0, 1 - Math.Sqrt(dx * dx + dy * dy));
                double intensity = t * t;
                if (stepped) intensity = Math.Round(intensity * 5) / 5;
                byte c = (byte)Math.Clamp((int)Math.Round(intensity * 255), 0, 255);
                data[y * size + x] = new Color(c, c, c, (byte)255);
            }
        tex.SetData(data);
        return tex;
    }

    private Texture2D MakeContactShadow()
    {
        const int size = 64;
        var tex = new Texture2D(_device, size, size, false, SurfaceFormat.Color);
        var data = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                double dx = (x + .5 - size * .5) / (size * .5);
                double dy = (y + .5 - size * .5) / (size * .5);
                double a = Math.Pow(Math.Max(0, 1 - dx * dx - dy * dy), 2);
                data[y * size + x] = new Color(0, 0, 0,
                    Math.Clamp((int)Math.Round(a * 110), 0, 255));
            }
        tex.SetData(data);
        return tex;
    }

    private void EnsureTargets(int w, int h)
    {
        if (_world is { IsDisposed: false } && _world.Width == w && _world.Height == h) return;
        _world?.Dispose(); _illumination?.Dispose(); _individual?.Dispose(); _visibility?.Dispose();
        _litLayer?.Dispose(); _layeredCanvas?.Dispose(); _basicAlbedoCanvas?.Dispose();
        _basicNormalMap?.Dispose(); _basicEdgeMap?.Dispose();
        _world = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _illumination = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _individual = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _visibility = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _litLayer = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _layeredCanvas = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _basicAlbedoCanvas = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _basicNormalMap = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _basicEdgeMap = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _geometryEffect.Projection = Matrix.CreateOrthographicOffCenter(0, w, h, 0, 0, 1);
    }

    public void BeginWorld()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_basicFrameActive) throw new InvalidOperationException("Finish basic material frame first.");
        int w = _device.PresentationParameters.BackBufferWidth;
        int h = _device.PresentationParameters.BackBufferHeight;
        EnsureTargets(w, h);
        _device.SetRenderTarget(_world);
        _device.Clear(Color.Black);
        _capturing = true;
    }

    /// <summary>One frame's shadow budget is shared across depth planes.</summary>
    public void BeginLayeredFrame(IReadOnlyList<RadialLight> lights, LightingFrameBudget budget)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _shadowQuota.Clear(); _shadowCounted.Clear();
        LastLights = lights.Count; LastShadowLights = 0; LastShadowTriangles = 0;
        LastSubmitMilliseconds = 0; Frames++;
        EnsureTargets(_device.PresentationParameters.BackBufferWidth,
            _device.PresentationParameters.BackBufferHeight);
        _device.SetRenderTarget(_layeredCanvas);
        _device.Clear(Color.Black);
        foreach (RadialLight light in lights)
            if (light.CastShadows && _shadowQuota.Count < budget.MaxShadowLights)
                _shadowQuota.Add(light.Id);
    }

    public void EndLayeredFrame(SpriteBatch batch)
    {
        if (_capturing) throw new InvalidOperationException("Cannot present while capturing a layer.");
        _device.SetRenderTarget(null);
        batch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.PointClamp);
        batch.Draw(_layeredCanvas!, new Rectangle(0, 0, _layeredCanvas!.Width,
            _layeredCanvas.Height), Color.White);
        batch.End();
        AverageSubmitMilliseconds += (LastSubmitMilliseconds - AverageSubmitMilliseconds) *
            (Frames == 1 ? 1 : .05);
    }

    /// <summary>
    /// Isolated basic lighting path. Reuses the production compositor's
    /// albedo, illumination, individual-light and final render targets.
    /// No shadow geometry, material maps, exposure multipliers or LDtk routing.
    /// SDR accumulation intentionally clamps once overlapping energy exceeds 1.
    /// </summary>
    public void ComposeBasic(SpriteBatch batch, LightingViewport viewport,
        LightRgb ambient, RadialLight? light, BasicLightingDebugView debugView) =>
        ComposeBasic(batch, viewport, ambient,
            light is null ? Array.Empty<RadialLight>() : new[] { light! },
            debugView, light?.Id);

    public void ComposeBasic(SpriteBatch batch, LightingViewport viewport,
        LightRgb ambient, IReadOnlyList<RadialLight> lights,
        BasicLightingDebugView debugView, string? selectedLightId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_capturing) throw new InvalidOperationException("BeginWorld must precede ComposeBasic.");
        _capturing = false;
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(lights);
        viewport.Validate();
        ambient.Validate(nameof(ambient));
        if (!Enum.IsDefined(debugView)) throw new ArgumentOutOfRangeException(nameof(debugView));
        foreach (RadialLight light in lights)
        {
            light.Validate();
            if (light.CastShadows)
                throw new ArgumentException("The basic lighting pass does not support shadows.", nameof(lights));
        }
        int width = _world!.Width, height = _world.Height;
        if (viewport.Width != width || viewport.Height != height)
            throw new ArgumentException("Lighting viewport and capture size must match.", nameof(viewport));
        Rectangle frame = new(0, 0, width, height);
        _device.SetRenderTarget(_illumination);
        _device.Clear(ToColor(ambient));
        if (lights.Count > 0)
        {
            batch.Begin(blendState: _additive, samplerState: SamplerState.LinearClamp);
            foreach (RadialLight light in lights)
                DrawBasicLight(batch, viewport, light);
            batch.End();
        }
        if (debugView == BasicLightingDebugView.SelectedLight)
        {
            _device.SetRenderTarget(_individual);
            _device.Clear(Color.Black);
            if (lights.Count > 0)
            {
                batch.Begin(blendState: _additive, samplerState: SamplerState.LinearClamp);
                foreach (RadialLight light in lights)
                    if (string.Equals(light.Id, selectedLightId, StringComparison.Ordinal))
                        DrawBasicLight(batch, viewport, light);
                batch.End();
            }
        }
        _device.SetRenderTarget(_litLayer);
        _device.Clear(Color.Transparent);
        batch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.PointClamp);
        batch.Draw(_world, frame, Color.White);
        batch.End();
        batch.Begin(blendState: _multiplyKeepAlpha, samplerState: SamplerState.PointClamp);
        batch.Draw(_illumination!, frame, Color.White);
        batch.End();

        _device.SetRenderTarget(null);
        Texture2D output = debugView switch
        {
            BasicLightingDebugView.Albedo => _world,
            BasicLightingDebugView.Illumination => _basicSurfaceEffect is null ? _illumination! : _individual!,
            BasicLightingDebugView.SelectedLight => _individual!,
            BasicLightingDebugView.ShadowMask => _individual!,
            _ => _litLayer!
        };
        batch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.PointClamp);
        batch.Draw(output, frame, Color.White);
        batch.End();
    }

    /// <summary>
    /// Stage-4/5/6 lab composition. Materials are submitted back-to-front.
    /// Each source image is rasterized once, then per-pixel GPU material/light/
    /// occlusion shading is applied (or bypassed for unlit material).
    /// This is an intentionally unoptimized reference, NOT the final batching plan.
    /// </summary>
    /// <summary>Legacy Stage-6 single-blocker entry point.</summary>
    public void BeginBasicMaterialFrame(LightingViewport viewport, LightRgb ambient,
        IReadOnlyList<BasicMaterialLight> lights, BasicShadowRectangle? blocker = null) =>
        BeginBasicMaterialFrameWithBlockers(viewport, ambient, lights,
            blocker.HasValue ? new[] { blocker.Value } : Array.Empty<BasicShadowRectangle>());

    /// <summary>Legacy Stage-7 rectangle-only entry point remains available.</summary>
    public void BeginBasicMaterialFrameWithBlockers(LightingViewport viewport, LightRgb ambient,
        IReadOnlyList<BasicMaterialLight> lights, IReadOnlyList<BasicShadowRectangle> blockers)
    {
        ArgumentNullException.ThrowIfNull(blockers);
        var shapes = new List<BasicShadowShape>(blockers.Count);
        foreach (BasicShadowRectangle rect in blockers)
            shapes.Add(new BasicShadowShape(rect, BasicShadowShapeKind.Rectangle));
        BeginBasicMaterialFrameWithShapes(viewport, ambient, lights, shapes);
    }

    /// <summary>Stage-8 bounded convex shadow geometry lab.</summary>
    public void BeginBasicMaterialFrameWithShapes(LightingViewport viewport, LightRgb ambient,
        IReadOnlyList<BasicMaterialLight> lights, IReadOnlyList<BasicShadowShape> blockers)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_basicFrameActive || _capturing)
            throw new InvalidOperationException("Finish the prior lighting capture first.");
        ArgumentNullException.ThrowIfNull(lights);
        ArgumentNullException.ThrowIfNull(blockers);
        if (blockers.Count > BasicLightingPreviewState.ShadowBlockerCount)
            throw new ArgumentOutOfRangeException(nameof(blockers), "Lab supports three shadow blockers.");
        viewport.Validate();
        ambient.Validate(nameof(ambient));
        foreach (BasicMaterialLight light in lights) light.Validate();
        foreach (BasicShadowShape blocker in blockers) blocker.Validate();
        if (blockers.Count > 0 && _basicSurfaceEffect is null)
            throw new InvalidOperationException("Hard shadows require L_BASIC_SurfaceLighting effect.");
        EnsureTargets(_device.PresentationParameters.BackBufferWidth,
            _device.PresentationParameters.BackBufferHeight);
        if (viewport.Width != _world!.Width || viewport.Height != _world.Height)
            throw new ArgumentException("Lighting viewport and target sizes must match.", nameof(viewport));
        _basicViewport = viewport;
        _basicAmbient = ambient;
        _basicLights = lights;
        _basicShadowShapes = new List<BasicShadowShape>(blockers);
        _basicFrameActive = true;
        _device.SetRenderTarget(_layeredCanvas);
        _device.Clear(Color.Transparent);
        _device.SetRenderTarget(_basicAlbedoCanvas);
        _device.Clear(Color.Transparent);
    }

    /// <summary>
    /// Submit one transparent or opaque surface in draw order. The draw delegate
    /// draws only albedo; the renderer owns Begin/End and targets. Opacity and
    /// lighting are applied once, AFTER all elements of a surface are rasterized.
    /// </summary>
    public void DrawBasicMaterial(SpriteBatch batch, BasicSurfaceMaterial material,
        Action<SpriteBatch> drawAlbedo,
        Action<SpriteBatch>? drawNormals = null,
        Action<SpriteBatch>? drawEdges = null,
        BasicWallLightOverlap? wallOverlap = null,
        BasicWallRimContext? wallRimContext = null,
        BasicShadowShape? wallRimShape = null,
        int wallRimShadowSlot = -1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_basicFrameActive) throw new InvalidOperationException("BeginBasicMaterialFrame required.");
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(drawAlbedo);
        material.Validate();
        if (_basicSurfaceEffect is null && wallOverlap is { } overlap &&
            (overlap.Light1Behind || overlap.Light2Behind))
            throw new InvalidOperationException("Behind-wall rim shading requires the basic material shader.");
        Rectangle frame = new(0, 0, _world!.Width, _world.Height);
        _device.SetRenderTarget(_world);
        _device.Clear(Color.Transparent);
        batch.Begin(blendState: BlendState.NonPremultiplied, samplerState: SamplerState.PointClamp);
        try { drawAlbedo(batch); }
        finally { batch.End(); }

        // The albedo debug view includes the same alpha and depth order as final.
        // Rasterized albedo here is premultiplied and composited only once.
        Color tint = Color.White * (float)material.Opacity;
        _device.SetRenderTarget(_basicAlbedoCanvas);
        batch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.PointClamp);
        batch.Draw(_world, frame, tint);
        batch.End();

        Texture2D shaded = _world;
        if (material.Mode == BasicSurfaceLightingMode.Lit)
        {
            if (_basicSurfaceEffect is not null)
            {
                // Stage-5: same GPU shader for Flat, Normal, Rim and Hybrid.
                // Neutral maps preserve Stage-4 flat materials and missing-map fallbacks.
                CaptureBasicSurfaceMap(batch, _basicNormalMap!, new Color(128, 128, 255), drawNormals);
                CaptureBasicSurfaceMap(batch, _basicEdgeMap!, new Color(128, 128, 128, 0), drawEdges);
                BindBasicSurfaceShader(material, wallOverlap: wallOverlap,
                    wallRimContext: wallRimContext, wallRimShape: wallRimShape,
                    wallRimShadowSlot: wallRimShadowSlot);
                _device.SetRenderTarget(_litLayer);
                _device.Clear(Color.Transparent);
                batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullNone, _basicSurfaceEffect);
                batch.Draw(_world, frame, Color.White);
                batch.End();
            }
            else
            {
                // Backward-compatible Stage-4 fallback for existing renderer callers.
                if (material.Shading != BasicSurfaceShadingMode.Flat)
                    throw new InvalidOperationException("Advanced surface lighting requires L_BASIC_SurfaceLighting effect.");
                RenderBasicMaterialLightBuffer(batch, material);
                _device.SetRenderTarget(_litLayer);
                _device.Clear(Color.Transparent);
                batch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.PointClamp);
                batch.Draw(_world, frame, Color.White);
                batch.End();
                batch.Begin(blendState: _multiplyKeepAlpha, samplerState: SamplerState.PointClamp);
                batch.Draw(_illumination!, frame, Color.White);
                batch.End();
            }
            shaded = _litLayer!;
        }
        _device.SetRenderTarget(_layeredCanvas);
        batch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.PointClamp);
        batch.Draw(shaded, frame, tint);
        batch.End();
    }

    private void CaptureBasicSurfaceMap(SpriteBatch batch, RenderTarget2D target,
        Color clear, Action<SpriteBatch>? draw)
    {
        _device.SetRenderTarget(target);
        _device.Clear(clear);
        if (draw is null) return;
        batch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.PointClamp);
        try { draw(batch); }
        finally { batch.End(); }
    }

    private void BindBasicSurfaceShader(BasicSurfaceMaterial material,
        BasicLightingDebugView debugView = BasicLightingDebugView.Final,
        string? selectedLightId = null,
        BasicWallLightOverlap? wallOverlap = null,
        BasicWallRimContext? wallRimContext = null,
        BasicShadowShape? wallRimShape = null,
        int wallRimShadowSlot = -1)
    {
        Effect effect = _basicSurfaceEffect!;
        effect.Parameters["SpriteTexture"].SetValue((Texture2D)_world!);
        effect.Parameters["NormalMap"].SetValue((Texture2D)_basicNormalMap!);
        effect.Parameters["EdgeMap"].SetValue((Texture2D)_basicEdgeMap!);
        effect.Parameters["ViewportPixels"].SetValue(new Vector2(_basicViewport.Width, _basicViewport.Height));
        effect.Parameters["AmbientRgb"].SetValue(new Vector3((float)_basicAmbient.R,
            (float)_basicAmbient.G, (float)_basicAmbient.B));
        effect.Parameters["ShadingMode"].SetValue((float)material.Shading);
        effect.Parameters["NormalStrength"].SetValue((float)material.NormalStrength);
        effect.Parameters["RimStrength"].SetValue((float)material.RimStrength);
        // Only the currently drawn intersected wall can opt into back-rim mode.
        // All other surfaces get zero overrides, avoiding global suppression.
        effect.Parameters["WallOverlapSettings"].SetValue(new Vector4(
            wallOverlap?.Light1Behind == true ? 1f : 0f,
            wallOverlap?.Light2Behind == true ? 1f : 0f,
            4.5f, 1.25f));
        effect.Parameters["WallRimContext"].SetValue(new Vector4(
            wallRimContext?.Light1Inside == true ? 1f : 0f,
            wallRimContext?.Light2Inside == true ? 1f : 0f,
            (float)((wallRimContext?.WallCenter.X ?? 0) * _basicViewport.Width),
            (float)((wallRimContext?.WallCenter.Y ?? 0) * _basicViewport.Height)));
        // Bind the exact visible polygon of THIS wall, including its sloped
        // edges. Never borrow a different slot's shadow geometry and never
        // retain a previous surface's parameters (cross-sprite rim leakage).
        Vector4 wallAB = Vector4.Zero, wallCD = Vector4.Zero;
        float wallVertexCount = 0;
        if (wallRimShape.HasValue)
        {
            LightPoint[] vertices = wallRimShape.Value.Vertices();
            Vector2 a = new((float)(vertices[0].X * _basicViewport.Width),
                (float)(vertices[0].Y * _basicViewport.Height));
            Vector2 b = new((float)(vertices[1].X * _basicViewport.Width),
                (float)(vertices[1].Y * _basicViewport.Height));
            Vector2 c = new((float)(vertices[2].X * _basicViewport.Width),
                (float)(vertices[2].Y * _basicViewport.Height));
            LightPoint last = vertices.Length == 4 ? vertices[3] : vertices[2];
            Vector2 d = new((float)(last.X * _basicViewport.Width),
                (float)(last.Y * _basicViewport.Height));
            wallAB = new Vector4(a.X, a.Y, b.X, b.Y);
            wallCD = new Vector4(c.X, c.Y, d.X, d.Y);
            wallVertexCount = vertices.Length;
        }
        effect.Parameters["WallRimPolygonAB"].SetValue(wallAB);
        effect.Parameters["WallRimPolygonCD"].SetValue(wallCD);
        effect.Parameters["WallRimPolygonCount"].SetValue(wallVertexCount);
        // Explicit compact shadow-slot identity; NEVER use polygon coordinates
        // to infer which caster is self (duplicate/overlapping walls are legal).
        if (wallRimShadowSlot < -1 || wallRimShadowSlot >= BasicLightingPreviewState.ShadowBlockerCount)
            throw new ArgumentOutOfRangeException(nameof(wallRimShadowSlot));
        if (wallRimShape.HasValue && (wallRimShadowSlot < 0 ||
            wallRimShadowSlot >= _basicShadowShapes!.Count ||
            !_basicShadowShapes[wallRimShadowSlot].Equals(wallRimShape.Value)))
            throw new ArgumentException("Wall rim shape must match its active shadow slot.", nameof(wallRimShadowSlot));
        effect.Parameters["WallRimSelfSlot"].SetValue((float)wallRimShadowSlot);
        // Bind all three polygon slots every draw to prevent stale GPU data.
        var blockers = _basicShadowShapes!;
        float[] counts = new float[BasicLightingPreviewState.ShadowBlockerCount];
        for (int i = 0; i < BasicLightingPreviewState.ShadowBlockerCount; i++)
        {
            Vector4 ab = Vector4.Zero, cd = Vector4.Zero;
            if (i < blockers.Count)
            {
                LightPoint[] vertices = blockers[i].Vertices();
                Vector2 a = new((float)(vertices[0].X * _basicViewport.Width),
                    (float)(vertices[0].Y * _basicViewport.Height));
                Vector2 b = new((float)(vertices[1].X * _basicViewport.Width),
                    (float)(vertices[1].Y * _basicViewport.Height));
                Vector2 c = new((float)(vertices[2].X * _basicViewport.Width),
                    (float)(vertices[2].Y * _basicViewport.Height));
                LightPoint last = vertices.Length == 4 ? vertices[3] : vertices[2];
                Vector2 d = new((float)(last.X * _basicViewport.Width),
                    (float)(last.Y * _basicViewport.Height));
                ab = new Vector4(a.X, a.Y, b.X, b.Y);
                cd = new Vector4(c.X, c.Y, d.X, d.Y);
                counts[i] = vertices.Length;
            }
            effect.Parameters[$"ShadowPoly{i}AB"].SetValue(ab);
            effect.Parameters[$"ShadowPoly{i}CD"].SetValue(cd);
        }
        effect.Parameters["ShadowVertexCounts"].SetValue(new Vector4(counts[0], counts[1], counts[2], 0));
        effect.Parameters["ShadowSlotEnable"].SetValue(new Vector4(
            blockers.Count > 0 ? 1 : 0,
            blockers.Count > 1 ? 1 : 0,
            blockers.Count > 2 ? 1 : 0, 0));
        float cast0 = 0, cast1 = 0;
        foreach (BasicMaterialLight configured in _basicLights!)
        {
            if (!configured.Light.CastShadows) continue;
            if (configured.Channel == BasicLightReceiverChannels.Light1) cast0 = 1;
            else if (configured.Channel == BasicLightReceiverChannels.Light2) cast1 = 1;
        }
        effect.Parameters["ShadowSettings"].SetValue(new Vector4(
            cast0, cast1, material.Mode == BasicSurfaceLightingMode.Lit &&
            material.ReceivesShadows ? 1 : 0, blockers.Count > 0 ? 1 : 0));
        effect.Parameters["DebugOutputMode"].SetValue(debugView switch
        {
            BasicLightingDebugView.ShadowMask => 1f,
            BasicLightingDebugView.Illumination => 2f,
            BasicLightingDebugView.SelectedLight => 3f,
            _ => 0f
        });
        // This is a lab with two stable channel IDs. No list-index remapping when
        // a light is disabled, so selecting Light 2 always inspects Light 2.
        effect.Parameters["DebugLightIndex"].SetValue(
            string.Equals(selectedLightId, "basics.2", StringComparison.Ordinal) ? 1f : 0f);
        for (int i = 0; i < 2; i++)
        {
            var position = new Vector4(0, 0, 1, 1);
            var colorIntensity = Vector4.Zero;
            foreach (BasicMaterialLight configured in _basicLights!)
            {
                if (configured.Channel != (i == 0
                    ? BasicLightReceiverChannels.Light1 : BasicLightReceiverChannels.Light2)) continue;
                RadialLight light = configured.Light;
                LightPoint center = _basicViewport.Project(light.Position);
                float radiusPixels = (float)(light.Radius * _basicViewport.PixelsPerUnit);
                position = new Vector4((float)center.X, (float)center.Y,
                    radiusPixels, radiusPixels * .25f);
                float energy = material.ReceivesLight(configured.Channel) ? (float)light.Intensity : 0f;
                colorIntensity = new Vector4((float)light.Color.R, (float)light.Color.G,
                    (float)light.Color.B, energy);
                break;
            }
            effect.Parameters[$"Light{i}PositionRadius"].SetValue(position);
            effect.Parameters[$"Light{i}ColorIntensity"].SetValue(colorIntensity);
        }
    }

    private void RenderBasicMaterialLightBuffer(SpriteBatch batch,
        BasicSurfaceMaterial material)
    {
        _device.SetRenderTarget(_illumination);
        _device.Clear(ToColor(_basicAmbient));
        if (material.Receives == BasicLightReceiverChannels.None) return;
        batch.Begin(blendState: _additive, samplerState: SamplerState.LinearClamp);
        foreach (BasicMaterialLight light in _basicLights!)
            if (material.ReceivesLight(light.Channel))
                DrawBasicLight(batch, _basicViewport, light.Light);
        batch.End();
    }

    public void EndBasicMaterialFrame(SpriteBatch batch, BasicLightingDebugView debugView,
        string? selectedLightId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_basicFrameActive) throw new InvalidOperationException("BeginBasicMaterialFrame required.");
        if (!Enum.IsDefined(debugView)) throw new ArgumentOutOfRangeException(nameof(debugView));
        ArgumentNullException.ThrowIfNull(batch);
        // All lab diagnostic buffers use the same GPU per-light visibility as
        // the Final view. Unlike the legacy CPU radial texture debug path,
        // they cannot show an unshadowed light while Final shows a shadow.
        if (debugView is BasicLightingDebugView.Illumination or
            BasicLightingDebugView.SelectedLight or BasicLightingDebugView.ShadowMask)
        {
            if (_basicSurfaceEffect is not null)
            {
                _device.SetRenderTarget(_world);
                _device.Clear(Color.White); // full-screen neutral debug surface
                var debugMaterial = new BasicSurfaceMaterial(
                    BasicSurfaceLightingMode.Lit, BasicLightReceiverChannels.Both, 1);
                BindBasicSurfaceShader(debugMaterial, debugView, selectedLightId);
                _device.SetRenderTarget(_individual);
                _device.Clear(Color.Black);
                Rectangle debugFrame = new(0, 0, _basicViewport.Width, _basicViewport.Height);
                batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp,
                    DepthStencilState.None, RasterizerState.CullNone, _basicSurfaceEffect);
                batch.Draw(_world!, debugFrame, Color.White);
                batch.End();
            }
            else
            {
                if (debugView == BasicLightingDebugView.ShadowMask)
                    throw new InvalidOperationException("Shadow-mask view requires the basic material shader.");
                RenderBasicMaterialLightBuffer(batch, new BasicSurfaceMaterial(
                    BasicSurfaceLightingMode.Lit, BasicLightReceiverChannels.Both, 1));
                if (debugView == BasicLightingDebugView.SelectedLight)
                {
                    _device.SetRenderTarget(_individual);
                    _device.Clear(Color.Black);
                    batch.Begin(blendState: _additive, samplerState: SamplerState.LinearClamp);
                    foreach (BasicMaterialLight light in _basicLights!)
                        if (string.Equals(light.Light.Id, selectedLightId, StringComparison.Ordinal))
                            DrawBasicLight(batch, _basicViewport, light.Light);
                    batch.End();
                }
            }
        }
        _device.SetRenderTarget(null);
        Rectangle frame = new(0, 0, _basicViewport.Width, _basicViewport.Height);
        Texture2D output = debugView switch
        {
            BasicLightingDebugView.Albedo => _basicAlbedoCanvas!,
            BasicLightingDebugView.Illumination => _basicSurfaceEffect is null ? _illumination! : _individual!,
            BasicLightingDebugView.SelectedLight => _individual!,
            BasicLightingDebugView.ShadowMask => _individual!,
            _ => _layeredCanvas!
        };
        batch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.PointClamp);
        batch.Draw(output, frame, Color.White);
        batch.End();
        _basicLights = null;
        _basicShadowShapes = null;
        _basicFrameActive = false;
    }

    private void DrawBasicLight(SpriteBatch batch, LightingViewport viewport, RadialLight light)
    {
        LightPoint center = viewport.Project(light.Position);
        double radius = light.Radius * viewport.PixelsPerUnit;
        var destination = new Rectangle(
            (int)Math.Round(center.X - radius), (int)Math.Round(center.Y - radius),
            Math.Max(1, (int)Math.Round(radius * 2)),
            Math.Max(1, (int)Math.Round(radius * 2)));
        foreach (LightRgb pass in LightingEnergyPasses.Split(light.Color, light.Intensity))
            batch.Draw(_falloff, destination, ToColor(pass));
    }

    public void BeginLayer()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureTargets(_device.PresentationParameters.BackBufferWidth,
            _device.PresentationParameters.BackBufferHeight);
        _device.SetRenderTarget(_world);
        _device.Clear(Color.Transparent);
        _capturing = true;
    }

    /// <summary>
    /// Premultiplied alpha is kept through multiplication, so an empty pixel on
    /// this layer can never darken or overwrite an already-composited lower layer.
    /// </summary>
    public void ComposeLayer(SpriteBatch batch, Camera2DView camera,
        IReadOnlyList<RadialLight> lights, IReadOnlyList<LightingOccluder> blockers,
        LightingArtPreset art, LightingFrameBudget budget, SceneLightingLayer layer)
    {
        if (!_capturing) throw new InvalidOperationException("BeginLayer required.");
        _capturing = false;
        art.Validate(); layer.Validate();
        var watch = Stopwatch.StartNew();
        int w = _world!.Width, h = _world.Height;
        LightingViewport viewport = View(camera);
        LightRgb ambient = art.Ambient * ((1 - art.AtmosphericDarkness) *
            layer.AmbientScale * art.AmbientScaleFor(layer.Id));
        _device.SetRenderTarget(_illumination);
        _device.Clear(ToColor(ambient.Clamp()));
        foreach (RadialLight light in lights)
        {
            if (!LightLayerRouting.Illuminates(light, layer)) continue;
            _device.SetRenderTarget(_individual);
            _device.Clear(Color.Black);
            LightPoint center = viewport.Project(light.Position);
            float radius = (float)(Math.Min(10000, light.Radius * art.LightRadiusScale) * viewport.PixelsPerUnit);
            // MonoGame Color clamps a tint channel to [0,1]. A single draw used
            // to discard any light gain above 1 BEFORE applying the falloff
            // texture, flattening dramatic lights on walls while the forward
            // sampled props received the full gain. Accumulate bounded unit
            // gains additively so the light buffer matches the CPU sampler.
            var lightPasses = LightingEnergyPasses.Split(light.Color,
                light.Intensity * art.LightMultiplier);
            if (lightPasses.Count > 0)
            {
                Rectangle destination = new(
                    (int)Math.Round(center.X - radius),
                    (int)Math.Round(center.Y - radius),
                    Math.Max(1, (int)Math.Round(radius * 2)),
                    Math.Max(1, (int)Math.Round(radius * 2)));
                batch.Begin(blendState: _additive, samplerState: SamplerState.LinearClamp);
                foreach (LightRgb rgb in lightPasses)
                    batch.Draw(art.Posterize ? _posterizedFalloff : _falloff,
                        destination, ToColor(rgb));
                batch.End();
            }
            if (light.CastShadows && layer.ReceivesShadows &&
                _shadowQuota.Contains(light.Id))
            {
                var relevant = new List<LightingOccluder>();
                foreach (LightingOccluder blocker in blockers)
                    if (LightLayerRouting.Occludes(light, blocker)) relevant.Add(blocker);
                if (relevant.Count > 0)
                {
                    ApplyVisibilityMask(batch, viewport,
                        light with { Radius = Math.Min(10000, light.Radius * art.LightRadiusScale) },
                        relevant, budget.ShadowRadialSamples);
                    if (_shadowCounted.Add(light.Id)) LastShadowLights++;
                }
            }
            _device.SetRenderTarget(_illumination);
            batch.Begin(blendState: _additive, samplerState: SamplerState.PointClamp);
            batch.Draw(_individual!, new Rectangle(0, 0, w, h), Color.White);
            batch.End();
        }
        _device.SetRenderTarget(_litLayer);
        _device.Clear(Color.Transparent);
        batch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.PointClamp);
        batch.Draw(_world, new Rectangle(0, 0, w, h), Color.White);
        batch.End();
        batch.Begin(blendState: _multiplyKeepAlpha, samplerState: SamplerState.PointClamp);
        batch.Draw(_illumination!, new Rectangle(0, 0, w, h), Color.White);
        batch.End();
        _device.SetRenderTarget(_layeredCanvas);
        Rectangle fullScreen = new(0, 0, w, h);
        batch.Begin(blendState: BlendState.AlphaBlend, samplerState: SamplerState.PointClamp);
        batch.Draw(_litLayer!, fullScreen, Color.White);
        batch.End();
        if (art.LightExposure > 1)
        {
            // Multiplicative SDR lighting cannot brighten a dark albedo above
            // its original RGB. Add bounded extra lit-color copies *only where
            // this layer has pixels*. This creates bright practical light pools
            // while near-black ambient regions remain near-black.
            double extra = art.LightExposure - 1;
            int fullCopies = (int)Math.Floor(extra);
            double remainder = extra - fullCopies;
            batch.Begin(blendState: _layerExposure, samplerState: SamplerState.PointClamp);
            for (int copy = 0; copy < fullCopies; copy++)
                batch.Draw(_litLayer!, fullScreen, Color.White);
            if (remainder > 1e-6)
            {
                float partial = (float)remainder;
                batch.Draw(_litLayer!, fullScreen,
                    new Color(partial, partial, partial, 1f));
            }
            batch.End();
        }
        watch.Stop();
        LastSubmitMilliseconds += watch.Elapsed.TotalMilliseconds;
        PeakSubmitMilliseconds = Math.Max(PeakSubmitMilliseconds, LastSubmitMilliseconds);
    }

    public void Compose(SpriteBatch batch, Camera2DView camera,
        IReadOnlyList<RadialLight> visible,
        IReadOnlyList<LightingOccluder> occluders, LightingArtPreset art,
        LightingFrameBudget budget)
    {
        if (!_capturing) throw new InvalidOperationException("BeginWorld required before lighting composition.");
        _capturing = false;
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentNullException.ThrowIfNull(occluders);
        art.Validate();
        var watch = Stopwatch.StartNew();
        int w = _world!.Width, h = _world.Height;
        LightingViewport viewport = View(camera);
        LastLights = visible.Count;
        LastShadowLights = 0;
        LastShadowTriangles = 0;
        LightRgb ambient = art.Ambient * (1 - art.AtmosphericDarkness);
        _device.SetRenderTarget(_illumination);
        _device.Clear(ToColor(ambient));
        foreach (RadialLight light in visible)
        {
            _device.SetRenderTarget(_individual);
            _device.Clear(Color.Black);
            LightPoint center = viewport.Project(light.Position);
            float radius = (float)(Math.Min(10000, light.Radius * art.LightRadiusScale) * viewport.PixelsPerUnit);
            // MonoGame Color clamps a tint channel to [0,1]. A single draw used
            // to discard any light gain above 1 BEFORE applying the falloff
            // texture, flattening dramatic lights on walls while the forward
            // sampled props received the full gain. Accumulate bounded unit
            // gains additively so the light buffer matches the CPU sampler.
            var lightPasses = LightingEnergyPasses.Split(light.Color,
                light.Intensity * art.LightMultiplier);
            if (lightPasses.Count > 0)
            {
                Rectangle destination = new(
                    (int)Math.Round(center.X - radius),
                    (int)Math.Round(center.Y - radius),
                    Math.Max(1, (int)Math.Round(radius * 2)),
                    Math.Max(1, (int)Math.Round(radius * 2)));
                batch.Begin(blendState: _additive, samplerState: SamplerState.LinearClamp);
                foreach (LightRgb rgb in lightPasses)
                    batch.Draw(art.Posterize ? _posterizedFalloff : _falloff,
                        destination, ToColor(rgb));
                batch.End();
            }
            if (light.CastShadows && budget.MaxShadowLights > 0)
            {
                ApplyVisibilityMask(batch, viewport,
                    light with { Radius = Math.Min(10000, light.Radius * art.LightRadiusScale) },
                    occluders, budget.ShadowRadialSamples);
                LastShadowLights++;
            }
            _device.SetRenderTarget(_illumination);
            batch.Begin(blendState: _additive, samplerState: SamplerState.PointClamp);
            batch.Draw(_individual!, new Rectangle(0, 0, w, h), Color.White);
            batch.End();
        }
        // World is drawn once; only the world/backdrop receiver is multiplied.
        // Characters, cast-shadow foreground props, transparent art, HUD and UI
        // are drawn *after* Compose by the caller, with their own light policies.
        _device.SetRenderTarget(null);
        batch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.PointClamp);
        batch.Draw(_world!, new Rectangle(0, 0, w, h), Color.White);
        batch.End();
        batch.Begin(blendState: _multiply, samplerState: SamplerState.PointClamp);
        batch.Draw(_illumination!, new Rectangle(0, 0, w, h), Color.White);
        batch.End();
        if (art.Bloom && art.BloomStrength > 0)
        {
            batch.Begin(blendState: _additive, samplerState: SamplerState.LinearClamp);
            foreach (RadialLight light in visible)
            {
                if (light.Intensity < .7) continue;
                LightPoint center = viewport.Project(light.Position);
                float radius = (float)(Math.Min(10000, light.Radius * art.LightRadiusScale) * viewport.PixelsPerUnit * .44);
                float weight = (float)Math.Clamp(art.BloomStrength * light.Intensity * .24, 0, .45);
                batch.Draw(_falloff,
                    new Rectangle((int)(center.X - radius), (int)(center.Y - radius),
                        Math.Max(1, (int)(radius * 2)), Math.Max(1, (int)(radius * 2))),
                    new Color((float)Math.Clamp(light.Color.R * weight, 0, 1),
                        (float)Math.Clamp(light.Color.G * weight, 0, 1),
                        (float)Math.Clamp(light.Color.B * weight, 0, 1)));
            }
            batch.End();
        }
        watch.Stop();
        LastSubmitMilliseconds = watch.Elapsed.TotalMilliseconds;
        PeakSubmitMilliseconds = Math.Max(PeakSubmitMilliseconds, LastSubmitMilliseconds);
        Frames++;
        // EWMA of CPU-side command submission; NOT a GPU timer.
        AverageSubmitMilliseconds += (LastSubmitMilliseconds - AverageSubmitMilliseconds) *
            (Frames == 1 ? 1 : .05);
    }

    private void ApplyVisibilityMask(SpriteBatch batch, LightingViewport view,
        RadialLight light, IReadOnlyList<LightingOccluder> occluders, int radialSamples)
    {
        IReadOnlyList<LightPoint> polygon = ShadowVisibility.BuildPolygon(light, occluders,
            radialSamples);
        if (polygon.Count < 3) return;
        var triangles = new VertexPositionColor[polygon.Count * 3];
        LightPoint center = view.Project(light.Position);
        Vector3 p = new((float)center.X, (float)center.Y, 0);
        for (int i = 0; i < polygon.Count; i++)
        {
            LightPoint a = view.Project(polygon[i]);
            LightPoint b = view.Project(polygon[(i + 1) % polygon.Count]);
            triangles[i * 3] = new VertexPositionColor(p, Color.White);
            triangles[i * 3 + 1] = new VertexPositionColor(new Vector3((float)a.X, (float)a.Y, 0), Color.White);
            triangles[i * 3 + 2] = new VertexPositionColor(new Vector3((float)b.X, (float)b.Y, 0), Color.White);
        }
        LastShadowTriangles += polygon.Count;
        _device.SetRenderTarget(_visibility);
        _device.Clear(Color.Black);
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        foreach (EffectPass pass in _geometryEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.TriangleList, triangles, 0, triangles.Length / 3);
        }
        _device.SetRenderTarget(_individual);
        batch.Begin(blendState: _multiply, samplerState: SamplerState.PointClamp);
        batch.Draw(_visibility!, new Rectangle(0, 0, view.Width, view.Height), Color.White);
        batch.End();
    }

    public void Abort()
    {
        _capturing = false;
        _basicFrameActive = false;
        _basicLights = null;
        _basicShadowShapes = null;
        _device.SetRenderTarget(null);
    }

    public static LightingViewport View(Camera2DView camera) => new(camera.ViewportWidth,
        camera.ViewportHeight,
        new LightPoint(camera.Left + camera.VisibleWidth * .5,
            camera.Bottom + camera.VisibleHeight * .5), camera.PixelsPerWorldUnit);

    public static Color ToColor(LightRgb rgb) => new((float)Math.Clamp(rgb.R, 0, 1),
        (float)Math.Clamp(rgb.G, 0, 1), (float)Math.Clamp(rgb.B, 0, 1));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _world?.Dispose(); _illumination?.Dispose(); _individual?.Dispose(); _visibility?.Dispose();
        _litLayer?.Dispose(); _layeredCanvas?.Dispose(); _basicAlbedoCanvas?.Dispose();
        _basicNormalMap?.Dispose(); _basicEdgeMap?.Dispose();
        _falloff.Dispose(); _posterizedFalloff.Dispose(); _contactShadow.Dispose();
        _geometryEffect.Dispose(); _additive.Dispose(); _multiply.Dispose();
        _multiplyKeepAlpha.Dispose(); _layerExposure.Dispose();
    }
}
