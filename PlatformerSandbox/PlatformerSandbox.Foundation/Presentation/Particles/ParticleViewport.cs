using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Foundation.Presentation.Particles;

/// <summary>Visual compositing plane, in back-to-front order. Not a gameplay layer.</summary>
public enum ParticleVisualLayer { Background, World, Foreground, Ui }

/// <summary>World particles are Y-up world units; UI particles are Y-down pixels.</summary>
public enum ParticleCoordinateSpace { World, Screen }

/// <summary>All particle textures and tint colors use premultiplied alpha.</summary>
public enum ParticleBlendMode { Alpha, Additive }

/// <summary>World particles receive scene illumination by default, independent
/// of blend mode. Unlit is an explicit artistic opt-out, not a light emitter.</summary>
public enum ParticleLightingMode { Scene, Unlit }

public static class ParticleLightingPolicy
{
    public static bool ReceivesSceneLight(ParticleCoordinateSpace space, ParticleLightingMode mode) =>
        space == ParticleCoordinateSpace.World && mode == ParticleLightingMode.Scene;
}

/// <summary>Shared procedural sprites; no per-particle texture allocations.</summary>
public enum ParticleSpriteShape { SoftDisc, SolidDisc, Spark, Square }

public readonly record struct ParticleScreenPoint(double X, double Y);

/// <summary>
/// Headless, unbounded viewport projection for cosmetics. Unlike a level camera,
/// this can pan past the scene and change zoom. World +Y is up, screen +Y down.
/// The viewport center is the camera focus in world units.
/// </summary>
public readonly record struct ParticleViewport(
    int Width, int Height, Vector2D CameraCenter, double PixelsPerWorldUnit)
{
    public void Validate()
    {
        if (Width <= 0 || Height <= 0 ||
            !double.IsFinite(PixelsPerWorldUnit) || PixelsPerWorldUnit <= 0 ||
            !double.IsFinite(CameraCenter.X) || !double.IsFinite(CameraCenter.Y))
            throw new ArgumentOutOfRangeException(nameof(PixelsPerWorldUnit),
                "Viewport dimensions and world pixel density must be finite and positive.");
    }

    public ParticleScreenPoint ToScreen(Vector2D position, ParticleCoordinateSpace space)
    {
        Validate();
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) ||
            !Enum.IsDefined(space))
            throw new ArgumentOutOfRangeException(nameof(position));
        return space == ParticleCoordinateSpace.Screen
            ? new ParticleScreenPoint(position.X, position.Y)
            : new ParticleScreenPoint(Width * .5 + (position.X - CameraCenter.X) * PixelsPerWorldUnit,
                Height * .5 - (position.Y - CameraCenter.Y) * PixelsPerWorldUnit);
    }

    public Vector2D ToWorld(double screenX, double screenY)
    {
        Validate();
        if (!double.IsFinite(screenX) || !double.IsFinite(screenY))
            throw new ArgumentOutOfRangeException(nameof(screenX));
        return new Vector2D(CameraCenter.X + (screenX - Width * .5) / PixelsPerWorldUnit,
            CameraCenter.Y - (screenY - Height * .5) / PixelsPerWorldUnit);
    }

    public double SizePixels(double size, ParticleCoordinateSpace space)
    {
        Validate();
        if (!double.IsFinite(size) || size < 0 || !Enum.IsDefined(space))
            throw new ArgumentOutOfRangeException(nameof(size));
        return space == ParticleCoordinateSpace.World ? size * PixelsPerWorldUnit : size;
    }

    public bool IsVisible(ParticleScreenPoint center, double diameterPixels)
    {
        Validate();
        if (!double.IsFinite(center.X) || !double.IsFinite(center.Y) ||
            !double.IsFinite(diameterPixels) || diameterPixels < 0)
            return false;
        double radius = diameterPixels * .5;
        return center.X + radius >= 0 && center.X - radius <= Width &&
            center.Y + radius >= 0 && center.Y - radius <= Height;
    }
}

/// <summary>
/// Deliberate stable ordering: alpha and additive are separate passes *within*
/// each named layer. Systems preserve insertion order within each pass.
/// </summary>
public static class ParticleRenderOrdering
{
    public static int PassKey(ParticleVisualLayer layer, ParticleBlendMode blend)
    {
        if (!Enum.IsDefined(layer)) throw new ArgumentOutOfRangeException(nameof(layer));
        if (!Enum.IsDefined(blend)) throw new ArgumentOutOfRangeException(nameof(blend));
        return (int)layer * 2 + (int)blend;
    }
}

/// <summary>
/// Convert unpremultiplied authoring colors into MonoGame's default
/// premultiplied-alpha vertex-color convention, without a graphics dependency.
/// </summary>
public readonly record struct ParticlePremultipliedRgba(byte R, byte G, byte B, byte A)
{
    public static ParticlePremultipliedRgba From(ParticleColor color)
    {
        color.Validate(nameof(color));
        static byte Byte(double x) => (byte)Math.Clamp((int)Math.Round(x * 255), 0, 255);
        return new ParticlePremultipliedRgba(
            Byte(color.R * color.A), Byte(color.G * color.A),
            Byte(color.B * color.A), Byte(color.A));
    }
}
