using System;
using System.Collections.Generic;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

public enum LightingMaterialMode { Lit, Unlit }

public sealed record LightingMaterialDefinition
{
    public string Id { get; init; } = "";
    public LightingMaterialMode Mode { get; init; } = LightingMaterialMode.Lit;
    public LightChannel Channel { get; init; } = LightChannel.Terrain;
    public string Albedo { get; init; } = "";
    public string? Normal { get; init; }
    public string? Emission { get; init; }
    public string? Mask { get; init; }
    public LightPoint Center { get; init; }
    public double Width { get; init; } = 1;
    public double Height { get; init; } = 1;
    public double RotationDegrees { get; init; }
    public bool FlipX { get; init; }
    /// <summary>
    /// Controls whether world-space occluders shadow this material receiver.
    /// Foreground artwork in front of the 2D occluder plane should opt out.
    /// Defaults to true to retain existing material behavior.
    /// </summary>
    public bool ReceivesOccluderShadows { get; init; } = true;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 80) throw new ArgumentException("Material ID missing.");
        if (!Enum.IsDefined(Mode) || Channel == LightChannel.None || (Channel & ~LightChannel.All) != 0)
            throw new ArgumentOutOfRangeException(nameof(Channel));
        ValidateAsset(Albedo, required: true);
        ValidateAsset(Normal);
        ValidateAsset(Emission);
        ValidateAsset(Mask);
        Center.Validate();
        if (!double.IsFinite(Width) || !double.IsFinite(Height) ||
            Width <= 0 || Height <= 0 || Width > 10000 || Height > 10000 ||
            !double.IsFinite(RotationDegrees) || Math.Abs(RotationDegrees) > 360)
            throw new ArgumentOutOfRangeException(nameof(Width));
    }

    private static void ValidateAsset(string? path, bool required = false)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            if (required) throw new ArgumentException("Albedo asset required.");
            return;
        }
        if (path.Contains("..", StringComparison.Ordinal) ||
            path.Contains('/') || path.Contains('\\') || path.Contains(':') || path.Length > 100 ||
            !path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Material asset must be a local PNG filename.");
    }
}

/// <summary>CPU reference shader for the playground; neutral normals and missing maps are inexpensive defaults.</summary>
public static class LightingMaterialSampler
{
    public static readonly LightRgb NeutralNormal = new(.5, .5, 1);

    public static LightRgb Shade(LightingMaterialDefinition material, LightRgb albedo,
        LightRgb? normalTexel, LightRgb? emissionTexel, double lightMask,
        LightPoint point, LightRgb ambient, IReadOnlyList<RadialLight> lights,
        IReadOnlyList<LightingOccluder>? occluders = null)
    {
        material.Validate(); albedo.Validate(); ambient.Validate(); point.Validate();
        if (!double.IsFinite(lightMask) || lightMask < 0 || lightMask > 1)
            throw new ArgumentOutOfRangeException(nameof(lightMask));
        LightRgb emission = emissionTexel ?? LightRgb.Black;
        emission.Validate();
        if (material.Mode == LightingMaterialMode.Unlit)
            return (albedo + emission).Clamp();

        LightRgb encoded = normalTexel ?? NeutralNormal;
        encoded.Validate();
        (double nx, double ny, double nz) = DecodeNormal(encoded,
            material.RotationDegrees, material.FlipX);
        LightRgb illumination = ambient;
        foreach (RadialLight light in lights)
        {
            if ((light.Channels & material.Channel) == 0) continue;
            double falloff = RadialLighting.Attenuation(light, point);
            if (falloff <= 0 || (light.CastShadows && material.ReceivesOccluderShadows &&
                occluders is not null && ShadowGeometry.IsOccluded(light.Position, point, occluders)))
                continue;
            double dx = light.Position.X - point.X, dy = light.Position.Y - point.Y;
            // A small fixed virtual light height provides a planar material preview.
            const double height = 1.5;
            double length = Math.Sqrt(dx * dx + dy * dy + height * height);
            double lambert = Math.Max(0, (nx * dx + ny * dy + nz * height) / length);
            illumination += light.Color * (falloff * lambert * lightMask);
        }
        return new LightRgb(albedo.R * illumination.R + emission.R,
            albedo.G * illumination.G + emission.G,
            albedo.B * illumination.B + emission.B).Clamp();
    }

    /// <summary>Apply horizontal mirroring in tangent space BEFORE world rotation.</summary>
    public static (double X, double Y, double Z) DecodeNormal(
        LightRgb texel, double angleDegrees, bool flipX)
    {
        texel.Validate();
        if (!double.IsFinite(angleDegrees)) throw new ArgumentOutOfRangeException(nameof(angleDegrees));
        double nx = (texel.R * 2 - 1) * (flipX ? -1 : 1);
        double ny = texel.G * 2 - 1;
        double nz = texel.B * 2 - 1;
        double radians = angleDegrees * (Math.PI / 180);
        double x = nx * Math.Cos(radians) - ny * Math.Sin(radians);
        double y = nx * Math.Sin(radians) + ny * Math.Cos(radians);
        double length = Math.Sqrt(x * x + y * y + nz * nz);
        return length <= 1e-10 ? (0, 0, 1) : (x / length, y / length, nz / length);
    }
}
