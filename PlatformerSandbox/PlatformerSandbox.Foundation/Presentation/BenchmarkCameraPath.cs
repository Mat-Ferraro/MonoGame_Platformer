using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Foundation.Presentation;

/// <summary>Presentation-only repeatable camera sweep; never moves the player or world.
/// At 60 simulation ticks/second a 240-tick period is four seconds.</summary>
public static class BenchmarkCameraPath
{
    public static Camera2DView View(Camera2DView basis, double levelWidth, double levelHeight, long tick)
    {
        double phase = (tick % 240) * (2 * Math.PI / 240);
        double x = levelWidth * .5 + Math.Max(0, levelWidth - basis.VisibleWidth) * .22 * Math.Sin(phase);
        double y = levelHeight * .5 + Math.Max(0, levelHeight - basis.VisibleHeight) * .22 * Math.Sin(phase);
        return Camera2DView.FromFocus(new Vector2D(x, y), levelWidth, levelHeight,
            basis.ViewportWidth, basis.ViewportHeight, basis.PixelsPerWorldUnit);
    }
    /// <summary>Manual inspection only. Independent axes; does not change the benchmark path.</summary>
    public static Camera2DView ManualView(Camera2DView basis, double levelWidth, double levelHeight,
        long tick, bool horizontal, bool vertical)
    {
        double phase = (tick % 360) * (2 * Math.PI / 360);
        double x = horizontal ? levelWidth * .5 + Math.Max(0, levelWidth - basis.VisibleWidth) * .32 * Math.Sin(phase)
            : basis.Left + basis.VisibleWidth * .5;
        double y = vertical ? levelHeight * .5 + Math.Max(0, levelHeight - basis.VisibleHeight) * .22 * Math.Sin(phase)
            : basis.Bottom + basis.VisibleHeight * .5;
        return Camera2DView.FromFocus(new Vector2D(x, y), levelWidth, levelHeight,
            basis.ViewportWidth, basis.ViewportHeight, basis.PixelsPerWorldUnit);
    }
}
