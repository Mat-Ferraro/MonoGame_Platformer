using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Gameplay.Content;

/// <summary>One free-position, resizable visual zone authored in LDtk (not collision data).</summary>
public sealed record LdtkParticleZone(
    string InstanceId, string EffectId, Aabb Bounds,
    double DensityPercent, double ActivationPaddingWorld);

public sealed class LdtkParticleSceneDefinition
{
    public LdtkParticleSceneDefinition(string levelId, IReadOnlyList<LdtkParticleZone> zones)
    {
        LevelId = levelId;
        Zones = zones;
    }
    public string LevelId { get; }
    public IReadOnlyList<LdtkParticleZone> Zones { get; }
}

/// <summary>
/// Separate visual-only LDtk importer. No runtime graphics and no mutation of
/// authoritative WorldDefinition. Rectangles are pixel-authored, world-unit converted
/// with the same Y-up convention used by the collision/visual importers.
/// </summary>
public static class LdtkParticleZoneImporter
{
    public const string EntityKind = "ParticleZone";
    public const int MaxZonesPerLevel = 48;

    public static LdtkParticleSceneDefinition FromJson(string json, string levelId)
    {
        if (string.IsNullOrWhiteSpace(json) || string.IsNullOrWhiteSpace(levelId))
            throw new ArgumentException("LDtk JSON and level identifier are required.");
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        if (root.GetProperty("externalLevels").GetBoolean() ||
            root.GetProperty("simplifiedExport").GetBoolean())
            throw new NotSupportedException("Particle authoring requires embedded LDtk levels.");
        JsonElement level = default;
        bool found = false;
        foreach (JsonElement candidate in root.GetProperty("levels").EnumerateArray())
        {
            if (candidate.GetProperty("identifier").GetString() != levelId) continue;
            if (found) throw new ArgumentException("Duplicate LDtk level: " + levelId);
            level = candidate;
            found = true;
        }
        if (!found) throw new ArgumentException("Unknown LDtk level: " + levelId);

        int width = level.GetProperty("pxWid").GetInt32();
        int height = level.GetProperty("pxHei").GetInt32();
        var result = new List<LdtkParticleZone>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement layer in level.GetProperty("layerInstances").EnumerateArray())
        {
            if (layer.GetProperty("__identifier").GetString() != LdtkLevelImporter.EntityLayer)
                continue;
            if (layer.GetProperty("__type").GetString() != "Entities" ||
                layer.GetProperty("__pxTotalOffsetX").GetInt32() != 0 ||
                layer.GetProperty("__pxTotalOffsetY").GetInt32() != 0)
                throw new ArgumentException("Particle entities require an unshifted Entities layer.");
            foreach (JsonElement raw in layer.GetProperty("entityInstances").EnumerateArray())
            {
                if (raw.GetProperty("__identifier").GetString() != EntityKind) continue;
                LdtkAuthoredEntity e = new(raw, width, height);
                if (string.IsNullOrWhiteSpace(e.InstanceId) || !ids.Add(e.InstanceId))
                    throw new ArgumentException("Duplicate/missing ParticleZone IID: " + e.InstanceId);
                if (result.Count >= MaxZonesPerLevel)
                    throw new ArgumentException("Too many ParticleZone entities in " + levelId);
                string effectId = e.RequiredString("EffectId");
                if (effectId.Length > 100 ||
                    !Array.TrueForAll(effectId.ToCharArray(), c =>
                        char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
                    throw new ArgumentException("Invalid particle effect ID: " + effectId);
                result.Add(new LdtkParticleZone(e.InstanceId, effectId, e.Bounds,
                    e.Number("DensityPercent", 1, 200),
                    e.Number("ActivationPaddingWorld", 0, 40)));
            }
        }
        return new LdtkParticleSceneDefinition(levelId,
            new ReadOnlyCollection<LdtkParticleZone>(result));
    }
}
