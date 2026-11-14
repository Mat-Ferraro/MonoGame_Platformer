using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Simulation.Diagnostics;

namespace PlatformerSandbox.Gyms;

/// <summary>
/// Presentation-only adapter from world-space AABBs (positive Y up) to screen
/// rectangles (positive Y down). Never changes authoritative simulation state.
/// No texture files, fonts, or content pipeline registrations are needed.
/// </summary>
internal static class MovementGymRenderer
{
    private const float PixelsPerWorldUnit = 72f;
    private static readonly Color GridColor = new(32, 42, 58);
    private static readonly Color FloorColor = new(100, 118, 143);
    private static readonly Color ElevatedColor = new(126, 113, 179);
    private static readonly Color GroundedPlayer = new(68, 214, 177);
    private static readonly Color AirbornePlayer = new(251, 177, 106);
    private static readonly Color MovingFloor = new(242, 170, 72);

    public static void Draw(
        SpriteBatch batch, Texture2D pixel, Viewport viewport,
        MovementGymSession gym, bool paused)
    {
        double centerX = gym.PlayerBounds.Center.X;
        float originX = viewport.Width * 0.5f;
        float groundY = viewport.Height * 0.78f;

        // Camera tracking is strictly visual. The player remains in world units.
        int visibleHalfWidth = (int)Math.Ceiling(viewport.Width /
            (2.0 * PixelsPerWorldUnit)) + 1;
        int firstX = (int)Math.Floor(centerX) - visibleHalfWidth;
        int lastX = (int)Math.Ceiling(centerX) + visibleHalfWidth;
        for (int x = firstX; x <= lastX; x++)
        {
            int screenX = (int)Math.Round(originX + (x - centerX) * PixelsPerWorldUnit);
            batch.Draw(pixel, new Rectangle(screenX, 0, 1, viewport.Height), GridColor);
        }
        for (int y = -4; y <= 12; y++)
        {
            int screenY = (int)Math.Round(groundY - y * PixelsPerWorldUnit);
            if (screenY >= 0 && screenY < viewport.Height)
                batch.Draw(pixel, new Rectangle(0, screenY, viewport.Width, 1), GridColor);
        }

        foreach (var collider in gym.Colliders)
        {
            Rectangle rect = WorldToScreen(collider.Bounds, centerX,
                originX, groundY);
            if (rect.Right < 0 || rect.Left > viewport.Width ||
                rect.Bottom < 0 || rect.Top > viewport.Height)
                continue;

            Color color = collider.Id <= 2 ? FloorColor : ElevatedColor;
            batch.Draw(pixel, rect, color);
            DrawOutline(batch, pixel, rect, Color.White * 0.22f);
        }

        Rectangle moving = WorldToScreen(gym.MovingPlatform.Bounds, centerX,
            originX, groundY);
        if (moving.Right >= 0 && moving.Left <= viewport.Width &&
            moving.Bottom >= 0 && moving.Top <= viewport.Height)
        {
            batch.Draw(pixel, moving, MovingFloor);
            DrawOutline(batch, pixel, moving, Color.White * 0.60f);
        }

        Rectangle player = WorldToScreen(gym.PlayerBounds, centerX,
            originX, groundY);
        batch.Draw(pixel, player, gym.IsGrounded ? GroundedPlayer : AirbornePlayer);
        DrawOutline(batch, pixel, player, Color.White * 0.7f);

        // Movement-state lights retain their original meaning and position.
        // Fault mode is a SEPARATELY LABELED, persistent in-game HUD below them.
        // Read the authoritative mode every Draw; no cached/lagging selection.
        DrawFaultModeHud(batch, pixel, gym);

        // Compact state indicators: ground, jumping, paused, moving-floor.
        batch.Draw(pixel, new Rectangle(18, 18, 15, 15),
            gym.IsGrounded ? GroundedPlayer : new Color(62, 74, 92));
        batch.Draw(pixel, new Rectangle(39, 18, 15, 15),
            gym.IsJumping ? AirbornePlayer : new Color(62, 74, 92));
        batch.Draw(pixel, new Rectangle(60, 18, 15, 15),
            paused ? new Color(244, 105, 112) : new Color(62, 74, 92));
        batch.Draw(pixel, new Rectangle(81, 18, 15, 15),
            gym.IsRidingMovingPlatform ? MovingFloor : new Color(62, 74, 92));
    }

    private static void DrawFaultModeHud(
        SpriteBatch batch, Texture2D pixel, MovementGymSession gym)
    {
        Color modeColor = gym.FaultSensitivity switch
        {
            SimulationFaultSensitivity.Strict => new Color(255, 113, 113),
            SimulationFaultSensitivity.Diagnostic => new Color(255, 194, 93),
            SimulationFaultSensitivity.Resilient => new Color(74, 218, 165),
            _ => Color.White
        };
        // Positioned below the four movement-state lamps; no SpriteFont needed.
        batch.Draw(pixel, new Rectangle(18, 46, 282, 66), new Color(17, 25, 37));
        batch.Draw(pixel, new Rectangle(18, 46, 4, 66), modeColor);
        GymDebugText.Draw(batch, pixel, 28, 54,
            MovementGymFaultModeLabel.For(gym.FaultSensitivity), modeColor);
        GymDebugText.Draw(batch, pixel, 28, 76,
            "F5 STRICT  F6 DIAG  F7 SAFE", new Color(174, 188, 208), 1);
        GymDebugText.Draw(batch, pixel, 28, 91,
            "LAST RECOVERIES: " + gym.LastStepRecoveryCount,
            new Color(174, 188, 208), 1);
    }

    private static Rectangle WorldToScreen(
        Aabb box, double cameraX, float originX, float groundY)
    {
        int left = (int)Math.Round(originX + (box.Left - cameraX) * PixelsPerWorldUnit);
        int top = (int)Math.Round(groundY - box.Top * PixelsPerWorldUnit);
        int width = Math.Max(1, (int)Math.Round(box.Width * PixelsPerWorldUnit));
        int height = Math.Max(1, (int)Math.Round(box.Height * PixelsPerWorldUnit));
        return new Rectangle(left, top, width, height);
    }

    private static void DrawOutline(SpriteBatch batch, Texture2D pixel,
        Rectangle rect, Color color)
    {
        batch.Draw(pixel, new Rectangle(rect.Left, rect.Top, rect.Width, 2), color);
        batch.Draw(pixel, new Rectangle(rect.Left, rect.Bottom - 2, rect.Width, 2), color);
        batch.Draw(pixel, new Rectangle(rect.Left, rect.Top, 2, rect.Height), color);
        batch.Draw(pixel, new Rectangle(rect.Right - 2, rect.Top, 2, rect.Height), color);
    }
}
