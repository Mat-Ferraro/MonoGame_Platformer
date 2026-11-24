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
    // Separate full-screen per-light visibility masks for production material
    // surfaces. Reused between draws, never sampled while bound as targets.
    private RenderTarget2D? _productionMask0;
    private RenderTarget2D? _productionMask1;
    // Phase 17B: one lazily allocated scratch mask for separable Gaussian blur.
    // Never sampled while bound; HARD mode allocates no additional GPU memory.
    private RenderTarget2D? _softBlurScratch;
    // Phase 19: floating-point scene accumulation (when supported) and a
    // half-size two-target bloom chain. All resources are renderer-owned.
    private RenderTarget2D? _l19BloomA;
    private RenderTarget2D? _l19BloomB;
    // L20D: three-scale bloom pyramid (half, quarter, eighth). Scratch pairs
    // reused; never sample a target while it is bound for writing.
    private RenderTarget2D? _l20BloomMidA, _l20BloomMidB;
    private RenderTarget2D? _l20BloomWideA, _l20BloomWideB;
    public float BloomScatter { get; set; } = .65f;
    public bool BloomOnly { get; set; }
    public int LastDirectionalLights { get; private set; }
    public int LastSpotLights { get; private set; }
    public int LastTubeLights { get; private set; }
    private RenderTarget2D? _l19EmissionMap;
    private readonly Texture2D _l19WhiteEmissionMask;
    private bool _l19HdrUnavailable;
    private bool _l19HdrAllocated;
    private bool _l19LastHdrRequest;
    public bool HdrEnabled { get; set; }
    public bool HdrActive => _l19HdrAllocated;
    public bool HdrUnsupported => _l19HdrUnavailable;
    public bool EmissionEnabled { get; set; } = true;
    public bool BloomEnabled { get; set; }
    public float PostExposure { get; set; } = 1.15f;
    public float BloomThreshold { get; set; } = 1.05f;
    public float BloomStrength { get; set; } = 0.45f;
    public int LastEmissiveSurfaces { get; private set; }
    public double LastPostProcessCpuMilliseconds { get; private set; }
    public int LastBloomPasses { get; private set; }
    public long LastBloomPixels { get; private set; }

    // Phase 17C: a lazily allocated, 4KB polar first-hit distance profile.
    // Rebuilt only for actual, contact-hardened visibility masks, based on the
    // SAME fan as the corresponding hard mask (including self exclusion).
    private Texture2D? _contactHitProfile;
    private readonly ushort[] _contactDistances =
        new ushort[ShadowContactHardening.ProfileResolution];
    private readonly Color[] _contactPackedPixels =
        new Color[ShadowContactHardening.ProfileResolution];
    private bool _contactReady;
    private Vector2 _contactCenterPixels;
    private float _contactReachPixels;
    private float _contactEmitterPixels;
    // Stage 16B: immutable one-texel fallback resources. These are shader input
    // textures, never render targets; equivalent to the old full-screen clears.
    private readonly Texture2D _neutralProductionNormal;
    private readonly Texture2D _neutralProductionEdge;
    private readonly Texture2D _whiteProductionVisibility;
    // Shader pixels are clipped to the authored artwork, but UVs remain in
    // full-viewport coordinates for per-light visibility and rim alignment.
    private readonly RasterizerState _productionMaterialScissor = new()
    {
        CullMode = CullMode.None,
        ScissorTestEnable = true
    };
    private int _productionRadialSamples = 128;
    private readonly Effect? _basicSurfaceEffect;
    private readonly Effect? _polygonRimEffect;
    private Texture2D? _polygonEdges;
    private readonly Vector4[] _polygonEdgeData = new Vector4[ProductionWallLightRouting.MaxOwnerEdges * 2];
    private readonly BlendState _materialAdditive = new()
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One
    };
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
    /// <summary>Stage 15B CPU-side instrumentation, not GPU timing.</summary>
    public int LastMaterialSurfaces { get; private set; }
    public int LastMaterialLightsDropped { get; private set; }
    // Phase 18: per-frame production contributions (not unique scene lights).
    public int LastMaterialLightsEvaluated { get; private set; }
    public int LastMaterialLightPasses { get; private set; }
    public int LastMaterialAmbientPasses { get; private set; }
    public int LastMaterialMaxLightsOnSurface { get; private set; }
    public ProductionMaterialLightMode MaterialLightMode { get; set; } =
        ProductionMaterialLightMode.AllEligible;
    public int LastMaterialVisibilityMasks { get; private set; }
    public int LastSoftLayerMasks { get; private set; }
    public int LastSoftMaterialMasks { get; private set; }
    public int LastContactProfilesBuilt { get; private set; }
    public double LastContactProfileCpuMilliseconds { get; private set; }
    public long LastContactProfileBytesUploaded { get; private set; }
    public int LastSoftFilterPasses { get; private set; }
    public long LastSoftFilterPixels { get; private set; }
    public ShadowFilterMode FilterMode { get; set; } = ShadowFilterMode.UniformGaussian;
    public IReadOnlyDictionary<string, float> ShadowEmitterOverrides { get; set; } =
        new Dictionary<string, float>(StringComparer.Ordinal);
    private float _shadowSoftnessPixels;
    /// <summary>Per-frame screen-space penumbra width (0, 2, 4 or 8 px).</summary>
    public float ShadowSoftnessPixels
    {
        get => _shadowSoftnessPixels;
        set
        {
            if (value is not (0f or 2f or 4f or 8f))
                throw new ArgumentOutOfRangeException(nameof(value),
                    "Choose a bounded shadow edge preset (0, 2, 4, 8 pixels).");
            _shadowSoftnessPixels = value;
        }
    }
    public int LastMaterialBackRimSurfaces { get; private set; }
    // Stage 15C: CPU command-submission / visibility *geometry* timing only.
    // None of these measurements claims to measure GPU execution or FPS.
    private readonly LightingCpuTimeWindow _cpuWindow = new(120);
    public LightingCpuTimeWindow CpuSubmitWindow => _cpuWindow;
    public double LastLayerCpuMilliseconds { get; private set; }
    public double LastMaterialCpuMilliseconds { get; private set; }
    public double LastMaterialMaskGeometryCpuMilliseconds { get; private set; }
    public double LastLayerMaskGeometryCpuMilliseconds { get; private set; }
    public int LastLegacyVisibilityMasks { get; private set; }
    public int LastMaterialMaskOccludersSubmitted { get; private set; }
    public int LastMaterialMaskRadialSamples { get; private set; }
    // Stage 16B: how many redundant per-material full-screen clears / target
    // switches were replaced with immutable 1x1 fallback texture bindings.
    public int LastMaterialNeutralMapsReused { get; private set; }
    public int LastMaterialWhiteMasksReused { get; private set; }
    public int LastMaterialRegionsDrawn { get; private set; }
    public int LastMaterialRegionsOffscreen { get; private set; }
    // Number of covered pixels submitted to the material shader; excludes
    // full-target clears and mask geometry. It is NOT measured GPU time.
    public long LastMaterialShadePixels { get; private set; }
    public long LastMaterialShadePixelsAvoided { get; private set; }
    // Stage 16A: broad-phase diagnostics. Skips count only when routed
    // casters exist but all are safely outside a light's visibility radius.
    public int LastShadowCastersPrunedByRange { get; private set; }
    public int LastShadowMasksSkippedByRange { get; private set; }
    public int TargetRebuilds { get; private set; }
    public long Frames { get; private set; }
    public double AverageSubmitMilliseconds { get; private set; }
    public double PeakSubmitMilliseconds { get; private set; }
    // Eleven baseline full-resolution RGBA8 targets, plus one lazy temporary
    // for two-pass softness (only allocated after a Soft preset is selected).
    // This excludes driver allocations, row padding and backbuffers.
    public double EstimatedTargetsMegabytes => _world is null ? 0 :
        (_world.Width * (double)_world.Height *
            // Four targets switch from 4 bytes/pixel to 8 in HDR mode.
            (44 + (_l19HdrAllocated ? 16 : 0) +
             (_softBlurScratch is { IsDisposed: false } ? 4 : 0) +
             (_l19EmissionMap is { IsDisposed: false } ? 4 : 0)) +
         (_l19BloomA is { IsDisposed: false } ?
              2.0 * _l19BloomA.Width * _l19BloomA.Height * (_l19HdrAllocated ? 8 : 4) : 0) +
         (_l20BloomMidA is { IsDisposed: false } ?
              2.0 * _l20BloomMidA.Width * _l20BloomMidA.Height * (_l19HdrAllocated ? 8 : 4) : 0) +
         (_l20BloomWideA is { IsDisposed: false } ?
              2.0 * _l20BloomWideA.Width * _l20BloomWideA.Height * (_l19HdrAllocated ? 8 : 4) : 0)) /
         (1024 * 1024);

    public GameplayLightingRenderer(GraphicsDevice device, Effect? basicSurfaceEffect = null,
        Effect? polygonRimEffect = null)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        // Effect is owned by the host's ContentManager; renderer does not dispose it.
        _basicSurfaceEffect = basicSurfaceEffect;
        _polygonRimEffect = polygonRimEffect;
        _falloff = MakeFalloff(false);
        _posterizedFalloff = MakeFalloff(true);
        _contactShadow = MakeContactShadow();
        _neutralProductionNormal = MakeSolidTexture(new Color(128, 128, 255, 255));
        _neutralProductionEdge = MakeSolidTexture(new Color(128, 128, 128, 0));
        _whiteProductionVisibility = MakeSolidTexture(Color.White);
        _l19WhiteEmissionMask = MakeSolidTexture(Color.White);
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

    private Texture2D MakeSolidTexture(Color color)
    {
        var texture = new Texture2D(_device, 1, 1, false, SurfaceFormat.Color);
        texture.SetData(new[] { color });
        return texture;
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
        if (_world is { IsDisposed: false } && _world.Width == w && _world.Height == h &&
            _l19LastHdrRequest == HdrEnabled) return;
        _l19LastHdrRequest = HdrEnabled;
        _world?.Dispose(); _illumination?.Dispose(); _individual?.Dispose(); _visibility?.Dispose();
        _litLayer?.Dispose(); _layeredCanvas?.Dispose(); _basicAlbedoCanvas?.Dispose();
        _basicNormalMap?.Dispose(); _basicEdgeMap?.Dispose();
        _productionMask0?.Dispose(); _productionMask1?.Dispose();
        _softBlurScratch?.Dispose();
        _softBlurScratch = null;
        DisposeBloomPyramid();
        _l19EmissionMap?.Dispose(); _l19EmissionMap = null;
        _world = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _visibility = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _basicAlbedoCanvas = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _basicNormalMap = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _basicEdgeMap = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _productionMask0 = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        _productionMask1 = new RenderTarget2D(_device, w, h, false, SurfaceFormat.Color,
            DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        // Use true RGBA16F for *accumulation*, not only a float final pass.
        // RGBA8 is retained as a tested fallback for unsupported GPUs.
        SurfaceFormat sceneFormat = HdrEnabled && !_l19HdrUnavailable
            ? SurfaceFormat.HalfVector4 : SurfaceFormat.Color;
        try
        {
            CreateL19SceneTargets(w, h, sceneFormat);
        }
        catch (Exception e) when (sceneFormat == SurfaceFormat.HalfVector4 &&
            e is not OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine("L19 floating-point target unavailable: " + e);
            _illumination?.Dispose(); _individual?.Dispose(); _litLayer?.Dispose();
            _layeredCanvas?.Dispose();
            _l19HdrUnavailable = true;
            CreateL19SceneTargets(w, h, SurfaceFormat.Color);
        }
        _l19HdrAllocated = sceneFormat == SurfaceFormat.HalfVector4 && !_l19HdrUnavailable;
        _geometryEffect.Projection = Matrix.CreateOrthographicOffCenter(0, w, h, 0, 0, 1);
        TargetRebuilds++; // Diagnostic: should change on first use / actual resize only.
    }

    private void CreateL19SceneTargets(int w, int h, SurfaceFormat format)
    {
        _illumination = NewL19Target(w, h, format);
        _individual = NewL19Target(w, h, format);
        _litLayer = NewL19Target(w, h, format);
        _layeredCanvas = NewL19Target(w, h, format);
    }

    private RenderTarget2D NewL19Target(int width, int height, SurfaceFormat format) =>
        new(_device, width, height, false, format, DepthFormat.None, 0,
            RenderTargetUsage.PreserveContents);

    private Texture2D CaptureL19EmissionMask(SpriteBatch batch,
        Action<SpriteBatch>? draw, LightingViewport viewport)
    {
        if (draw is null) return _l19WhiteEmissionMask;
        if (_l19EmissionMap is null || _l19EmissionMap.IsDisposed ||
            _l19EmissionMap.Width != viewport.Width || _l19EmissionMap.Height != viewport.Height)
        {
            _l19EmissionMap?.Dispose();
            _l19EmissionMap = NewL19Target(viewport.Width, viewport.Height, SurfaceFormat.Color);
        }
        // Avoid simultaneously binding this target as a shader sampler.
        _basicSurfaceEffect!.Parameters["EmissionMap"].SetValue(_l19WhiteEmissionMask);
        CaptureBasicSurfaceMap(batch, _l19EmissionMap, Color.Black, draw);
        return _l19EmissionMap;
    }

    private void L19Fullscreen(SpriteBatch batch, Texture2D input, string technique,
        RenderTarget2D? output, int width, int height)
    {
        Effect effect = _basicSurfaceEffect!;
        effect.Parameters["SpriteTexture"].SetValue(input);
        effect.Parameters["L19SourceTexture"].SetValue(input);
        effect.CurrentTechnique = effect.Techniques[technique];
        _device.SetRenderTarget(output);
        batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque,
            SamplerState.LinearClamp, DepthStencilState.None,
            RasterizerState.CullNone, effect);
        try { batch.Draw(input, new Rectangle(0, 0, width, height), Color.White); }
        finally { batch.End(); }
    }

    private void DisposeBloomPyramid()
    {
        _l19BloomA?.Dispose(); _l19BloomB?.Dispose();
        _l20BloomMidA?.Dispose(); _l20BloomMidB?.Dispose();
        _l20BloomWideA?.Dispose(); _l20BloomWideB?.Dispose();
        _l19BloomA = null; _l19BloomB = null;
        _l20BloomMidA = null; _l20BloomMidB = null;
        _l20BloomWideA = null; _l20BloomWideB = null;
    }

    private bool EnsureL19BloomTargets()
    {
        int w = Math.Max(1, (_layeredCanvas!.Width + 1) / 2);
        int h = Math.Max(1, (_layeredCanvas.Height + 1) / 2);
        if (_l19BloomA is { IsDisposed: false } && _l20BloomWideB is { IsDisposed: false } &&
            _l19BloomA.Width == w && _l19BloomA.Height == h) return true;
        DisposeBloomPyramid();
        try
        {
            SurfaceFormat format = _l19HdrAllocated ? SurfaceFormat.HalfVector4 : SurfaceFormat.Color;
            int mw = Math.Max(1, (w + 1) / 2), mh = Math.Max(1, (h + 1) / 2);
            int ww = Math.Max(1, (mw + 1) / 2), wh = Math.Max(1, (mh + 1) / 2);
            _l19BloomA = NewL19Target(w, h, format);
            _l19BloomB = NewL19Target(w, h, format);
            _l20BloomMidA = NewL19Target(mw, mh, format);
            _l20BloomMidB = NewL19Target(mw, mh, format);
            _l20BloomWideA = NewL19Target(ww, wh, format);
            _l20BloomWideB = NewL19Target(ww, wh, format);
            return true;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            Debug.WriteLine("L20 bloom pyramid unavailable: " + e);
            DisposeBloomPyramid();
            return false;
        }
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
        LastSubmitMilliseconds = 0; LastMaterialSurfaces = 0;
        LastMaterialLightsDropped = 0; LastMaterialVisibilityMasks = 0;
        LastMaterialLightsEvaluated = 0; LastMaterialLightPasses = 0;
        LastMaterialAmbientPasses = 0; LastMaterialMaxLightsOnSurface = 0;
        LastSoftLayerMasks = 0; LastSoftMaterialMasks = 0;
        LastContactProfilesBuilt = 0;
        LastContactProfileCpuMilliseconds = 0;
        LastContactProfileBytesUploaded = 0;
        LastSoftFilterPasses = 0;
        LastSoftFilterPixels = 0;
        LastEmissiveSurfaces = 0;
        LastPostProcessCpuMilliseconds = 0;
        LastBloomPasses = 0;
        LastBloomPixels = 0;
        LastDirectionalLights = 0; LastSpotLights = 0; LastTubeLights = 0;
        _contactReady = false;
        LastMaterialBackRimSurfaces = 0; Frames++;
        LastLayerCpuMilliseconds = 0;
        LastMaterialCpuMilliseconds = 0;
        LastMaterialMaskGeometryCpuMilliseconds = 0;
        LastLayerMaskGeometryCpuMilliseconds = 0;
        LastLegacyVisibilityMasks = 0;
        LastMaterialMaskOccludersSubmitted = 0;
        LastMaterialMaskRadialSamples = 0;
        LastMaterialNeutralMapsReused = 0;
        LastMaterialWhiteMasksReused = 0;
        LastMaterialRegionsDrawn = 0;
        LastMaterialRegionsOffscreen = 0;
        LastMaterialShadePixels = 0;
        LastMaterialShadePixelsAvoided = 0;
        LastShadowCastersPrunedByRange = 0;
        LastShadowMasksSkippedByRange = 0;
        _productionRadialSamples = budget.ShadowRadialSamples;
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
        var l19Watch = Stopwatch.StartNew();
        // The lit scene is complete. Bloom operates here, BEFORE any HUD/UI.
        // No source texture is ever sampled while bound for writing.
        if (_basicSurfaceEffect is not null && (HdrEnabled || BloomEnabled))
        {
            new L19PostProcessPolicy(PostExposure, BloomThreshold, BloomStrength).Validate();
            Effect fx = _basicSurfaceEffect;
            fx.Parameters["L19Exposure"].SetValue(PostExposure);
            fx.Parameters["L19Threshold"].SetValue(BloomThreshold);
            fx.Parameters["L19BloomGain"].SetValue(BloomStrength);
            bool bloom = BloomEnabled && BloomStrength > 0 && EnsureL19BloomTargets();
            int w = _layeredCanvas!.Width, h = _layeredCanvas.Height;
            if (bloom)
            {
                // Hot highlights extracted once (HDR pre-tonemap), then a reusable
                // half/quarter/eighth pyramid creates near, mid and far halos.
                L19Fullscreen(batch, _layeredCanvas!, "L19BloomExtract", _l19BloomA,
                    _l19BloomA!.Width, _l19BloomA.Height);
                fx.Parameters["L19BlurStep"].SetValue(new Vector2(1f / _l19BloomA.Width, 0));
                L19Fullscreen(batch, _l19BloomA, "L19BloomBlur", _l19BloomB,
                    _l19BloomB!.Width, _l19BloomB.Height);
                fx.Parameters["L19BlurStep"].SetValue(new Vector2(0, 1f / _l19BloomB.Height));
                L19Fullscreen(batch, _l19BloomB, "L19BloomBlur", _l19BloomA,
                    _l19BloomA.Width, _l19BloomA.Height);
                L19Fullscreen(batch, _l19BloomA, "L20BloomDownsample", _l20BloomMidA,
                    _l20BloomMidA!.Width, _l20BloomMidA.Height);
                fx.Parameters["L19BlurStep"].SetValue(new Vector2(1f / _l20BloomMidA.Width, 0));
                L19Fullscreen(batch, _l20BloomMidA, "L19BloomBlur", _l20BloomMidB,
                    _l20BloomMidB!.Width, _l20BloomMidB.Height);
                fx.Parameters["L19BlurStep"].SetValue(new Vector2(0, 1f / _l20BloomMidB.Height));
                L19Fullscreen(batch, _l20BloomMidB, "L19BloomBlur", _l20BloomMidA,
                    _l20BloomMidA.Width, _l20BloomMidA.Height);
                L19Fullscreen(batch, _l20BloomMidA, "L20BloomDownsample", _l20BloomWideA,
                    _l20BloomWideA!.Width, _l20BloomWideA.Height);
                fx.Parameters["L19BlurStep"].SetValue(new Vector2(1f / _l20BloomWideA.Width, 0));
                L19Fullscreen(batch, _l20BloomWideA, "L19BloomBlur", _l20BloomWideB,
                    _l20BloomWideB!.Width, _l20BloomWideB.Height);
                fx.Parameters["L19BlurStep"].SetValue(new Vector2(0, 1f / _l20BloomWideB.Height));
                L19Fullscreen(batch, _l20BloomWideB, "L19BloomBlur", _l20BloomWideA,
                    _l20BloomWideA.Width, _l20BloomWideA.Height);
                fx.Parameters["L19BloomTexture"].SetValue((Texture2D)_l19BloomA);
                fx.Parameters["L20BloomMidTexture"].SetValue((Texture2D)_l20BloomMidA);
                fx.Parameters["L20BloomWideTexture"].SetValue((Texture2D)_l20BloomWideA);
                fx.Parameters["L20BloomScatter"].SetValue(Math.Clamp(BloomScatter, 0f, 1f));
                fx.Parameters["L20BloomOnly"].SetValue(BloomOnly ? 1f : 0f);
                L19Fullscreen(batch, _layeredCanvas,
                    HdrActive ? "L20HdrBloomComposite" : "L20SdrBloomComposite", null, w, h);
                LastBloomPasses = 10;
                LastBloomPixels = 3L * (_l19BloomA.Width * (long)_l19BloomA.Height +
                    _l20BloomMidA.Width * (long)_l20BloomMidA.Height +
                    _l20BloomWideA.Width * (long)_l20BloomWideA.Height) + (long)w * h;
            }
            else if (HdrActive)
                L19Fullscreen(batch, _layeredCanvas!, "L19ToneMap", null, w, h);
            else
            {
                _device.SetRenderTarget(null);
                batch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.PointClamp);
                batch.Draw(_layeredCanvas, new Rectangle(0, 0, w, h), Color.White);
                batch.End();
            }
            fx.CurrentTechnique = fx.Techniques["SpriteDrawing"];
        }
        else
        {
            _device.SetRenderTarget(null);
            batch.Begin(blendState: BlendState.Opaque, samplerState: SamplerState.PointClamp);
            batch.Draw(_layeredCanvas!, new Rectangle(0, 0, _layeredCanvas!.Width,
                _layeredCanvas.Height), Color.White);
            batch.End();
        }
        l19Watch.Stop();
        LastPostProcessCpuMilliseconds = l19Watch.Elapsed.TotalMilliseconds;
        LastSubmitMilliseconds += LastPostProcessCpuMilliseconds;
        AverageSubmitMilliseconds += (LastSubmitMilliseconds - AverageSubmitMilliseconds) *
            (Frames == 1 ? 1 : .05);
        _cpuWindow.Add(LastSubmitMilliseconds);
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

    /// <summary>
    /// Stage 15B2 production opt-in: shade a positioned LDtk artwork through
    /// the same GPU surface effect as Lighting Basics. Exact self-caster
    /// exclusion and two unbounded per-light radial visibility masks preserve
    /// production shadow correctness without the lab's three-polygon limit.
    /// The existing ordered layered canvas and legacy receivers remain intact.
    /// </summary>
    public void DrawProductionMaterialSurface(SpriteBatch batch, LightingViewport viewport,
        ProductionSurfaceSubmission surface, ProductionMaterialRegion region,
        IReadOnlyList<RadialLight> visible,
        IReadOnlyList<LightingOccluder> blockers,
        LightingArtPreset art, SceneLightingLayer layer,
        Action<SpriteBatch> drawAlbedo,
        Action<SpriteBatch>? drawNormals = null,
        Action<SpriteBatch>? drawEdges = null,
        Action<SpriteBatch>? drawEmission = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(visible);
        ArgumentNullException.ThrowIfNull(blockers);
        ArgumentNullException.ThrowIfNull(drawAlbedo);
        surface.Validate();
        art.Validate(); layer.Validate(); viewport.Validate();
        if (surface.Shading == ProductionSurfaceShading.Legacy)
            throw new InvalidOperationException("Production material pass requires opt-in shading.");
        if (surface.LayerId != layer.Id || layer.Mode == RenderLayerLightingMode.Unlit)
            throw new ArgumentException("Material layer must match an active lit presentation layer.");
        if (_basicSurfaceEffect is null)
            throw new InvalidOperationException("Production GPU material effect not loaded.");
        if (_capturing || _basicFrameActive || _layeredCanvas is null)
            throw new InvalidOperationException("Complete the current lighting layer before material draws.");
        if (_layeredCanvas.Width != viewport.Width || _layeredCanvas.Height != viewport.Height)
            throw new ArgumentException("Material viewport mismatch.", nameof(viewport));
        // Region always comes from the same sprite transform used for drawing.
        // Validate at the GPU boundary rather than allowing a malformed
        // region to leave stale pixels or an out-of-range GL scissor.
        if (region.X < 0 || region.Y < 0 || region.Width < 0 || region.Height < 0 ||
            (long)region.X + region.Width > viewport.Width ||
            (long)region.Y + region.Height > viewport.Height)
            throw new ArgumentOutOfRangeException(nameof(region));
        if (region.IsEmpty)
        {
            LastMaterialRegionsOffscreen++;
            return; // No mask geometry, RT clears or shader work off-screen.
        }
        LastMaterialRegionsDrawn++;

        // The presenter has already enforced the global quality/camera budget.
        // Phase 18 preserves deterministic influence ranking but now evaluates
        // EVERY selected light in batches of two instead of silently dropping
        // all but the first pair. CompareTwo is a visible A/B developer option.
        _basicViewport = viewport;
        _productionLightRadiusScale = art.LightRadiusScale;
        _basicAmbient = (art.Ambient * ((1 - art.AtmosphericDarkness) *
            layer.AmbientScale * art.AmbientScaleFor(layer.Id))).Clamp();
        _basicShadowShapes = Array.Empty<BasicShadowShape>();
        IReadOnlyList<LightingOccluder> ownWalls = ProductionWallLightRouting.OwnWalls(surface, blockers);
        LightPoint center = viewport.Unproject(new LightPoint(
            viewport.Width * .5, viewport.Height * .5));
        if (surface.Center is LightPoint authoredCenter) center = authoredCenter;
        int maximum = ProductionMaterialLightPlan.Limit(MaterialLightMode);
        IReadOnlyList<RadialLight> selected =
            ProductionSurfaceLightSelection.Select(visible, layer, center,
                maximum: maximum, radiusScale: art.LightRadiusScale);
        int eligible = ProductionSurfaceLightSelection.CountEligible(visible, layer,
            center, art.LightRadiusScale);
        LastMaterialLightsDropped += Math.Max(0, eligible - selected.Count);
        LastMaterialLightsEvaluated += selected.Count;
        LastMaterialMaxLightsOnSurface = Math.Max(LastMaterialMaxLightsOnSurface, selected.Count);
        int materialPasses = 1;
        foreach (var pair in ProductionMaterialLightPlan.Batches(selected.Count))
        {
            int behind = 0;
            for (int i = 0; i < pair.Count; i++)
                if (ProductionWallLightRouting.BehindAny(surface, selected[pair.Start+i], ownWalls)) behind++;
            materialPasses += (behind < pair.Count ? 1 : 0) +
                (surface.BackRimIntensity > 0 ? behind : 0);
        }
        LastMaterialShadePixels += region.Area * (long)materialPasses;
        LastMaterialShadePixelsAvoided +=
            ((long)viewport.Width * viewport.Height - region.Area) * materialPasses;
        var material = new BasicSurfaceMaterial(BasicSurfaceLightingMode.Lit,
            BasicLightReceiverChannels.Both, 1)
        {
            Shading = (BasicSurfaceShadingMode)((int)surface.Shading - 1),
            NormalStrength = surface.NormalStrength,
            RimStrength = surface.RimStrength,
            ReceivesShadows = surface.ReceivesShadows
        };
        material.Validate();

        var watch = Stopwatch.StartNew();
        // Capture maps ONCE per surface, not for every light pair. Accumulation
        // occurs entirely inside _litLayer, then composites alpha only once.
        Rectangle full = new(0, 0, viewport.Width, viewport.Height);
        _device.SetRenderTarget(_world);
        _device.Clear(Color.Transparent);
        batch.Begin(blendState: BlendState.NonPremultiplied,
            samplerState: SamplerState.PointClamp);
        try { drawAlbedo(batch); }
        finally { batch.End(); }
        ProductionMaterialMapPassPlan maps = ProductionMaterialPassPlanner.Maps(
            drawNormals is not null, drawEdges is not null);
        Texture2D normalTexture = _neutralProductionNormal;
        Texture2D edgeTexture = _neutralProductionEdge;
        if (maps.CaptureNormal)
        {
            CaptureBasicSurfaceMap(batch, _basicNormalMap!,
                new Color(128, 128, 255), drawNormals);
            normalTexture = _basicNormalMap!;
        }
        else LastMaterialNeutralMapsReused++;
        if (maps.CaptureEdge)
        {
            CaptureBasicSurfaceMap(batch, _basicEdgeMap!,
                new Color(128, 128, 128, 0), drawEdges);
            edgeTexture = _basicEdgeMap!;
        }
        else LastMaterialNeutralMapsReused++;
        Texture2D emissiveMask = surface.EmissionIntensity > 0 && EmissionEnabled
            ? CaptureL19EmissionMask(batch, drawEmission, viewport)
            : _l19WhiteEmissionMask;
        // First write the ambient/albedo/alpha base. The effect short-circuits
        // normals and all light evaluation in this pass. This keeps ambient
        // invariant when a surface has 0, 2, 3, or 12 selected lights.
        _basicLights = Array.Empty<BasicMaterialLight>();
        BindBasicSurfaceShader(material);
        BindProductionMaterialMaps(normalTexture, edgeTexture);
        _basicSurfaceEffect!.Parameters["EmissionMap"].SetValue(emissiveMask);
        if (EmissionEnabled && surface.EmissionIntensity > 0)
        {
            _basicSurfaceEffect.Parameters["ProductionEmission"].SetValue(new Vector4(
                (float)surface.EmissionColor.R, (float)surface.EmissionColor.G,
                (float)surface.EmissionColor.B, (float)surface.EmissionIntensity));
            LastEmissiveSurfaces++;
        }
        _basicSurfaceEffect!.Parameters["ProductionVisibility0"].SetValue(_whiteProductionVisibility);
        _basicSurfaceEffect.Parameters["ProductionVisibility1"].SetValue(_whiteProductionVisibility);
        _basicSurfaceEffect.Parameters["ProductionLightPassMode"].SetValue(1f);
        BindProductionVisibilityMode();
        _device.SetRenderTarget(_litLayer);
        _device.Clear(Color.Transparent);
        DrawProductionMaterialRegion(batch, region, full, BlendState.Opaque);
        LastMaterialAmbientPasses++;

        bool anyBackRim = false;
        int uploadedEdgeCount = 0;
        // Each pair has independent visibility, depth, back-rim and self-caster
        // routing. Both full-viewport visibility targets can be safely reused
        // AFTER the previous pair has been accumulated into _litLayer.
        foreach (ProductionMaterialLightBatch pair in ProductionMaterialLightPlan.Batches(selected.Count))
        {
            var lightsInPair = new List<RadialLight>(pair.Count);
            var configured = new List<BasicMaterialLight>(pair.Count);
            for (int slot = 0; slot < pair.Count; slot++)
            {
                RadialLight original = selected[pair.Start + slot];
                lightsInPair.Add(original);
                configured.Add(new BasicMaterialLight(original with
                {
                    Radius = Math.Min(10000, original.Radius * art.LightRadiusScale),
                    Intensity = original.Intensity
                }, slot == 0 ? BasicLightReceiverChannels.Light1 : BasicLightReceiverChannels.Light2));
            }
            bool back0 = ProductionWallLightRouting.BehindAny(surface, lightsInPair[0], ownWalls);
            bool back1 = pair.Count > 1 &&
                ProductionWallLightRouting.BehindAny(surface, lightsInPair[1], ownWalls);
            // Behind lights never illuminate the front face. The independent
            // polygon rim pass adds only the eligible edge contribution.
            var frontLights = new List<BasicMaterialLight>(pair.Count);
            for (int slot = 0; slot < pair.Count; slot++)
                if (!(slot == 0 ? back0 : back1)) frontLights.Add(configured[slot]);
            _basicLights = frontLights;
            // Release previous pair's mask samplers before those RTs are reused.
            _basicSurfaceEffect!.Parameters["ProductionVisibility0"].SetValue(_whiteProductionVisibility);
            _basicSurfaceEffect.Parameters["ProductionVisibility1"].SetValue(_whiteProductionVisibility);
            (Texture2D visibility0, Texture2D visibility1) =
                DrawProductionVisibility(batch, viewport, surface, lightsInPair,
                    blockers, layer, back0, back1);
            anyBackRim |= back0 || back1;
            BindBasicSurfaceShader(material, productionGain: art.LightMultiplier);
            BindProductionMaterialMaps(normalTexture, edgeTexture);
            _basicSurfaceEffect!.Parameters["ProductionVisibility0"].SetValue(visibility0);
            _basicSurfaceEffect.Parameters["ProductionVisibility1"].SetValue(visibility1);
            _basicSurfaceEffect.Parameters["ProductionLightPassMode"].SetValue(2f);
            BindProductionVisibilityMode();
            _device.SetRenderTarget(_litLayer);
            if (frontLights.Count > 0)
            {
                DrawProductionMaterialRegion(batch, region, full, _materialAdditive);
                LastMaterialLightPasses++;
            }
            if ((back0 || back1) && surface.BackRimIntensity > 0)
            {
                if (uploadedEdgeCount == 0) uploadedEdgeCount = UploadPolygonEdges(ownWalls);
                int edgeCount = uploadedEdgeCount;
                if (back0) DrawPolygonRim(batch, region, full, surface, configured[0].Light,
                    ownWalls, edgeCount, visibility0, art.LightMultiplier);
                if (back1) DrawPolygonRim(batch, region, full, surface, configured[1].Light,
                    ownWalls, edgeCount, visibility1, art.LightMultiplier);
                LastMaterialLightPasses += (back0 ? 1 : 0) + (back1 ? 1 : 0);
            }
        }
        if (anyBackRim) LastMaterialBackRimSurfaces++;
        // Only one final alpha composite per artwork, regardless of how many
        // shadowed light pairs it received. Region and UV remain unchanged.
        _device.SetRenderTarget(_layeredCanvas);
        batch.Begin(blendState: BlendState.AlphaBlend,
            samplerState: SamplerState.PointClamp);
        Rectangle clipped = new(region.X, region.Y, region.Width, region.Height);
        batch.Draw(_litLayer!, clipped, clipped, Color.White);
        batch.End();
        watch.Stop();
        LastSubmitMilliseconds += watch.Elapsed.TotalMilliseconds;
        LastMaterialCpuMilliseconds += watch.Elapsed.TotalMilliseconds;
        LastMaterialSurfaces++;
    }

    private void BindProductionMaterialMaps(Texture2D normal, Texture2D edge)
    {
        Effect shader = _basicSurfaceEffect!;
        shader.Parameters["NormalMap"].SetValue(normal);
        shader.Parameters["EdgeMap"].SetValue(edge);
    }

    private void DrawProductionMaterialRegion(SpriteBatch batch,
        ProductionMaterialRegion region, Rectangle full, BlendState blend, Effect? effect = null)
    {
        RasterizerState previousRasterizer = _device.RasterizerState;
        try
        {
            batch.Begin(SpriteSortMode.Immediate, blend,
                SamplerState.PointClamp, DepthStencilState.None,
                _productionMaterialScissor, effect ?? _basicSurfaceEffect);
            try
            {
                _device.ScissorRectangle = new Rectangle(region.X, region.Y,
                    region.Width, region.Height);
                batch.Draw(_world!, full, Color.White);
            }
            finally { batch.End(); }
        }
        finally
        {
            _device.ScissorRectangle = full;
            _device.RasterizerState = previousRasterizer;
        }
    }

    private (Texture2D Mask0, Texture2D Mask1) DrawProductionVisibility(
        SpriteBatch batch, LightingViewport viewport, ProductionSurfaceSubmission surface,
        IReadOnlyList<RadialLight> selected, IReadOnlyList<LightingOccluder> blockers,
        SceneLightingLayer layer, bool back0, bool back1)
    {
        // White means unobstructed: use the immutable one-pixel texture rather
        // than binding and clearing a full-screen render target for each slot.
        // IMPORTANT: an actual mask must still be regenerated per surface,
        // because the self-shadow exclusion ID differs between surfaces.
        Texture2D mask0 = _whiteProductionVisibility;
        Texture2D mask1 = _whiteProductionVisibility;
        for (int slot = 0; slot < 2; slot++)
        {
            if (slot >= selected.Count)
            {
                LastMaterialWhiteMasksReused++;
                continue;
            }
            RadialLight light = selected[slot];
            bool back = slot == 0 ? back0 : back1;
            bool receivesProjected = surface.ReceivesShadows && layer.ReceivesShadows;
            if (!ProductionMaterialPassPlanner.NeedsVisibility(receivesProjected,
                back, light.CastShadows, _shadowQuota.Contains(light.Id)))
            {
                LastMaterialWhiteMasksReused++;
                continue;
            }
            IReadOnlyList<LightingOccluder> routed =
                ProductionWallLightRouting.OtherBlockers(surface, light, blockers);
            // Always prefilter using the effective (art-scaled) light radius;
            // otherwise a light with a large radius scale can lose shadows.
            var scaled = light with
            {
                Radius = Math.Min(10000, light.Radius * _productionLightRadiusScale)
            };
            IReadOnlyList<LightingOccluder> nearby = light.Shape is LightShape.Directional or LightShape.Tube
                ? routed : ShadowCasterPruning.Filter(scaled, routed);
            LastShadowCastersPrunedByRange += routed.Count - nearby.Count;
            if (nearby.Count == 0)
            {
                if (routed.Count > 0) LastShadowMasksSkippedByRange++;
                LastMaterialWhiteMasksReused++;
                continue;
            }
            RenderTarget2D target = slot == 0 ? _productionMask0! : _productionMask1!;
            IReadOnlyList<LightPoint> fan;
            if (light.Shape == LightShape.Directional)
            {
                DrawDirectionalMask(viewport, light, nearby, target);
                fan = Array.Empty<LightPoint>();
                _contactReady = false;
            }
            else fan = DrawVisibilityFan(viewport, scaled,
                nearby, _productionRadialSamples, target);
            if (_shadowSoftnessPixels > 0f)
            {
                // Filter only real ray masks; the immutable 1x1 white fallback
                // never requires filtering. One shared temporary serves both
                // material lights without sampling an active render target.
                if (light.Shape != LightShape.Directional)
                    PrepareContactProfile(scaled, fan, viewport);
                RenderTarget2D scratch = EnsureSoftBlurScratch(viewport.Width, viewport.Height);
                GaussianVisibilityPass(batch, target, scratch, viewport,
                    horizontal: true, blend: BlendState.Opaque);
                GaussianVisibilityPass(batch, scratch, target, viewport,
                    horizontal: false, blend: BlendState.Opaque);
                LastSoftMaterialMasks++;
            }
            if (slot == 0) mask0 = target;
            else mask1 = target;
            LastMaterialVisibilityMasks++;
            LastMaterialMaskOccludersSubmitted += nearby.Count;
            LastMaterialMaskRadialSamples += _productionRadialSamples;
            if (_shadowCounted.Add(light.Id)) LastShadowLights++;
        }
        return (mask0, mask1);
    }

    private double _productionLightRadiusScale = 1;

    private void DrawShapedLight(SpriteBatch batch, LightingViewport viewport,
        RadialLight light, double intensity)
    {
        if (_basicSurfaceEffect is null) return;
        Effect fx = _basicSurfaceEffect;
        LightPoint center = viewport.Project(light.Position);
        double mag = Math.Sqrt(light.Direction.X * light.Direction.X +
            light.Direction.Y * light.Direction.Y);
        float dx = (float)(light.Direction.X / mag);
        float dy = (float)(-light.Direction.Y / mag);
        float outer = (float)Math.Cos(light.ConeAngleDegrees * Math.PI / 360);
        float inner = (float)Math.Cos(light.ConeAngleDegrees * (1 - light.ConeSoftness) * Math.PI / 360);
        fx.Parameters["ViewportPixels"].SetValue(new Vector2(viewport.Width, viewport.Height));
        fx.Parameters["L20LightPositionRadius"].SetValue(new Vector4(
            (float)center.X, (float)center.Y, (float)(light.Radius * viewport.PixelsPerUnit), 0));
        fx.Parameters["L20LightColorIntensity"].SetValue(new Vector4(
            (float)light.Color.R, (float)light.Color.G, (float)light.Color.B, (float)intensity));
        fx.Parameters["L20LightShape"].SetValue(new Vector4((float)light.Shape, dx, dy, outer));
        fx.Parameters["L20LightExtra"].SetValue(new Vector4(inner,
            (float)(light.TubeLength * viewport.PixelsPerUnit * .5), 0, 0));
        L19Fullscreen(batch, _world!, "L20LightField", _individual,
            viewport.Width, viewport.Height);
    }

    private void DrawDirectionalMask(LightingViewport view, RadialLight light,
        IReadOnlyList<LightingOccluder> blockers, RenderTarget2D target)
    {
        _device.SetRenderTarget(target);
        _device.Clear(Color.White);
        // L20 shadow-fidelity correction: project each *authored simple hull*
        // only by the light's bounded world-space reach. The old version swept
        // every edge multiple screen diagonals, creating giant black wedges.
        var triangles = new List<VertexPositionColor>(Math.Min(blockers.Count, 96) * 39);
        foreach (LightingOccluder blocker in blockers)
        {
            if (!DirectionalShadowProjection.IntersectsViewport(blocker, light.Direction,
                light.DirectionalShadowLength, view)) continue;
            IReadOnlyList<LightPoint> world = DirectionalShadowProjection.Triangles(
                blocker, light.Direction, light.DirectionalShadowLength);
            foreach (LightPoint point in world)
            {
                LightPoint screen = view.Project(point);
                triangles.Add(new VertexPositionColor(new Vector3(
                    (float)screen.X, (float)screen.Y, 0), Color.Black));
            }
        }
        if (triangles.Count == 0) return;
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        VertexPositionColor[] data = triangles.ToArray();
        foreach (EffectPass pass in _geometryEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.TriangleList,
                data, 0, data.Length / 3);
        }
        LastShadowTriangles += data.Length / 3;
    }

    private IReadOnlyList<LightPoint> DrawVisibilityFan(LightingViewport viewport, RadialLight light,
        IReadOnlyList<LightingOccluder> blockers, int samples, RenderTarget2D destination)
    {
        var geometryTimer = Stopwatch.StartNew();
        IReadOnlyList<LightPoint> boundary;
        try { boundary = ShadowVisibility.BuildPolygon(light, blockers, samples); }
        finally
        {
            geometryTimer.Stop();
            LastMaterialMaskGeometryCpuMilliseconds += geometryTimer.Elapsed.TotalMilliseconds;
        }
        _device.SetRenderTarget(destination);
        _device.Clear(Color.Black);
        if (boundary.Count < 3) return boundary;
        var triangles = new VertexPositionColor[boundary.Count * 3];
        LightPoint center = viewport.Project(light.Position);
        Vector3 source = new((float)center.X, (float)center.Y, 0);
        for (int i = 0; i < boundary.Count; i++)
        {
            LightPoint a = viewport.Project(boundary[i]);
            LightPoint b = viewport.Project(boundary[(i + 1) % boundary.Count]);
            triangles[3 * i] = new VertexPositionColor(source, Color.White);
            triangles[3 * i + 1] = new VertexPositionColor(
                new Vector3((float)a.X, (float)a.Y, 0), Color.White);
            triangles[3 * i + 2] = new VertexPositionColor(
                new Vector3((float)b.X, (float)b.Y, 0), Color.White);
        }
        LastShadowTriangles += boundary.Count;
        _device.BlendState = BlendState.Opaque;
        _device.DepthStencilState = DepthStencilState.None;
        _device.RasterizerState = RasterizerState.CullNone;
        foreach (EffectPass pass in _geometryEffect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _device.DrawUserPrimitives(PrimitiveType.TriangleList, triangles,
                0, triangles.Length / 3);
        }
        return boundary;
    }

    private void BindProductionVisibilityMode()
    {
        // Old shader is still the validated front-face/lab material shader.
        // Its four-corner back-rim uniforms are zeroed by BindBasicSurfaceShader.
        _basicSurfaceEffect!.Parameters["ProductionMaskMode"].SetValue(1f);
    }

    private int UploadPolygonEdges(IReadOnlyList<LightingOccluder> walls)
    {
        if (_polygonRimEffect is null)
            throw new InvalidOperationException("Production polygon rim effect is not loaded.");
        _polygonEdges ??= new Texture2D(_device, ProductionWallLightRouting.MaxOwnerEdges,
            2, false, SurfaceFormat.Vector4);
        int count = 0;
        int contour = 0;
        foreach (var wall in walls)
        {
            contour++;
            var v = new LightPoint[wall.Vertices.Length];
            for (int i=0;i<v.Length;i++) v[i] = _basicViewport.Project(wall.Vertices[i]);
            if (ShadowGeometry.SignedDoubleArea(v) < 0) Array.Reverse(v);
            for (int i=0;i<v.Length;i++)
            {
                var a = v[i]; var b = v[(i+1)%v.Length];
                _polygonEdgeData[count] = new Vector4((float)a.X,(float)a.Y,(float)b.X,(float)b.Y);
                _polygonEdgeData[ProductionWallLightRouting.MaxOwnerEdges + count] = new Vector4(contour,0,0,0);
                count++;
            }
        }
        _polygonEdges.SetData(_polygonEdgeData);
        return count;
    }

    private void DrawPolygonRim(SpriteBatch batch, ProductionMaterialRegion region, Rectangle full,
        ProductionSurfaceSubmission surface, RadialLight light,
        IReadOnlyList<LightingOccluder> walls, int count, Texture2D visibility, double gain)
    {
        if (surface.BackRimIntensity <= 0 || count == 0) return;
        Effect fx = _polygonRimEffect!;
        fx.CurrentTechnique = fx.Techniques["PolygonRim"];
        fx.Parameters["PolygonEdges"].SetValue(_polygonEdges!);
        fx.Parameters["VisibilityMap"].SetValue(visibility);
        fx.Parameters["ViewportSize"].SetValue(new Vector2(full.Width, full.Height));
        fx.Parameters["EdgeCount"].SetValue((float)count);
        LightPoint center = _basicViewport.Project(light.Position);
        LightPoint? insideCenter = null;
        int insideContour = 0;
        if (light.Shape != LightShape.Directional)
            for (int i=0;i<walls.Count;i++)
                if (ShadowGeometry.Contains(walls[i], light.Position))
                {
                    insideCenter = _basicViewport.Project(ProductionWallLightRouting.Center(walls[i]));
                    insideContour = i + 1;
                    break;
                }
        double m = Math.Sqrt(light.Direction.X*light.Direction.X+light.Direction.Y*light.Direction.Y);
        fx.Parameters["LightPositionRadius"].SetValue(new Vector4((float)center.X,(float)center.Y,
            (float)(light.Radius*_basicViewport.PixelsPerUnit),0));
        fx.Parameters["LightShapeDirection"].SetValue(new Vector4((float)light.Shape,
            (float)(light.Direction.X/m),(float)(-light.Direction.Y/m),
            (float)Math.Cos(light.ConeAngleDegrees*Math.PI/360)));
        fx.Parameters["LightExtra"].SetValue(new Vector4(
            (float)Math.Cos(light.ConeAngleDegrees*(1-light.ConeSoftness)*Math.PI/360),
            (float)(light.TubeLength*_basicViewport.PixelsPerUnit*.5),0,0));
        fx.Parameters["LightEnergy"].SetValue(new Vector4((float)light.Color.R,(float)light.Color.G,
            (float)light.Color.B,(float)(light.Intensity*gain)));
        fx.Parameters["RimSettings"].SetValue(new Vector4((float)surface.BackRimWidthPx,
            (float)surface.BackRimIntensity,insideContour,0));
        fx.Parameters["InsideCenter"].SetValue(new Vector2((float)(insideCenter?.X ?? 0),
            (float)(insideCenter?.Y ?? 0)));
        DrawProductionMaterialRegion(batch, region, full, _materialAdditive, fx);
        fx.Parameters["VisibilityMap"].SetValue(_whiteProductionVisibility);
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
        int wallRimShadowSlot = -1, double productionGain = 1)
    {
        Effect effect = _basicSurfaceEffect!;
        effect.CurrentTechnique = effect.Techniques["SpriteDrawing"];
        effect.Parameters["EmissionMap"].SetValue(_l19WhiteEmissionMask);
        effect.Parameters["ProductionEmission"].SetValue(Vector4.Zero);
        effect.Parameters["ProductionLightPassMode"].SetValue(0f);
        effect.Parameters["ProductionMaskMode"].SetValue(0f);
        effect.Parameters["ProductionVisibility0"].SetValue((Texture2D)_productionMask0!);
        effect.Parameters["ProductionVisibility1"].SetValue((Texture2D)_productionMask1!);
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
                float energy = material.ReceivesLight(configured.Channel) ? (float)(light.Intensity * productionGain) : 0f;
                colorIntensity = new Vector4((float)light.Color.R, (float)light.Color.G,
                    (float)light.Color.B, energy);
                break;
            }
            effect.Parameters[$"Light{i}PositionRadius"].SetValue(position);
            effect.Parameters[$"Light{i}ColorIntensity"].SetValue(colorIntensity);
            RadialLight? shapeLight = null;
            foreach (BasicMaterialLight configured in _basicLights!)
                if (configured.Channel == (i == 0 ? BasicLightReceiverChannels.Light1 : BasicLightReceiverChannels.Light2))
                { shapeLight = configured.Light; break; }
            var shape = shapeLight?.Shape ?? LightShape.Point;
            LightPoint direction = shapeLight?.Direction ?? new LightPoint(1, -0.65);
            double magnitude = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            float dx = (float)(direction.X / magnitude), dy = (float)(-direction.Y / magnitude);
            double angle = shapeLight?.ConeAngleDegrees ?? 55;
            double softness = shapeLight?.ConeSoftness ?? .15;
            effect.Parameters[$"Light{i}Shape"].SetValue(new Vector4((float)shape,
                dx, dy, (float)Math.Cos(angle * Math.PI / 360)));
            effect.Parameters[$"Light{i}ShapeExtra"].SetValue(new Vector4(
                (float)Math.Cos(angle * (1 - softness) * Math.PI / 360),
                (float)((shapeLight?.TubeLength ?? 3) * _basicViewport.PixelsPerUnit * .5), 0, 0));
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
            if (light.Shape != LightShape.Point)
            {
                DrawShapedLight(batch, viewport, light with {
                    Radius = Math.Min(10000, light.Radius * art.LightRadiusScale) },
                    light.Intensity * art.LightMultiplier);
                if (light.Shape == LightShape.Directional) LastDirectionalLights++;
                if (light.Shape == LightShape.Spot) LastSpotLights++;
                if (light.Shape == LightShape.Tube) LastTubeLights++;
            }
            else
            {
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
            } // point-light legacy texture path
            if (light.CastShadows && layer.ReceivesShadows &&
                _shadowQuota.Contains(light.Id))
            {
                var relevant = new List<LightingOccluder>();
                foreach (LightingOccluder blocker in blockers)
                    if (LightLayerRouting.Occludes(light, blocker)) relevant.Add(blocker);
                if (relevant.Count > 0)
                {
                    var scaled = light with
                    {
                        Radius = Math.Min(10000, light.Radius * art.LightRadiusScale)
                    };
                    IReadOnlyList<LightingOccluder> nearby = light.Shape is LightShape.Directional or LightShape.Tube
                        ? relevant : ShadowCasterPruning.Filter(scaled, relevant);
                    LastShadowCastersPrunedByRange += relevant.Count - nearby.Count;
                    if (nearby.Count > 0)
                    {
                        ApplyVisibilityMask(batch, viewport, scaled, nearby,
                            budget.ShadowRadialSamples);
                        if (_shadowCounted.Add(light.Id)) LastShadowLights++;
                    }
                    else LastShadowMasksSkippedByRange++;
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
        LastLayerCpuMilliseconds += watch.Elapsed.TotalMilliseconds;
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

    private RenderTarget2D EnsureSoftBlurScratch(int width, int height)
    {
        if (_softBlurScratch is { IsDisposed: false } &&
            _softBlurScratch.Width == width && _softBlurScratch.Height == height)
            return _softBlurScratch;
        _softBlurScratch?.Dispose();
        _softBlurScratch = new RenderTarget2D(_device, width, height, false,
            SurfaceFormat.Color, DepthFormat.None, 0,
            RenderTargetUsage.PreserveContents);
        return _softBlurScratch;
    }

    private void PrepareContactProfile(RadialLight light,
        IReadOnlyList<LightPoint> visibleFan, LightingViewport view)
    {
        _contactReady = false;
        if (FilterMode != ShadowFilterMode.ContactHardening ||
            _shadowSoftnessPixels <= 0 || visibleFan.Count < 3)
            return;
        var timer = Stopwatch.StartNew();
        // BuildPolygon's reach uses 1.02 * the effective scene-scaled radius.
        double reachWorld = light.Radius * 1.02;
        ShadowContactHardening.FillProfile(light.Position, visibleFan,
            reachWorld, _contactDistances);
        for (int i = 0; i < _contactDistances.Length; i++)
        {
            ushort packed = _contactDistances[i];
            _contactPackedPixels[i] = new Color(
                (byte)(packed >> 8), (byte)packed, (byte)0, (byte)255);
        }
        _contactHitProfile ??= new Texture2D(_device,
            ShadowContactHardening.ProfileResolution, 1, false, SurfaceFormat.Color);
        _contactHitProfile.SetData(_contactPackedPixels);
        LightPoint screen = view.Project(light.Position);
        _contactCenterPixels = new Vector2((float)screen.X, (float)screen.Y);
        _contactReachPixels = (float)(reachWorld * view.PixelsPerUnit);
        _contactEmitterPixels = ShadowEmitterOverrides.TryGetValue(light.Id, out float radius)
            ? Math.Clamp(radius, 0f, 32f) : 8f;
        _contactReady = true;
        timer.Stop();
        LastContactProfileCpuMilliseconds += timer.Elapsed.TotalMilliseconds;
        LastContactProfilesBuilt++;
        LastContactProfileBytesUploaded += _contactPackedPixels.Length * 4L;
    }

    /// <summary>
    /// One axis of a true separable Gaussian over EVERY texel between -radius
    /// and +radius (using bilinear tap pairing). Source and target must differ.
    /// For an ordinary layer the vertical pass multiplies the lit light-buffer;
    /// for a material the vertical pass writes the filtered mask opaquely.
    /// </summary>
    private void GaussianVisibilityPass(SpriteBatch batch, Texture2D source,
        RenderTarget2D destination, LightingViewport view,
        bool horizontal, BlendState blend)
    {
        if (ReferenceEquals(source, destination))
            throw new InvalidOperationException("Cannot blur a visibility mask in place.");
        Effect effect = _basicSurfaceEffect ?? throw new InvalidOperationException(
            "Soft shadow mask shader is not loaded.");
        ShadowGaussianKernel kernel = ShadowEdgeSettings.GaussianKernel(
            _contactReady ? 8f : _shadowSoftnessPixels);
        effect.Parameters["SoftContactEnabled"].SetValue(_contactReady ? 1f : 0f);
        effect.Parameters["SoftContactProfile"].SetValue(
            (Texture2D?)_contactHitProfile ?? _whiteProductionVisibility);
        effect.Parameters["SoftContactLightCenter"].SetValue(_contactCenterPixels);
        effect.Parameters["SoftContactFanReachPixels"].SetValue(_contactReachPixels);
        effect.Parameters["SoftContactEmitterRadiusPixels"].SetValue(_contactEmitterPixels);
        effect.Parameters["SoftContactMaxBlurPixels"].SetValue(_shadowSoftnessPixels);
        effect.Parameters["ViewportPixels"].SetValue(new Vector2(view.Width, view.Height));
        EffectTechnique previousTechnique = effect.CurrentTechnique;
        effect.Parameters["SoftMaskSource"].SetValue(source);
        effect.Parameters["SoftBlurTexelStep"].SetValue(horizontal
            ? new Vector2(1f / view.Width, 0f)
            : new Vector2(0f, 1f / view.Height));
        effect.Parameters["SoftBlurCenterWeight"].SetValue(kernel.CenterWeight);
        effect.Parameters["SoftBlurOffsets"].SetValue(new Vector4(
            kernel.Pair0.Offset, kernel.Pair1.Offset,
            kernel.Pair2.Offset, kernel.Pair3.Offset));
        effect.Parameters["SoftBlurWeights"].SetValue(new Vector4(
            kernel.Pair0.Weight, kernel.Pair1.Weight,
            kernel.Pair2.Weight, kernel.Pair3.Weight));
        _device.SetRenderTarget(destination);
        try
        {
            effect.CurrentTechnique = effect.Techniques["GaussianVisibility"];
            batch.Begin(SpriteSortMode.Immediate, blend, SamplerState.LinearClamp,
                DepthStencilState.None, RasterizerState.CullNone, effect);
            try { batch.Draw(source, new Rectangle(0, 0, view.Width, view.Height), Color.White); }
            finally { batch.End(); }
        }
        finally { effect.CurrentTechnique = previousTechnique; }
        LastSoftFilterPasses++;
        LastSoftFilterPixels += (long)view.Width * view.Height;
    }

    private void ApplyVisibilityMask(SpriteBatch batch, LightingViewport view,
        RadialLight light, IReadOnlyList<LightingOccluder> occluders, int radialSamples)
    {
        if (light.Shape == LightShape.Directional)
        {
            DrawDirectionalMask(view, light, occluders, _visibility!);
            _contactReady = false;
            LastLegacyVisibilityMasks++;
            if (_shadowSoftnessPixels > 0f && _basicSurfaceEffect is not null)
            {
                RenderTarget2D scratch = EnsureSoftBlurScratch(view.Width, view.Height);
                GaussianVisibilityPass(batch, _visibility!, scratch, view, true, BlendState.Opaque);
                GaussianVisibilityPass(batch, scratch, _individual!, view, false, _multiply);
                LastSoftLayerMasks++;
            }
            else
            {
                _device.SetRenderTarget(_individual);
                batch.Begin(blendState: _multiply, samplerState: SamplerState.PointClamp);
                batch.Draw(_visibility!, new Rectangle(0, 0, view.Width, view.Height), Color.White);
                batch.End();
            }
            return;
        }
        var geometryTimer = Stopwatch.StartNew();
        IReadOnlyList<LightPoint> polygon;
        try { polygon = ShadowVisibility.BuildPolygon(light, occluders, radialSamples); }
        finally
        {
            geometryTimer.Stop();
            LastLayerMaskGeometryCpuMilliseconds += geometryTimer.Elapsed.TotalMilliseconds;
        }
        LastLegacyVisibilityMasks++;
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
        if (_shadowSoftnessPixels > 0f && _basicSurfaceEffect is not null)
        {
            // First blur the hard ray fan horizontally into a shared scratch;
            // then blur vertically WHILE multiplying the resulting visibility
            // into the existing per-light illumination target. The hard path
            // below is preserved when softness is disabled.
            PrepareContactProfile(light, polygon, view);
            RenderTarget2D scratch = EnsureSoftBlurScratch(view.Width, view.Height);
            GaussianVisibilityPass(batch, _visibility!, scratch, view,
                horizontal: true, blend: BlendState.Opaque);
            GaussianVisibilityPass(batch, scratch, _individual!, view,
                horizontal: false, blend: _multiply);
            LastSoftLayerMasks++;
        }
        else
        {
            _device.SetRenderTarget(_individual);
            // Original Phase 16 hard visibility multiplication (unmodified).
            batch.Begin(blendState: _multiply, samplerState: SamplerState.PointClamp);
            batch.Draw(_visibility!, new Rectangle(0, 0, view.Width, view.Height), Color.White);
            batch.End();
        }
    }

    public void Abort()
    {
        _capturing = false;
        _basicFrameActive = false;
        _basicLights = null;
        _basicShadowShapes = null;
        _contactReady = false;
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
        _productionMask0?.Dispose(); _productionMask1?.Dispose();
        _softBlurScratch?.Dispose();
        DisposeBloomPyramid();
        _l19EmissionMap?.Dispose(); _l19WhiteEmissionMask.Dispose();
        _contactHitProfile?.Dispose();
        _falloff.Dispose(); _posterizedFalloff.Dispose(); _contactShadow.Dispose();
        _neutralProductionNormal.Dispose(); _neutralProductionEdge.Dispose();
        _whiteProductionVisibility.Dispose();
        _productionMaterialScissor.Dispose();
        _polygonEdges?.Dispose(); _materialAdditive.Dispose();
        _geometryEffect.Dispose(); _additive.Dispose(); _multiply.Dispose();
        _multiplyKeepAlpha.Dispose(); _layerExposure.Dispose();
    }
}
