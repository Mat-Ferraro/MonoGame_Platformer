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


    public PlatformerParticleVfx(GraphicsDevice device, string particleFolder)
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
        _renderer = new MonoGameParticleRenderer(device);
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
                def.Space, def.Blend, def.Sprite));
        }
    }

    /// <summary>Only call while SpriteBatch is *not* begun.</summary>
    public int DrawLayer(SpriteBatch batch, in ParticleViewport view, ParticleVisualLayer layer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _renderer.DrawLayer(batch, view, CollectionsMarshal.AsSpan(_sources), layer);
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
