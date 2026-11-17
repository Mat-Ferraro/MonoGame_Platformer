using PlatformerSandbox.Foundation.Geometry;

namespace PlatformerSandbox.Foundation.Presentation;

/// <summary>Presentation-only follow tuning. World units are positive-Y-up.
/// Nothing in this type feeds back into authoritative simulation.</summary>
public sealed record CameraFollowSettings
{
    public double DeadZoneHalfWidth { get; init; } = 0.4;
    public double DeadZoneHalfHeight { get; init; } = 0.24;
    public double HorizontalLookAhead { get; init; } = 1.0;
    public double HorizontalSpeedThreshold { get; init; } = 0.25;
    public double FollowSharpness { get; init; } = 9.0;
    public double MaximumFrameSeconds { get; init; } = 0.05;

    public void Validate()
    {
        Check(DeadZoneHalfWidth, nameof(DeadZoneHalfWidth), false);
        Check(DeadZoneHalfHeight, nameof(DeadZoneHalfHeight), false);
        Check(HorizontalLookAhead, nameof(HorizontalLookAhead), false);
        Check(HorizontalSpeedThreshold, nameof(HorizontalSpeedThreshold), false);
        Check(FollowSharpness, nameof(FollowSharpness), true);
        Check(MaximumFrameSeconds, nameof(MaximumFrameSeconds), true);
    }

    private static void Check(double value, string name, bool positive)
    {
        if (!double.IsFinite(value) || (positive ? value <= 0 : value < 0))
            throw new ArgumentOutOfRangeException(name);
    }
}

/// <summary>
/// Smooth camera focus independent of MonoGame and collision physics. Advance
/// using presentation-frame delta; Snap on a new level/respawn as appropriate.
/// </summary>
public sealed class Camera2D
{
    private readonly CameraFollowSettings _settings;
    private Vector2D _focus;
    public bool IsInitialized { get; private set; }
    public Vector2D Focus => _focus;

    public Camera2D(CameraFollowSettings? settings = null)
    {
        _settings = settings ?? new CameraFollowSettings();
        _settings.Validate();
    }

    public void Snap(Vector2D focus)
    {
        _focus = focus;
        IsInitialized = true;
    }

    public void Follow(Vector2D target, Vector2D velocity, double deltaSeconds)
    {
        if (!double.IsFinite(deltaSeconds) || deltaSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
        if (!IsInitialized)
        {
            Snap(target);
            return;
        }
        if (deltaSeconds == 0) return;

        double lookAhead = Math.Abs(velocity.X) >= _settings.HorizontalSpeedThreshold
            ? Math.Sign(velocity.X) * _settings.HorizontalLookAhead : 0;
        double targetX = target.X + lookAhead;
        double desiredX = ApplyDeadZone(_focus.X, targetX, _settings.DeadZoneHalfWidth);
        double desiredY = ApplyDeadZone(_focus.Y, target.Y, _settings.DeadZoneHalfHeight);
        double dt = Math.Min(deltaSeconds, _settings.MaximumFrameSeconds);
        double alpha = 1.0 - Math.Exp(-_settings.FollowSharpness * dt);
        // Vector2D refuses non-finite values, matching the project's numerical policy.
        _focus = new Vector2D(_focus.X + (desiredX - _focus.X) * alpha,
            _focus.Y + (desiredY - _focus.Y) * alpha);
    }

    public Camera2DView GetView(double levelWidth, double levelHeight,
        int viewportWidth, int viewportHeight, double pixelsPerWorldUnit) =>
        Camera2DView.FromFocus(_focus, levelWidth, levelHeight,
            viewportWidth, viewportHeight, pixelsPerWorldUnit);

    private static double ApplyDeadZone(double focus, double target, double halfSize)
    {
        double difference = target - focus;
        return Math.Abs(difference) <= halfSize ? focus :
            target - Math.Sign(difference) * halfSize;
    }
}

/// <summary>
/// Immutable Y-up world to Y-down screen projection. Level 0,0 is bottom-left;
/// camera top/left pixel offsets share LDtk's top-left visual coordinates.
/// </summary>
public readonly record struct Camera2DView(
    double Left, double Bottom, double VisibleWidth, double VisibleHeight,
    double PixelsPerWorldUnit, double LevelHeight,
    int ViewportWidth, int ViewportHeight)
{
    public double CameraLeftPx => Left * PixelsPerWorldUnit;
    public double CameraTopPx =>
        (LevelHeight - Bottom - VisibleHeight) * PixelsPerWorldUnit;

    public static Camera2DView FromFocus(Vector2D focus,
        double levelWidth, double levelHeight, int viewportWidth,
        int viewportHeight, double pixelsPerWorldUnit)
    {
        if (!double.IsFinite(levelWidth) || levelWidth <= 0 ||
            !double.IsFinite(levelHeight) || levelHeight <= 0 ||
            !double.IsFinite(pixelsPerWorldUnit) || pixelsPerWorldUnit <= 0 ||
            viewportWidth <= 0 || viewportHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(levelWidth),
                "Level dimensions, viewport dimensions and pixel density must be positive.");
        double visibleWidth = viewportWidth / pixelsPerWorldUnit;
        double visibleHeight = viewportHeight / pixelsPerWorldUnit;
        return new Camera2DView(
            ViewOrigin(focus.X, levelWidth, visibleWidth),
            ViewOrigin(focus.Y, levelHeight, visibleHeight),
            visibleWidth, visibleHeight, pixelsPerWorldUnit, levelHeight,
            viewportWidth, viewportHeight);
    }

    public CameraScreenRect WorldRect(Aabb box)
    {
        int x = checked((int)Math.Round((box.Left - Left) * PixelsPerWorldUnit));
        int y = checked((int)Math.Round(ViewportHeight -
            (box.Top - Bottom) * PixelsPerWorldUnit));
        int width = Math.Max(1, checked((int)Math.Round(box.Width * PixelsPerWorldUnit)));
        int height = Math.Max(1, checked((int)Math.Round(box.Height * PixelsPerWorldUnit)));
        return new CameraScreenRect(x, y, width, height);
    }

    public Vector2D ScreenToWorld(double screenX, double screenY)
    {
        if (!double.IsFinite(screenX) || !double.IsFinite(screenY))
            throw new ArgumentOutOfRangeException(nameof(screenX));
        return new Vector2D(Left + screenX / PixelsPerWorldUnit,
            Bottom + (ViewportHeight - screenY) / PixelsPerWorldUnit);
    }

    private static double ViewOrigin(double focus, double levelLength,
        double viewportLength) => levelLength <= viewportLength
            ? (levelLength - viewportLength) * 0.5
            : Math.Clamp(focus - viewportLength * 0.5, 0, levelLength - viewportLength);
}

public readonly record struct CameraScreenRect(int X, int Y, int Width, int Height);
