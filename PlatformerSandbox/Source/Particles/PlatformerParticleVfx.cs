using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Foundation.Presentation.Particles;
using PlatformerSandbox.Gameplay.Presentation;

namespace PlatformerSandbox.Particles;

/// <summary>
/// MonoGame host adapter. Catalog and mapping are authored JSON; all
/// simulation/lifecycle and committed-event semantics remain headless.
/// Shared GPU sprites belong to the app, not individual effect instances.
/// </summary>
internal sealed class PlatformerParticleVfx : IDisposable
{
    private readonly MonoGameParticleRenderer _renderer;
    private readonly ParticleCompositeCatalog _catalog;
    private ParticleZoneEnvironment? _environment;
    private readonly List<ParticleDrawSource> _sources = new(64);
    private bool _disposed;

    public ParticleEffectPresenter Presenter { get; }
    public int LastDrawCount => _renderer.LastDrawCount;
    public int LastGpuInstances=>_renderer.LastGpuInstances;
    public int LastGpuDraws=>_renderer.LastGpuDraws;
    public int LastGpuAnalytic=>_renderer.LastGpuAnalytic;
    public int LastGpuFallbacks=>_renderer.LastGpuFallbacks;
    public long LastGpuUploadBytes=>_renderer.LastGpuUploadBytes;
    public string GpuStatus=>_renderer.GpuStatus;
    public void DrawStress(SpriteBatch batch,int count,int w,int h,float time,bool analytic)=>_renderer.DrawStress(batch,count,w,h,time,analytic);
    public int ActiveEnvironmentZones => _environment?.ActiveZones ?? 0;
    public int EnvironmentZones => _environment?.TotalZones ?? 0;
    public int EnvironmentParticles => _environment?.ActiveParticles ?? 0;
    public long CappedZoneFrames => _environment?.CappedZoneFrames ?? 0;

    /// <summary>Replace only authored visual zones when the active world changes.</summary>
    public void ConfigureEnvironment(PlatformerSandbox.Gameplay.Content.LdtkParticleSceneDefinition? scene)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ParticleZoneEnvironment? next = scene is null ? null :
            new ParticleZoneEnvironment(scene, _catalog, Presenter.FaultPolicy);
        _environment?.Clear();
        _environment = next;
    }

    public void UpdateEnvironment(PlatformerSandbox.Foundation.Geometry.Vector2D center,
        double deltaSeconds, bool paused) =>
        _environment?.Update(center, deltaSeconds, paused);


    public PlatformerParticleVfx(GraphicsDevice device, string particleFolder,Effect? instanceEffect=null)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (string.IsNullOrWhiteSpace(particleFolder))
            throw new ArgumentException("Particle content folder required.", nameof(particleFolder));
        ParticleCompositeCatalog catalog = ParticleCompositeCatalog.FromJson(
            File.ReadAllText(Path.Combine(particleFolder, "P04_GameplayEffects.json")));
        ParticleGameplayBindings bindings = ParticleGameplayBindings.FromJson(
            File.ReadAllText(Path.Combine(particleFolder, "P04_EventBindings.json")), catalog);
        _catalog = catalog;
        Presenter = new ParticleEffectPresenter(catalog, bindings,
#if DEBUG
            ParticleFaultPolicy.Strict
#else
            ParticleFaultPolicy.RecoverAndCount
#endif
        );
        _renderer = new MonoGameParticleRenderer(device,instanceEffect);
    }

    /// <summary>Only world-space effect instances may request cosmetic lights;
    /// each effect contributes at most one candidate, not one light per particle.</summary>
    public IEnumerable<(string EffectId, PlatformerSandbox.Foundation.Geometry.Vector2D Origin)> LightCandidates()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        foreach (ParticleCompositeInstance instance in Presenter.WorldInstances)
            if (!instance.IsFinished)
                yield return (instance.Definition.Id, instance.Origin);
        if (_environment is not null)
            foreach (ParticleCompositeInstance instance in _environment.Instances)
                if (!instance.IsFinished)
                    yield return (instance.Definition.Id, instance.Origin);
    }

    public void BeginFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _renderer.BeginFrame();
        _sources.Clear();
        foreach (ParticleCompositeInstance instance in Presenter.WorldInstances)
            Add(instance);
        if (_environment is not null)
            foreach (ParticleCompositeInstance instance in _environment.Instances)
                Add(instance);
        foreach (ParticleCompositeInstance instance in Presenter.UiInstances)
            Add(instance);
    }

    private void Add(ParticleCompositeInstance instance)
    {
        foreach (ParticleEffectPart part in instance.Parts)
        {
            if (part.Particles.ActiveCount == 0) continue;
            ParticleEffectPartDefinition def = part.Definition;
            _sources.Add(new ParticleDrawSource(part.Particles, def.Layer,
                def.Space, def.Blend, def.Sprite, def.Lighting));
        }
    }

    /// <summary>Only call while SpriteBatch is *not* begun.</summary>
    public int DrawLayer(SpriteBatch batch, in ParticleViewport view, ParticleVisualLayer layer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _renderer.DrawLayer(batch, view, CollectionsMarshal.AsSpan(_sources), layer);
    }

    /// <summary>Capture lit world particles before scene composition. The
    /// supplied callbacks use the normal scene light map and visibility masks.
    /// Alpha and additive particles must be captured separately: an additive
    /// pass must never composite its black pixels over the scene as alpha art.
    /// Unlit parts retain the direct draw path, and UI is always direct.</summary>
    public int DrawSceneLitLayer(SpriteBatch batch, in ParticleViewport view,
        ParticleVisualLayer layer, Action beginCapture, Action<bool> composeCapture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (layer == ParticleVisualLayer.Ui) return DrawLayer(batch, view, layer);
        var sources = CollectionsMarshal.AsSpan(_sources);
        int drawn = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            var blend = (ParticleBlendMode)pass;
            if (MonoGameParticleRenderer.HasVisibleLit(view, sources, layer, blend))
            {
                beginCapture();
                drawn += _renderer.DrawLayer(batch, view, sources, layer, blend, true);
                composeCapture(blend == ParticleBlendMode.Additive);
            }
            drawn += _renderer.DrawLayer(batch, view, sources, layer, blend, false);
        }
        return drawn;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Presenter.LeaveWorld();
        _environment?.Clear();
        _environment = null;
        _renderer.Dispose();
        _sources.Clear();
    }
}
