using System;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>Explicit material participation for the isolated basic-lighting renderer.</summary>
public enum BasicSurfaceLightingMode
{
    Lit,
    Unlit
}

/// <summary>
/// Receiver channels are independent of draw order and visibility. A Lit surface
/// with None still receives ambient light; Unlit bypasses both ambient and lamps.
/// </summary>
[Flags]
public enum BasicLightReceiverChannels
{
    None = 0,
    Light1 = 1,
    Light2 = 2,
    Both = Light1 | Light2
}

/// <summary>Stage-5 GPU shading response; independent of Lit/Unlit and receiver channels.</summary>
public enum BasicSurfaceShadingMode { Flat, Normal, Rim, Hybrid }

/// <summary>
/// Material properties are independent of object alpha. Opacity is applied once,
/// while compositing the fully rendered surface, not once for every draw primitive.
/// </summary>
public readonly record struct BasicSurfaceMaterial(
    BasicSurfaceLightingMode Mode,
    BasicLightReceiverChannels Receives,
    double Opacity)
{
    public BasicSurfaceShadingMode Shading { get; init; } = BasicSurfaceShadingMode.Flat;
    public double NormalStrength { get; init; } = 1.0;
    public double RimStrength { get; init; } = 1.0;
    /// <summary>Independent of whether the surface itself casts a shadow.</summary>
    public bool ReceivesShadows { get; init; } = true;

    public bool ReceivesAmbient => Mode == BasicSurfaceLightingMode.Lit;

    /// <summary>Lit is independent of the set of local light channels.</summary>
    public bool ReceivesLight(BasicLightReceiverChannels channel) =>
        Mode == BasicSurfaceLightingMode.Lit && (Receives & channel) != 0;

    public void Validate()
    {
        if (!Enum.IsDefined(Mode))
            throw new ArgumentOutOfRangeException(nameof(Mode));
        if ((Receives & ~BasicLightReceiverChannels.Both) != 0)
            throw new ArgumentOutOfRangeException(nameof(Receives));
        if (!double.IsFinite(Opacity) || Opacity < 0 || Opacity > 1)
            throw new ArgumentOutOfRangeException(nameof(Opacity));
        if (!Enum.IsDefined(Shading))
            throw new ArgumentOutOfRangeException(nameof(Shading));
        if (!double.IsFinite(NormalStrength) || NormalStrength < 0 || NormalStrength > 1)
            throw new ArgumentOutOfRangeException(nameof(NormalStrength));
        if (!double.IsFinite(RimStrength) || RimStrength < 0 || RimStrength > 2)
            throw new ArgumentOutOfRangeException(nameof(RimStrength));
    }
}

/// <summary>A light and its authored receiver channel. Never infer from list index.</summary>
public readonly record struct BasicMaterialLight(RadialLight Light,
    BasicLightReceiverChannels Channel)
{
    public void Validate()
    {
        if (Light is null) throw new ArgumentNullException(nameof(Light));
        Light.Validate();
        if (Channel is not BasicLightReceiverChannels.Light1 and not BasicLightReceiverChannels.Light2)
            throw new ArgumentOutOfRangeException(nameof(Channel));
    }
}
