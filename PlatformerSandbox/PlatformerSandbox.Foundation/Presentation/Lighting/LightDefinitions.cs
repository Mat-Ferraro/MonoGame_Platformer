using PlatformerSandbox.Foundation.Performance;
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

/// <summary>Authorable 2D light shape. Point preserves the existing radial behavior.</summary>
public enum LightShape { Point, Directional, Spot, Tube }

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
    /// <summary>Direction in world Y-up space: direction of incoming light for sun,
    /// forward beam axis for spots and long axis for tube lights.</summary>
    public LightShape Shape { get; init; } = LightShape.Point;
    public LightPoint Direction { get; init; } = new(1, -0.65);
    /// <summary>Full apex angle in degrees; spot only.</summary>
    public double ConeAngleDegrees { get; init; } = 55;
    /// <summary>Fraction of the half-angle reserved for the soft spotlight edge.</summary>
    public double ConeSoftness { get; init; } = 0.15;
    /// <summary>Emitter length/diameter in world units. Production integrates weighted samples.</summary>
    public double TubeLength { get; init; } = 3;
    public AreaEmitterFootprint EmitterFootprint { get; init; } = AreaEmitterFootprint.Line;
    public double EmitterWidth { get; init; } = 2;
    public double PulseDepth { get; init; }
    public double PulseHz { get; init; } = 1;
    public LightRgb? PulseTint { get; init; }
    /// <summary>How far a directional caster projects its 2D silhouette, in world
    /// units. This is a finite artistic control, not the local light radius.
    /// Point/spot/tube lighting ignores this property.</summary>
    public double DirectionalShadowLength { get; init; } = 6;
    public bool CastShadows { get; init; }
    /// <summary>Optional authored virtual depth for GPU wall lighting. 0 is the legacy plane.
    /// This is NOT LDtk draw-order or the name of a render layer.</summary>
    public double VirtualZ { get; init; }
    // Positive virtual Z component toward a directional source (receiver normals only).
    public double ReceiverDirectionalDepth { get; init; } = 1;
    /// <summary>Allows this light to reveal the front face of explicitly
    /// silhouetted scenery. Ordinary lights leave silhouettes dark. This does
    /// not change occluder geometry or an object's CastsShadows flag.</summary>
    public bool RevealsSilhouettes { get; init; }
    public int Priority { get; init; } = 0;
    public LightChannel Channels { get; init; } = LightChannel.All;
    public string Owner { get; init; } = "scene";
    /// <summary>Only the emitting artwork is exempt from blocking its own light.</summary>
    public string? EmitterCasterId { get; init; }
    /// <summary>Optional art/test designation (SUN, MOON, POINT, SPOT, TUBE).
    /// Never changes physics or collision and remains empty in legacy scenes.</summary>
    public string Tag { get; init; } = "";
    /// <summary>Explicit scene-layer illumination targets. Null uses legacy channels.</summary>
    public IReadOnlyList<string>? AffectsLayers { get; init; }
    /// <summary>Independent list of depth planes whose blockers can shadow this light.</summary>
    public IReadOnlyList<string>? OccluderLayers { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 80 ||
            string.IsNullOrWhiteSpace(Owner) || Owner.Length > 80 || Tag is null || Tag.Length > 32)
            throw new ArgumentException("Light ID and owner must be nonempty (at most 80 characters).");
        if (EmitterCasterId is not null && (string.IsNullOrWhiteSpace(EmitterCasterId) || EmitterCasterId.Length > 100))
            throw new ArgumentException("Invalid emissive caster owner.", nameof(EmitterCasterId));
        Position.Validate();
        Direction.Validate();
        if (!Enum.IsDefined(Shape) ||
            (Direction.X * Direction.X + Direction.Y * Direction.Y) < 0.000001 ||
            !double.IsFinite(ConeAngleDegrees) || ConeAngleDegrees is < 5 or > 170 ||
            !double.IsFinite(ConeSoftness) || ConeSoftness is < 0 or > 0.95 ||
            !double.IsFinite(TubeLength) || TubeLength is < 0.1 or > 40 ||
            !double.IsFinite(DirectionalShadowLength) || DirectionalShadowLength is < 0.25 or > 30)
            throw new ArgumentOutOfRangeException(nameof(Shape), "Invalid bounded light shape parameters.");
        if (!Enum.IsDefined(EmitterFootprint) || !double.IsFinite(EmitterWidth) || EmitterWidth is < .1 or > 40 ||
            !double.IsFinite(PulseDepth) || PulseDepth is < 0 or > 1 ||
            !double.IsFinite(PulseHz) || PulseHz is < .01 or > 5)
            throw new ArgumentOutOfRangeException(nameof(EmitterFootprint));
        PulseTint?.Validate(nameof(PulseTint));
        if (!double.IsFinite(ReceiverDirectionalDepth) || ReceiverDirectionalDepth is < -4 or > 4)
            throw new ArgumentOutOfRangeException(nameof(ReceiverDirectionalDepth));
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
        if (EnginePerformanceOptions.LightingValidation)
        {
            ValidateRouting(AffectsLayers);
            ValidateRouting(OccluderLayers);
        }
        else
        {
        foreach (IReadOnlyList<string>? routing in new[] { AffectsLayers, OccluderLayers })
            if (routing is not null && (routing.Count is < 1 or > 32 ||
                routing.Any(id => !SceneLightingLayer.IsId(id)) ||
                routing.Distinct(StringComparer.Ordinal).Count() != routing.Count))
                throw new ArgumentException("Invalid light layer routing selection.");
        }
    }

    private static void ValidateRouting(IReadOnlyList<string>? routing)
    {
        if (routing is null) return;
        if (routing.Count is < 1 or > 32) throw new ArgumentException("Invalid light layer routing selection.");
        for (int i = 0; i < routing.Count; i++)
        {
            if (!SceneLightingLayer.IsId(routing[i])) throw new ArgumentException("Invalid light layer routing selection.");
            for (int j = 0; j < i; j++)
                if (string.Equals(routing[i], routing[j], StringComparison.Ordinal))
                    throw new ArgumentException("Invalid light layer routing selection.");
        }
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
        Validate();
        return ProjectWithValidatedViewport(world);
    }

    /// <summary>For a batch whose caller has already validated this immutable viewport.</summary>
    public LightPoint ProjectWithValidatedViewport(LightPoint world)
    {
        world.Validate();
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
        if (light.Shape == LightShape.Directional) return true; // no local radius
        LightPoint c = Project(light.Position);
        double r = (light.Radius + AreaEmitterSampling.Extent(light)) * PixelsPerUnit;
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
        if (light.Intensity <= 0) return 0;
        if (light.Shape == LightShape.Directional) return light.Intensity;
        double dx = sample.X - light.Position.X;
        double dy = sample.Y - light.Position.Y;
        double magnitude = Math.Sqrt(light.Direction.X * light.Direction.X + light.Direction.Y * light.Direction.Y);
        double dirX = light.Direction.X / magnitude, dirY = light.Direction.Y / magnitude;
        if (light.Shape == LightShape.Tube)
        {
            // Legacy analytic capsule approximation (production expands finite emitters).
            double projection = Math.Clamp(dx * dirX + dy * dirY,
                -light.TubeLength * .5, light.TubeLength * .5);
            dx -= dirX * projection;
            dy -= dirY * projection;
        }
        double d = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(d) || d >= light.Radius) return 0;
        double t = 1 - d / light.Radius;
        double angular = 1;
        if (light.Shape == LightShape.Spot && d > 0.00001)
        {
            double dot = (dx * dirX + dy * dirY) / d;
            double outer = Math.Cos(light.ConeAngleDegrees * Math.PI / 360);
            double inner = Math.Cos(light.ConeAngleDegrees * (1 - light.ConeSoftness) * Math.PI / 360);
            angular = Math.Clamp((dot - outer) / Math.Max(0.00001, inner - outer), 0, 1);
            angular = angular * angular * (3 - 2 * angular);
        }
        return t * t * light.Intensity * angular;
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
