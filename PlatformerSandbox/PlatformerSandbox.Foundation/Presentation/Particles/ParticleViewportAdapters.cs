using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation;

namespace PlatformerSandbox.Foundation.Presentation.Particles;

/// <summary>Camera projection adapters: exactly share existing scene coordinates.</summary>
public static class ParticleViewportAdapters
{
    /// <summary>LDtk camera is edge-clamped; using camera focus directly would misalign effects.</summary>
    public static ParticleViewport FromLdtkCamera(in Camera2DView camera) =>
        new(camera.ViewportWidth, camera.ViewportHeight,
            new Vector2D(camera.Left + camera.VisibleWidth * .5,
                camera.Bottom + camera.VisibleHeight * .5),
            camera.PixelsPerWorldUnit);

    /// <summary>Match MovementGymRenderer's centered-X, baseline-Y projection.</summary>
    public static ParticleViewport FromMovementGym(int width, int height,
        double cameraX, double pixelsPerWorldUnit = 72, double groundYFraction = .78)
    {
        if (!double.IsFinite(groundYFraction) || groundYFraction < 0 || groundYFraction > 1)
            throw new ArgumentOutOfRangeException(nameof(groundYFraction));
        var viewport = new ParticleViewport(width, height,
            new Vector2D(cameraX,
                (height * groundYFraction - height * .5) / pixelsPerWorldUnit),
            pixelsPerWorldUnit);
        viewport.Validate();
        return viewport;
    }
}
