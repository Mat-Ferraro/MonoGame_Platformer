namespace PlatformerSandbox.Foundation.Audio;

/// <summary>Presentation-only listener in world units, independent of MonoGame and gameplay.</summary>
public readonly record struct AudioListener(double X, double Y);

/// <summary>Sound anchored in world space. Inner radius has full gain; outer radius is silent.</summary>
public readonly record struct AudioEmitterPosition(double X, double Y,
    double InnerRadius = 1.5, double OuterRadius = 16);

public readonly record struct AudioSpatialMix(float Gain, float Pan);

/// <summary>Deterministic mathematical mixing, not simulation authority.</summary>
public static class AudioSpatializer
{
    public static AudioSpatialMix Evaluate(AudioListener? listener, AudioEmitterPosition? emitter)
    {
        if (emitter is not AudioEmitterPosition source)
            return new AudioSpatialMix(1, 0);
        Validate(source);
        if (listener is not AudioListener ear)
            return new AudioSpatialMix(1, 0);
        if (!double.IsFinite(ear.X) || !double.IsFinite(ear.Y))
            throw new ArgumentOutOfRangeException(nameof(listener));
        double dx = source.X - ear.X;
        double dy = source.Y - ear.Y;
        // Outside either axis-aligned radius bound implies outside the audible circle.
        // This also prevents overflow when subtracting extremely large finite coordinates.
        if (Math.Abs(dx) >= source.OuterRadius || Math.Abs(dy) >= source.OuterRadius)
            return new AudioSpatialMix(0, 0);
        // Both components are now bounded by OuterRadius (<= 100,000), so squaring is safe.
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance >= source.OuterRadius) return new AudioSpatialMix(0, 0);
        double distanceFraction = distance <= source.InnerRadius ? 0 :
            (distance - source.InnerRadius) / (source.OuterRadius - source.InnerRadius);
        double falloff = 1 - distanceFraction;
        double pan = Math.Clamp(dx / (source.OuterRadius * .65), -1, 1);
        return new AudioSpatialMix((float)(falloff * falloff), (float)pan);
    }

    public static void Validate(AudioEmitterPosition source)
    {
        if (!double.IsFinite(source.X) || !double.IsFinite(source.Y) ||
            !double.IsFinite(source.InnerRadius) || !double.IsFinite(source.OuterRadius) ||
            source.InnerRadius < 0 || source.OuterRadius <= source.InnerRadius ||
            source.OuterRadius > 100_000)
            throw new ArgumentOutOfRangeException(nameof(source),
                "Emitter positions and attenuation radii must be finite and valid.");
    }
}
