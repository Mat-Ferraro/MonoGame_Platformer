using System;

namespace PlatformerSandbox.Foundation.Presentation.Lighting;

/// <summary>
/// Conservative viewport-clipped pixel rectangle for a single authored material.
/// Pure presentation geometry: no GPU or simulation dependencies.
/// The rectangle is intentionally padded for sprite rasterization, sampling
/// differences and the shader's inward edge-rim calculation.
/// </summary>
public readonly record struct ProductionMaterialRegion(int X, int Y, int Width, int Height)
{
    public bool IsEmpty => Width <= 0 || Height <= 0;
    public long Area => IsEmpty ? 0L : (long)Width * Height;
    public static ProductionMaterialRegion Full(int width, int height) =>
        new(0, 0, width, height);
}

public static class ProductionMaterialRegionPlanner
{
    /// <summary>
    /// The anchor (x, y), unrotated width/height, pivot and rotation must be
    /// exactly those passed to SpriteBatch.Draw. Positive rotation follows
    /// screen-space Y-down coordinates. Mirrors do not change outer bounds.
    /// On non-finite authored transforms, conservatively use the full viewport
    /// instead of accidentally clipping an effect. No sampling is shifted:
    /// callers still draw the original full-viewport source texture under a
    /// scissor rectangle, then composite the corresponding same-pixel region.
    /// </summary>
    public static ProductionMaterialRegion ForSprite(double x, double y,
        double width, double height, double pivotX, double pivotY,
        double rotationDegrees, int viewportWidth, int viewportHeight,
        int paddingPixels = 3)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(viewportWidth));
        if (paddingPixels is < 0 or > 128)
            throw new ArgumentOutOfRangeException(nameof(paddingPixels));

        if (!double.IsFinite(x) || !double.IsFinite(y) ||
            !double.IsFinite(width) || !double.IsFinite(height) ||
            !double.IsFinite(pivotX) || !double.IsFinite(pivotY) ||
            !double.IsFinite(rotationDegrees) || width < 0 || height < 0 ||
            pivotX is < 0 or > 1 || pivotY is < 0 or > 1)
            return ProductionMaterialRegion.Full(viewportWidth, viewportHeight);
        if (width == 0 || height == 0) return default;

        double radians = (rotationDegrees % 360) * (Math.PI / 180);
        double cosine = Math.Cos(radians), sine = Math.Sin(radians);
        double left = -width * pivotX, right = width * (1 - pivotX);
        double top = -height * pivotY, bottom = height * (1 - pivotY);
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        // Four corners with no per-material managed allocations.
        for (int xi = 0; xi < 2; xi++)
        for (int yi = 0; yi < 2; yi++)
        {
            double localX = xi == 0 ? left : right;
            double localY = yi == 0 ? top : bottom;
            double px = x + cosine * localX - sine * localY;
            double py = y + sine * localX + cosine * localY;
            minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
            minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
        }
        if (!double.IsFinite(minX) || !double.IsFinite(maxX) ||
            !double.IsFinite(minY) || !double.IsFinite(maxY))
            return ProductionMaterialRegion.Full(viewportWidth, viewportHeight);

        // Clamp as doubles before casting to int: remote/off-camera authored
        // geometry must never overflow conversion or become a negative region.
        int x0 = (int)Math.Clamp(Math.Floor(minX - paddingPixels), 0, viewportWidth);
        int y0 = (int)Math.Clamp(Math.Floor(minY - paddingPixels), 0, viewportHeight);
        int x1 = (int)Math.Clamp(Math.Ceiling(maxX + paddingPixels), 0, viewportWidth);
        int y1 = (int)Math.Clamp(Math.Ceiling(maxY + paddingPixels), 0, viewportHeight);
        return x1 <= x0 || y1 <= y0 ? default :
            new ProductionMaterialRegion(x0, y0, x1 - x0, y1 - y0);
    }
}
