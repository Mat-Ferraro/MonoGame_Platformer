using System;
using System.Collections.Generic;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Abilities;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.World;

namespace PlatformerSandbox.Gameplay.Presentation;

/// <summary>
/// One-way, scene-scoped lighting presentation model. Never writes to gameplay.
/// Transient event lights are accepted exactly once after a committed tick.
/// </summary>
public sealed class GameplayLightingPresenter
{
    // Only a Pulse flash follows a moving player. Landing/respawn flashes remain
    // anchored to their committed event positions. This state is presentation-only.
    private sealed record Flash(RadialLight Light, double Remaining, int? FollowPlayerId);
    private readonly List<Flash> _flashes = new();
    private readonly HashSet<(long World, long Tick, int Sequence)> _seen = new();
    private readonly Queue<(long World, long Tick, int Sequence)> _history = new();
    private const int HistoryLimit = 512;
    private LdtkLightingScene? _scene;
    private long _worldId;
    private int _flashSequence;
    private double _presentationLightClock;
    private double _occluderAnimationClock;
    public bool WallMotionEnabled { get; set; } = true;
    private LdtkLightingScene? _cachedOccluderScene;
    private double _cachedOccluderSeconds = -1;
    public int LastDynamicHullsUpdated { get; private set; }
    public int LastStaticHullsReused { get; private set; }
    public LightingQuality Quality { get; set; } = LightingQuality.Balanced;
    public bool Enabled { get; set; } = true;
    public bool ReducedFlashes { get; set; }
    public int ActiveFlashes => _flashes.Count;
    public int SceneLightCount => _scene?.Lights.Count ?? 0;
    public int SceneBlockerCount => _scene?.Occluders.Count ?? 0;
    public long DroppedFlashes { get; private set; }
    public LdtkLightingScene? Scene => _scene;

    public void EnterWorld(long worldInstanceId, LdtkLightingScene? scene)
    {
        if (worldInstanceId <= 0) throw new ArgumentOutOfRangeException(nameof(worldInstanceId));
        Clear();
        _scene = scene;
        _worldId = worldInstanceId;
    }

    public void Clear()
    {
        _scene = null;
        _worldId = 0;
        _flashes.Clear();
        _seen.Clear();
        _history.Clear();
        _flashSequence = 0;
        _presentationLightClock = 0;
        _occluderAnimationClock = 0;
        WallMotionEnabled = true;
        _cachedOccluderScene = null;
        _cachedOccluderSeconds = -1;
        LastDynamicHullsUpdated = 0;
        LastStaticHullsReused = 0;
    }

    public void Present(WorldGameplayEvent e)
    {
        if (!Enabled || _scene is null || e.WorldInstanceId != _worldId ||
            e.PlayerId <= 0) return;
        var key = (e.WorldInstanceId, e.WorldTick, e.Sequence);
        if (!_seen.Add(key)) return;
        _history.Enqueue(key);
        if (_history.Count > HistoryLimit) _seen.Remove(_history.Dequeue());
        // Do not leave player-attached light proxies behind after a player leaves
        // or respawns. Respawn's separate green flash is still spawned below.
        if (e.Kind is WorldGameplayEventKind.PlayerRemoved or
            WorldGameplayEventKind.PlayerRespawned)
            _flashes.RemoveAll(f => f.FollowPlayerId == e.PlayerId);
        if (e.Kind == WorldGameplayEventKind.PlayerRemoved) return;
        LightRgb rgb;
        double radius, intensity, duration;
        bool frontFacingPulse = e.Kind == WorldGameplayEventKind.AbilityActivated &&
            e.AbilityId == CharacterAbilityId.Pulse;
        switch (e.Kind)
        {
            case WorldGameplayEventKind.AbilityActivated when e.AbilityId == CharacterAbilityId.Pulse:
                rgb = new LightRgb(.18, .66, 1); radius = 4.2; intensity = 1.25; duration = .24;
                break;
            case WorldGameplayEventKind.PlayerRespawned:
                rgb = new LightRgb(.42, 1, .78); radius = 3.2; intensity = 1.05; duration = .34;
                break;
            case WorldGameplayEventKind.Landed:
                rgb = new LightRgb(.95, .74, .5); radius = 1.75; intensity = .35; duration = .11;
                break;
            default: return;
        }
        if (_flashes.Count >= 12) { DroppedFlashes++; return; }
        _flashes.Add(new Flash(new RadialLight
        {
            Id = "flash_" + (++_flashSequence),
            Owner = "flash",
            Position = new LightPoint(e.Position.X, e.Position.Y),
            Color = rgb, Radius = radius, Intensity = intensity,
            Priority = 15,
            Channels = LightChannel.Terrain | LightChannel.Hero | LightChannel.Effects,
            // The dash light lives at the gameplay plane and reaches its
            // immediately-forward depth plane too. Keep existing background
            // illumination, but exclude remote foreground planes and blockers
            // behind the player from this front-facing light.
            AffectsLayers = frontFacingPulse ? PulseTargets(_scene.Layers) : null,
            OccluderLayers = frontFacingPulse ? PulseOccluders(_scene.Layers) : null,
            RevealsSilhouettes = frontFacingPulse,
            CastShadows = frontFacingPulse
        }, duration, frontFacingPulse ? e.PlayerId : null));
    }

    /// <summary>
    /// Update moving gameplay flashes from the already-committed world snapshot.
    /// A Pulse light must travel with the character instead of remaining at the
    /// activation point while a dash crosses or overlaps authored shadow hulls.
    /// Never writes to WorldSession and never predicts uncommitted motion.
    /// </summary>
    public void FollowCommittedPlayers(WorldStepResult committed)
    {
        ArgumentNullException.ThrowIfNull(committed);
        if (!Enabled || _scene is null || _flashes.Count == 0) return;
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            Flash flash = _flashes[i];
            if (flash.FollowPlayerId is not int playerId) continue;
            int playerIndex = -1;
            for (int j = 0; j < committed.Players.Count; j++)
                if (committed.Players[j].PlayerId == playerId)
                { playerIndex = j; break; }
            if (playerIndex < 0)
            {
                _flashes.RemoveAt(i); // No ghost light after a player is removed.
                continue;
            }
            var center = committed.Players[playerIndex].Bounds.Center;
            _flashes[i] = flash with
            {
                Light = flash.Light with { Position = new LightPoint(center.X, center.Y) }
            };
        }
    }

    private static SceneLightingLayer? ClosestForeground(SceneLightingLayerSet layers) =>
        layers.Layers.FirstOrDefault(layer => layer.Group == RenderLayerGroup.Foreground);

    /// <summary>Preserve all existing background contributions while lighting
    /// the hero/gameplay plane and only the nearest foreground depth plane.</summary>
    private static string[] PulseTargets(SceneLightingLayerSet layers)
    {
        string? nearest = ClosestForeground(layers)?.Id;
        return layers.Layers.Where(layer => layer.Group != RenderLayerGroup.Foreground ||
            layer.Id == nearest).Select(layer => layer.Id).ToArray();
    }

    /// <summary>Foreground and gameplay blockers occlude a light emitted in
    /// front of the gameplay plane; background scenery cannot self-shadow it.</summary>
    private static string[] PulseOccluders(SceneLightingLayerSet layers)
    {
        string? nearest = ClosestForeground(layers)?.Id;
        return layers.Layers.Where(layer => layer.Group == RenderLayerGroup.Gameplay ||
            layer.Id == nearest).Select(layer => layer.Id).ToArray();
    }

    public void Update(double seconds, bool worldPaused)
    {
        if (!double.IsFinite(seconds) || seconds < 0)
            throw new ArgumentOutOfRangeException(nameof(seconds));
        if (worldPaused) return;
        double dt = Math.Min(.1, seconds);
        // Authored lights animate on the presentation clock, never in WorldSession.
        // Clamp long frames consistently with the existing flash presentation clock.
        _presentationLightClock += dt;
        if (WallMotionEnabled) _occluderAnimationClock += dt;
        for (int i = _flashes.Count - 1; i >= 0; i--)
        {
            Flash flash = _flashes[i] with { Remaining = _flashes[i].Remaining - dt };
            if (flash.Remaining <= 0) _flashes.RemoveAt(i);
            else _flashes[i] = flash;
        }
    }

    /// <summary>
    /// A frame-consistent scene snapshot for both wall artwork and shadow rendering.
    /// Imported static hulls are reused by reference. Only animated hulls are rebuilt.
    /// Cached during pause and within the same presentation frame.
    /// </summary>
    public LdtkLightingScene? CurrentScene()
    {
        if (_scene is null) return null;
        if (_scene.OccluderMotions.Count == 0) return _scene;
        if (_cachedOccluderScene is not null &&
            _cachedOccluderSeconds == _occluderAnimationClock)
            return _cachedOccluderScene;
        var current = new List<LightingOccluder>(_scene.Occluders.Count);
        var centers = new Dictionary<string, LightPoint>(StringComparer.Ordinal);
        foreach (var entry in _scene.SurfaceCenters)
            centers.Add(entry.Key, entry.Value);
        int dynamic = 0;
        var updatedCenters = new HashSet<string>(StringComparer.Ordinal);
        foreach (LightingOccluder hull in _scene.Occluders)
        {
            if (_scene.OccluderMotions.TryGetValue(hull.Owner, out var motion))
            {
                current.Add(motion.Apply(hull, _occluderAnimationClock));
                if (updatedCenters.Add(hull.Owner) && centers.TryGetValue(hull.Owner, out LightPoint center))
                    centers[hull.Owner] = motion.At(_occluderAnimationClock).Transform(center, motion.Anchor);
                dynamic++;
            }
            else current.Add(hull);
        }
        LastDynamicHullsUpdated = dynamic;
        LastStaticHullsReused = current.Count - dynamic;
        _cachedOccluderSeconds = _occluderAnimationClock;
        return _cachedOccluderScene = new LdtkLightingScene(_scene.LevelId,
            _scene.Lights, current.AsReadOnly(), _scene.Surfaces, centers,
            _scene.Layers, _scene.LightMotions, _scene.OccluderMotions,
            _occluderAnimationClock);
    }

    /// <summary>Additional lights are bounded particle/emitter proxies, not individual particles.</summary>
    /// <summary>The current authored light positions for the developer UI.
    /// Uses presentation time, not world/simulation time.</summary>
    public IReadOnlyList<RadialLight> CurrentAuthoredLights()
    {
        if (_scene is null) return Array.Empty<RadialLight>();
        var result = new List<RadialLight>(_scene.Lights.Count);
        foreach (RadialLight light in _scene.Lights)
            result.Add(PresentationLightPulse.At(_scene.LightMotions.TryGetValue(light.Id, out PresentationLightMotion motion)
                ? light with { Position = motion.At(light.Position, _presentationLightClock) }
                : light, _presentationLightClock));
        return result;
    }

    public IReadOnlyList<RadialLight> BuildVisible(LightingViewport viewport,
        IEnumerable<RadialLight>? particleLights = null,
        IEnumerable<RadialLight>? debugLights = null, string? filterTag = null)
    {
        if (!Enabled || _scene is null) return Array.Empty<RadialLight>();
        LightingFrameBudget budget = LightingFrameBudget.For(Quality);
        var all = new List<RadialLight>(_scene.Lights.Count + _flashes.Count + 6);
        all.AddRange(CurrentAuthoredLights());
        foreach (Flash flash in _flashes)
            all.Add(flash.Light);
        // Use the same animated snapshot as artwork and shadow hulls.
        var current = CurrentScene()!;
        foreach (var (id, surface) in current.Surfaces)
            if (surface.EmissiveLightRadius > 0 && current.SurfaceCenters.TryGetValue(id, out var position) &&
                EmissiveWorldLighting.Create(id, position, surface) is RadialLight emitter)
                all.Add(emitter);
        if (particleLights is not null)
        {
            int added = 0;
            foreach (RadialLight light in particleLights)
            {
                if (added >= budget.MaxParticleLights) break;
                if (light.Owner != "particle")
                    throw new ArgumentException("Particle lighting requires particle-owned lights.");
                all.Add(light);
                added++;
            }
        }
        if (debugLights is not null)
            foreach (RadialLight light in debugLights)
            {
                if (light.Owner != "stress")
                    throw new ArgumentException("Debug lights must be stress-owned.");
                all.Add(light);
                if (all.Count > 256) break;
            }
        string[]? tags = filterTag?.Split('|');
        return LightingFramePlanner.Select(tags is null ? all : all.Where(l => tags.Contains(l.Tag, StringComparer.Ordinal)),
            viewport, budget, ReducedFlashes);
    }
}
