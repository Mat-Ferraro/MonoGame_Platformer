using System;
using System.Collections.Generic;
using System.Linq;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Presentation coordinates are world Y-up. No MonoGame dependency.</summary>
public readonly record struct LightPoint(double X, double Y)
{
    public void Validate()
    {
        if (!double.IsFinite(X) || !double.IsFinite(Y))
            throw new ArgumentOutOfRangeException(nameof(X), "Light coordinates must be finite.");
    }
    public static LightPoint operator +(LightPoint a, LightPoint b) => new(a.X + b.X, a.Y + b.Y);
    public static LightPoint operator -(LightPoint a, LightPoint b) => new(a.X - b.X, a.Y - b.Y);
    public static LightPoint operator *(LightPoint a, double b) => new(a.X * b, a.Y * b);
}

/// <summary>Linear-ish authored RGB multiplier, with no implicit alpha.</summary>
public readonly record struct LightRgb(double R, double G, double B)
{
    public static LightRgb White => new(1, 1, 1);
    public static LightRgb Black => new(0, 0, 0);
    public void Validate(string name = "color")
    {
        if (!double.IsFinite(R) || !double.IsFinite(G) || !double.IsFinite(B) ||
            R < 0 || G < 0 || B < 0 || R > 1 || G > 1 || B > 1)
            throw new ArgumentOutOfRangeException(name, "Light color components must be in [0,1].");
    }
    public LightRgb Clamp() => new(Math.Clamp(R, 0, 1), Math.Clamp(G, 0, 1), Math.Clamp(B, 0, 1));
    public static LightRgb operator +(LightRgb a, LightRgb b) => new(a.R + b.R, a.G + b.G, a.B + b.B);
    public static LightRgb operator *(LightRgb a, double scale) => new(a.R * scale, a.G * scale, a.B * scale);
}

[Flags]
public enum LightChannel { None = 0, Backdrop = 1, Terrain = 2, Hero = 4, Effects = 8, All = 15 }

/// <summary>A radial light. Shadow behavior and emission are opt-in, never inferred from sprite glow.</summary>
public sealed record RadialLight
{
    public string Id { get; init; } = "";
    public LightPoint Position { get; init; }
    public LightRgb Color { get; init; } = LightRgb.White;
    public double Radius { get; init; } = 3.0;
    public double Intensity { get; init; } = 1.0;
    public bool CastShadows { get; init; }
    /// <summary>Optional authored virtual depth for GPU wall lighting. 0 is the legacy plane.
    /// This is NOT LDtk draw-order or the name of a render layer.</summary>
    public double VirtualZ { get; init; }
    /// <summary>Allows this light to reveal the front face of explicitly
    /// silhouetted scenery. Ordinary lights leave silhouettes dark. This does
    /// not change occluder geometry or an object's CastsShadows flag.</summary>
    public bool RevealsSilhouettes { get; init; }
    public int Priority { get; init; } = 0;
    public LightChannel Channels { get; init; } = LightChannel.All;
    public string Owner { get; init; } = "scene";
    /// <summary>Explicit scene-layer illumination targets. Null uses legacy channels.</summary>
    public IReadOnlyList<string>? AffectsLayers { get; init; }
    /// <summary>Independent list of depth planes whose blockers can shadow this light.</summary>
    public IReadOnlyList<string>? OccluderLayers { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 80 ||
            string.IsNullOrWhiteSpace(Owner) || Owner.Length > 80)
            throw new ArgumentException("Light ID and owner must be nonempty (at most 80 characters).");
        Position.Validate();
        if (!double.IsFinite(VirtualZ) || VirtualZ is < -100 or > 100)
            throw new ArgumentOutOfRangeException(nameof(VirtualZ));
        Color.Validate(nameof(Color));
        if (!double.IsFinite(Radius) || Radius <= 0 || Radius > 10000 ||
            !double.IsFinite(Intensity) || Intensity < 0 || Intensity > 4)
            throw new ArgumentOutOfRangeException(nameof(Radius));
        if (Channels == LightChannel.None || (Channels & ~LightChannel.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(Channels));
        if (Priority < -1000 || Priority > 1000)
            throw new ArgumentOutOfRangeException(nameof(Priority));
        foreach (IReadOnlyList<string>? routing in new[] { AffectsLayers, OccluderLayers })
            if (routing is not null && (routing.Count is < 1 or > 32 ||
                routing.Any(id => !SceneLightingLayer.IsId(id)) ||
                routing.Distinct(StringComparer.Ordinal).Count() != routing.Count))
                throw new ArgumentException("Invalid light layer routing selection.");
    }
}

/// <summary>Y-up world camera with no baked link to a game's collision bounds.</summary>
public readonly record struct LightingViewport(int Width, int Height, LightPoint Focus, double PixelsPerUnit)
{
    public void Validate()
    {
        if (Width <= 0 || Height <= 0 || !double.IsFinite(PixelsPerUnit) || PixelsPerUnit <= 0)
            throw new ArgumentOutOfRangeException(nameof(PixelsPerUnit));
        Focus.Validate();
    }

    public LightPoint Project(LightPoint world)
    {
        Validate(); world.Validate();
        return new LightPoint(Width * .5 + (world.X - Focus.X) * PixelsPerUnit,
            Height * .5 - (world.Y - Focus.Y) * PixelsPerUnit);
    }

    public LightPoint Unproject(LightPoint screen)
    {
        Validate(); screen.Validate();
        return new LightPoint(Focus.X + (screen.X - Width * .5) / PixelsPerUnit,
            Focus.Y - (screen.Y - Height * .5) / PixelsPerUnit);
    }

    public bool InfluencesViewport(RadialLight light)
    {
        Validate(); light.Validate();
        LightPoint c = Project(light.Position);
        double r = light.Radius * PixelsPerUnit;
        return c.X + r >= 0 && c.X - r <= Width &&
            c.Y + r >= 0 && c.Y - r <= Height;
    }
}

/// <summary>Stable radial attenuation used in both the visual prototype and headless tests.</summary>
public static class RadialLighting
{
    public static double Attenuation(RadialLight light, LightPoint sample)
    {
        light.Validate(); sample.Validate();
        double dx = sample.X - light.Position.X;
        double dy = sample.Y - light.Position.Y;
        // Avoid unnecessary overflow on huge but finite cosmetic coordinates.
        double d = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(d) || d >= light.Radius) return 0;
        double t = 1 - d / light.Radius;
        return t * t * light.Intensity;
    }
}

/// <summary>Scene-owned, bounded light inventory and stable priority-based visible selection.</summary>
public sealed class LightWorld
{
    private readonly List<RadialLight> _lights = new();
    private readonly int _capacity;
    public int Count => _lights.Count;
    public int RejectedAtCapacity { get; private set; }
    public IReadOnlyList<RadialLight> Lights => _lights;

    public LightWorld(int capacity = 64)
    {
        if (capacity is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public bool TryAdd(RadialLight light)
    {
        ArgumentNullException.ThrowIfNull(light); light.Validate();
        if (_lights.Exists(x => x.Id == light.Id))
            throw new ArgumentException($"Duplicate light '{light.Id}'.", nameof(light));
        if (_lights.Count == _capacity) { RejectedAtCapacity++; return false; }
        _lights.Add(light);
        return true;
    }
    public bool Remove(string id) => _lights.RemoveAll(x => x.Id == id) != 0;
    public int RemoveOwner(string owner) => _lights.RemoveAll(x => x.Owner == owner);
    public void Clear() { _lights.Clear(); RejectedAtCapacity = 0; }
    public void Replace(string id, RadialLight replacement)
    {
        ArgumentNullException.ThrowIfNull(replacement); replacement.Validate();
        int index = _lights.FindIndex(x => x.Id == id);
        if (index < 0 || id != replacement.Id) throw new ArgumentException("Unknown ID or replacement ID mismatch.");
        _lights[index] = replacement;
    }

    public IReadOnlyList<RadialLight> Visible(LightingViewport viewport, int budget = 12)
    {
        viewport.Validate();
        if (budget <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        return _lights.Where(viewport.InfluencesViewport)
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .Take(budget).ToArray();
    }
}
