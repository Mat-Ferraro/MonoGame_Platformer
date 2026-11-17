using System;
using System.Collections.Generic;
using System.Linq;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation.Particles;
using PlatformerSandbox.Gameplay.Content;

namespace PlatformerSandbox.Gameplay.Presentation;

/// <summary>
/// Scene-owned, headless environmental VFX. Zones are LDtk authoring rectangles,
/// not colliders. At most MaxActiveZones run at a time. Spawn regions are actual
/// authored bounds, rather than temporary camera-followed P04 ambience.
/// </summary>
public sealed class ParticleZoneEnvironment
{
    public const int MaxActiveZones = 6;
    public const int MaxCapacityPerPart = 256;

    private readonly record struct ZoneEntry(LdtkParticleZone Zone, string EffectId);
    private readonly record struct ZoneCandidate(ZoneEntry Entry, double Distance);
    private readonly ZoneEntry[] _entries;
    private readonly List<ZoneCandidate> _candidates = new(48);
    private readonly HashSet<string> _keep = new(StringComparer.Ordinal);
    private readonly List<string> _stale = new(MaxActiveZones);
    private readonly Dictionary<string, ParticleCompositeInstance> _active =
        new(StringComparer.Ordinal);
    private readonly ParticleCompositeRuntime? _runtime;

    public string LevelId { get; }
    public int TotalZones => _entries.Length;
    public int ActiveZones => _active.Count;
    public int ActiveParticles => _runtime?.Instances.Sum(i =>
        i.Parts.Sum(p => p.Particles.ActiveCount)) ?? 0;
    public long CappedZoneFrames { get; private set; }
    public long DroppedInstances => _runtime?.DroppedAtCapacity ?? 0;
    public IReadOnlyList<ParticleCompositeInstance> Instances =>
        _runtime?.Instances ?? Array.Empty<ParticleCompositeInstance>();

    public ParticleZoneEnvironment(LdtkParticleSceneDefinition scene,
        ParticleCompositeCatalog catalog,
        ParticleFaultPolicy faultPolicy = ParticleFaultPolicy.Strict)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(catalog);
        LevelId = scene.LevelId;
        if (scene.Zones.Count > LdtkParticleZoneImporter.MaxZonesPerLevel)
            throw new ArgumentException("Too many authored particle zones.", nameof(scene));
        var emitters = new List<ParticleEmitterDefinition>();
        var effects = new List<ParticleCompositeDefinition>();
        var entries = new List<ZoneEntry>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int z = 0; z < scene.Zones.Count; z++)
        {
            LdtkParticleZone zone = scene.Zones[z];
            if (zone is null || !zone.Bounds.IsValid ||
                string.IsNullOrWhiteSpace(zone.InstanceId) || !ids.Add(zone.InstanceId) ||
                !double.IsFinite(zone.DensityPercent) ||
                zone.DensityPercent < 1 || zone.DensityPercent > 200 ||
                !double.IsFinite(zone.ActivationPaddingWorld) ||
                zone.ActivationPaddingWorld < 0 || zone.ActivationPaddingWorld > 40)
                throw new ArgumentException("Invalid or duplicate particle zone.", nameof(scene));
            ParticleCompositeDefinition source = catalog.Get(zone.EffectId);
            var parts = new List<ParticleEffectPartDefinition>();
            for (int p = 0; p < source.Parts!.Length; p++)
            {
                ParticleEffectPartDefinition part = source.Parts[p];
                ParticleEmitterDefinition emitter = catalog.Emitters.Get(part.EmitterId);
                if (part.Space != ParticleCoordinateSpace.World ||
                    emitter.RatePerSecond <= 0)
                    throw new ArgumentException("ParticleZone requires a world-space, continuous effect: " +
                        zone.EffectId, nameof(scene));
                double rate = emitter.RatePerSecond * zone.DensityPercent / 100.0;
                if (!double.IsFinite(rate) || rate > 100_000)
                    throw new ArgumentOutOfRangeException(nameof(scene), "Authored particle rate exceeds budget.");
                string emitterId = $"p05.zone{z}.emitter{p}";
                // The emitter itself scatters particles across the entire authored
                // rectangle. This leaves the P03 renderer and P01 physics unchanged.
                emitters.Add(emitter with
                {
                    Id = emitterId, BurstCount = 0, RatePerSecond = rate,
                    Shape = ParticleSpawnShape.Box, SpawnRadius = 0,
                    SpawnOffsetX = new ParticleRange(-zone.Bounds.Width / 2 + part.OffsetX,
                        zone.Bounds.Width / 2 + part.OffsetX),
                    SpawnOffsetY = new ParticleRange(-zone.Bounds.Height / 2 + part.OffsetY,
                        zone.Bounds.Height / 2 + part.OffsetY)
                });
                parts.Add(part with
                {
                    EmitterId = emitterId, DelaySeconds = 0, DurationSeconds = 0,
                    OffsetX = 0, OffsetY = 0, Anchor = ParticleEmitterAnchor.Fixed,
                    Capacity = Math.Min(part.Capacity, MaxCapacityPerPart)
                });
            }
            string effectId = $"p05.zone{z}";
            effects.Add(new ParticleCompositeDefinition { Id = effectId, Parts = parts.ToArray() });
            entries.Add(new ZoneEntry(zone, effectId));
        }
        _entries = entries.ToArray();
        if (_entries.Length > 0)
            _runtime = new ParticleCompositeRuntime(
                new ParticleCompositeCatalog(emitters, effects),
                maxInstances: MaxActiveZones,
                seed: 0xC51E2026UL, faultPolicy: faultPolicy);
    }

    private static double DistanceToBox(Vector2D position, Aabb box)
    {
        double dx = Math.Max(Math.Max(box.Left - position.X, position.X - box.Right), 0);
        double dy = Math.Max(Math.Max(box.Bottom - position.Y, position.Y - box.Top), 0);
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Camera/player-relative activation with one-world-unit hysteresis. Nearer
    /// zones win a stable tie-break; selection never changes gameplay state.
    /// Pausing freezes both emission and culling.
    /// </summary>
    public void Update(Vector2D listener, double deltaSeconds, bool paused)
    {
        if (!double.IsFinite(listener.X) || !double.IsFinite(listener.Y) ||
            !double.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(listener));
        if (_runtime is null) return;
        if (paused)
        {
            _runtime.Update(deltaSeconds, worldPaused: true);
            return;
        }
        _candidates.Clear();
        foreach (ZoneEntry entry in _entries)
        {
            double distance = DistanceToBox(listener, entry.Zone.Bounds);
            if (distance <= entry.Zone.ActivationPaddingWorld +
                (_active.ContainsKey(entry.Zone.InstanceId) ? 1.0 : 0.0))
                _candidates.Add(new ZoneCandidate(entry, distance));
        }
        _candidates.Sort(static (a, b) =>
        {
            int distance = a.Distance.CompareTo(b.Distance);
            return distance != 0 ? distance :
                StringComparer.Ordinal.Compare(a.Entry.Zone.InstanceId,
                    b.Entry.Zone.InstanceId);
        });
        if (_candidates.Count > MaxActiveZones && CappedZoneFrames < long.MaxValue)
            CappedZoneFrames++;
        _keep.Clear();
        int count = Math.Min(_candidates.Count, MaxActiveZones);
        for (int i = 0; i < count; i++)
            _keep.Add(_candidates[i].Entry.Zone.InstanceId);
        _stale.Clear();
        foreach (string activeId in _active.Keys)
            if (!_keep.Contains(activeId)) _stale.Add(activeId);
        foreach (string stale in _stale)
        {
            _runtime.Remove(_active[stale]);
            _active.Remove(stale);
        }
        for (int i = 0; i < count; i++)
        {
            ZoneEntry entry = _candidates[i].Entry;
            if (_active.ContainsKey(entry.Zone.InstanceId)) continue;
            if (_runtime.TryPlay(entry.EffectId, entry.Zone.Bounds.Center,
                    out ParticleCompositeInstance? instance) && instance is not null)
                _active.Add(entry.Zone.InstanceId, instance);
        }
        _runtime.Update(deltaSeconds);
    }

    public void Clear()
    {
        _runtime?.Clear();
        _active.Clear();
    }
}
