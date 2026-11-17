using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Movement.Gyms;
using PlatformerSandbox.Gameplay.World;
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
        MovementGymSession gym, bool paused, bool showDebugHud = true)
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

            Color color = collider.Id == MovementGymSession.GroundStepBumpColliderId
                ? new Color(250, 216, 83) // visual fixture, NOT collision behavior
                : collider.Id <= 2 ? FloorColor : ElevatedColor;
            batch.Draw(pixel, rect, color);
            DrawOutline(batch, pixel, rect, Color.White * 0.22f);
        }

        if (showDebugHud)
        {
            // World-anchored step marker stays with the bump as the camera pans.
            // The small 0.10-unit rise is intentionally highlighted, not enlarged.
            if (gym.Colliders.Any(c => c.Id == MovementGymSession.GroundStepBumpColliderId))
            {
                int markerX = (int)Math.Round(originX +
                    (-3.70 - centerX) * PixelsPerWorldUnit);
                int markerY = (int)Math.Round(groundY - 2.30 * PixelsPerWorldUnit);
                GymDebugText.Draw(batch, pixel, markerX, markerY,
                    "STEP TEST - WALK LEFT - F9", new Color(250, 216, 83), 1);
            }
        }

        // Both real moving colliders render from the same simulation clock.
        foreach (var platform in gym.MovingPlatforms)
        {
            Rectangle moving = WorldToScreen(platform.Bounds, centerX,
                originX, groundY);
            if (moving.Right < 0 || moving.Left > viewport.Width ||
                moving.Bottom < 0 || moving.Top > viewport.Height) continue;
            batch.Draw(pixel, moving, MovingFloor);
            DrawOutline(batch, pixel, moving, Color.White * 0.60f);
        }

        Rectangle player = WorldToScreen(gym.PlayerBounds, centerX,
            originX, groundY);
        batch.Draw(pixel, player, gym.IsGrounded ? GroundedPlayer : AirbornePlayer);
        DrawOutline(batch, pixel, player, Color.White * 0.7f);

        if (showDebugHud)
        {
            // Movement-state lights retain their original meaning and position.
            // Fault mode is a SEPARATELY LABELED, persistent in-game HUD below them.
            // Read the authoritative mode every Draw; no cached/lagging selection.
            DrawFaultModeHud(batch, pixel, gym);
            GymDebugText.Draw(batch, pixel, 18, 123,
                gym.PlatformMode == MovingPlatformGymMode.Both
                    ? "PLATFORMS: HORIZONTAL + ELEVATOR"
                    : gym.PlatformMode == MovingPlatformGymMode.Elevator
                        ? "PLATFORM: ELEVATOR" : "PLATFORM: HORIZONTAL",
                MovingFloor, 1);
            GymDebugText.Draw(batch, pixel, 18, 140,
                "CRUSH RESPAWNS: " + gym.CrushRespawns,
                new Color(255, 144, 144), 1);
            GymDebugText.Draw(batch, pixel, 18, 157,
                "F9 COLLISION ASSIST: " +
                (gym.AssistancePolicy.AnyEnabled ? "ON" : "OFF") +
                "  ASSISTS: " + gym.TotalAssists,
                new Color(174, 218, 192), 1);

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
    }

    /// <summary>
    /// Separate F4 fixture: both authoritative players render on the same
    /// world-owned platform clock. The normal crush gym remains untouched.
    /// </summary>
    public static void DrawSharedWorldClock(
        SpriteBatch batch, Texture2D pixel, Viewport viewport,
        WorldSession world, bool paused, int p1Respawns, int p2Respawns,
        bool showDebugHud = true)
    {
        WorldPlayerSnapshot p1 = world.GetPlayer(SharedWorldClockGym.FirstPlayerId);
        WorldPlayerSnapshot p2 = world.GetPlayer(SharedWorldClockGym.SecondPlayerId);
        double cameraX = (p1.Bounds.Center.X + p2.Bounds.Center.X) * 0.5;
        float originX = viewport.Width * 0.5f;
        float groundY = viewport.Height * 0.78f;

        int visibleHalfWidth = (int)Math.Ceiling(viewport.Width /
            (2.0 * PixelsPerWorldUnit)) + 1;
        for (int x = (int)Math.Floor(cameraX) - visibleHalfWidth;
             x <= (int)Math.Ceiling(cameraX) + visibleHalfWidth; x++)
        {
            int sx = (int)Math.Round(originX + (x - cameraX) * PixelsPerWorldUnit);
            batch.Draw(pixel, new Rectangle(sx, 0, 1, viewport.Height), GridColor);
        }
        for (int y = -7; y <= 10; y++)
        {
            int sy = (int)Math.Round(groundY - y * PixelsPerWorldUnit);
            if (sy >= 0 && sy < viewport.Height)
                batch.Draw(pixel, new Rectangle(0, sy, viewport.Width, 1), GridColor);
        }
        foreach (var staticBody in world.StaticColliders)
        {
            Rectangle screen = WorldToScreen(staticBody.Bounds, cameraX, originX, groundY);
            batch.Draw(pixel, screen, FloorColor);
            DrawOutline(batch, pixel, screen, Color.White * 0.22f);
        }
        foreach (var platform in world.MovingPlatforms)
        {
            Rectangle screen = WorldToScreen(platform.Bounds, cameraX, originX, groundY);
            batch.Draw(pixel, screen, MovingFloor);
            DrawOutline(batch, pixel, screen, Color.White * 0.6f);
        }
        foreach (WorldPlayerSnapshot p in world.Snapshot().Players)
        {
            Rectangle screen = WorldToScreen(p.Bounds, cameraX, originX, groundY);
            Color tint = p.PlayerId == SharedWorldClockGym.FirstPlayerId
                ? new Color(68, 214, 177) : new Color(161, 145, 246);
            batch.Draw(pixel, screen, tint);
            DrawOutline(batch, pixel, screen, Color.White * 0.8f);
            GymDebugText.Draw(batch, pixel, screen.Left + 5, screen.Top + 5,
                p.PlayerId == SharedWorldClockGym.FirstPlayerId ? "1" : "2",
                new Color(18, 23, 35), 1);
        }

        if (showDebugHud)
        {
            batch.Draw(pixel, new Rectangle(18, 42, 494, 178),
                new Color(17, 25, 37) * 0.95f);
            batch.Draw(pixel, new Rectangle(18, 42, 4, 178), new Color(116, 193, 242));
            GymDebugText.Draw(batch, pixel, 30, 50,
                "F4 SHARED WORLD CLOCK TEST", new Color(116, 193, 242), 2);
            GymDebugText.Draw(batch, pixel, 30, 74,
                "WORLD TICK: " + world.WorldTick +
                "  TIME: " + world.WorldTimeSeconds.ToString("F3"),
                new Color(200, 220, 236), 1);
            GymDebugText.Draw(batch, pixel, 30, 93,
                "P1 X: " + p1.Bounds.Left.ToString("F2") +
                "  SUPPORT: " + (p1.MovingSupportId?.ToString() ?? "NONE") +
                "  RESPAWNS: " + p1Respawns,
                new Color(68, 214, 177), 1);
            GymDebugText.Draw(batch, pixel, 30, 110,
                "P2 X: " + p2.Bounds.Left.ToString("F2") +
                "  SUPPORT: " + (p2.MovingSupportId?.ToString() ?? "NONE") +
                "  RESPAWNS: " + p2Respawns,
                new Color(161, 145, 246), 1);
            GymDebugText.Draw(batch, pixel, 30, 130,
                "P1: A/D + SPACE OR F8 GAMEPAD",
                new Color(200, 220, 236), 1);
            GymDebugText.Draw(batch, pixel, 30, 148,
                "P2: J/L MOVE   I JUMP",
                new Color(200, 220, 236), 1);
            GymDebugText.Draw(batch, pixel, 30, 166,
                "R P1 RESPAWN  U P2 RESPAWN  T WORLD RESET",
                new Color(200, 220, 236), 1);
            GymDebugText.Draw(batch, pixel, 30, 184,
                "F4 EXIT  P PAUSE  " + (paused ? "PAUSED" : "RUNNING"),
                paused ? new Color(255, 156, 156) : new Color(112, 218, 173), 1);
        }
    }

    /// <summary>
    /// Data-defined world/slot gym. Rendered entirely from immutable world
    /// state; the draw loop cannot advance or alter the simulation.
    /// </summary>
    public static void DrawAuthoredWorld(
        SpriteBatch batch, Texture2D pixel, Viewport viewport,
        WorldSession world, bool paused, int p1Respawns, int p2Respawns,
        bool motionLab = false, bool showDebugHud = true)
    {
        WorldStepResult snapshot = world.Snapshot();
        WorldPlayerSnapshot p1 = world.GetPlayer(AuthoredWorldGym.FirstPlayerId);
        WorldPlayerSnapshot p2 = world.GetPlayer(AuthoredWorldGym.SecondPlayerId);
        double cameraX = (p1.Bounds.Center.X + p2.Bounds.Center.X) * 0.5;
        float originX = viewport.Width * 0.5f;
        float groundY = viewport.Height * 0.78f;

        int half = (int)Math.Ceiling(viewport.Width / (2.0 * PixelsPerWorldUnit)) + 1;
        for (int x = (int)Math.Floor(cameraX) - half;
             x <= (int)Math.Ceiling(cameraX) + half; x++)
        {
            int sx = (int)Math.Round(originX + (x - cameraX) * PixelsPerWorldUnit);
            batch.Draw(pixel, new Rectangle(sx, 0, 1, viewport.Height), GridColor);
        }
        for (int y = -5; y <= 9; y++)
        {
            int sy = (int)Math.Round(groundY - y * PixelsPerWorldUnit);
            if (sy >= 0 && sy < viewport.Height)
                batch.Draw(pixel, new Rectangle(0, sy, viewport.Width, 1), GridColor);
        }
        foreach (var collider in world.StaticColliders)
        {
            Rectangle screen = WorldToScreen(collider.Bounds, cameraX, originX, groundY);
            batch.Draw(pixel, screen, FloorColor);
            DrawOutline(batch, pixel, screen, Color.White * 0.22f);
        }
        foreach (var platform in world.MovingPlatforms)
        {
            Rectangle screen = WorldToScreen(platform.Bounds, cameraX, originX, groundY);
            batch.Draw(pixel, screen, MovingFloor);
            DrawOutline(batch, pixel, screen, Color.White * 0.6f);
        }
        foreach (WorldTriggerDefinition trigger in world.Definition?.Triggers ??
                     Array.Empty<WorldTriggerDefinition>())
        {
            Rectangle zone = WorldToScreen(trigger.Bounds, cameraX, originX, groundY);
            batch.Draw(pixel, zone, new Color(245, 214, 111) * 0.16f);
            DrawOutline(batch, pixel, zone, new Color(245, 214, 111) * 0.9f);
            GymDebugText.Draw(batch, pixel, zone.Left + 4, zone.Top + 4,
                "ZONE " + trigger.Id, new Color(248, 224, 144), 1);
        }
        foreach (WorldPlayerSnapshot p in snapshot.Players)
        {
            Rectangle screen = WorldToScreen(p.Bounds, cameraX, originX, groundY);
            Color tint = p.PlayerId switch
            {
                AuthoredWorldGym.FirstPlayerId => new Color(68, 214, 177),
                AuthoredWorldGym.SecondPlayerId => new Color(161, 145, 246),
                _ => new Color(245, 214, 111)
            };
            batch.Draw(pixel, screen, tint);
            DrawOutline(batch, pixel, screen, Color.White * 0.8f);
            GymDebugText.Draw(batch, pixel, screen.Left + 5, screen.Top + 5,
                p.PlayerId.ToString(), new Color(18, 23, 35), 1);
        }
        // Separate from the normal F4 dashboard: shows authored ID, player
        // slot ownership, spawn selection, and all actions under test.
        if (showDebugHud)
        {
            batch.Draw(pixel, new Rectangle(18, 42, 570, 219),
                new Color(17, 25, 37) * 0.95f);
            batch.Draw(pixel, new Rectangle(18, 42, 4, 219), new Color(116, 193, 242));
            GymDebugText.Draw(batch, pixel, 30, 50,
                motionLab ? "F4 MOTION LAB (F1)" : "F4 WORLD DEFINITION + PLAYER SLOTS",
                new Color(116, 193, 242), 2);
            GymDebugText.Draw(batch, pixel, 30, 75,
                "LEVEL: " + world.Definition?.LevelId +
                "  TICK: " + world.WorldTick, new Color(200, 220, 236), 1);
            GymDebugText.Draw(batch, pixel, 30, 92,
                "PLAYERS: " + world.PlayerCount +
                "  TIME: " + world.WorldTimeSeconds.ToString("F2"),
                new Color(200, 220, 236), 1);
            foreach (WorldPlayerSnapshot p in snapshot.Players)
            {
                WorldPlayerSlotInfo slot = world.GetPlayerSlot(p.PlayerId);
                Color tint = p.PlayerId switch
                {
                    1 => new Color(68, 214, 177),
                    2 => new Color(161, 145, 246),
                    _ => new Color(245, 214, 111)
                };
                int y = 111 + (p.PlayerId - 1) * 17;
                GymDebugText.Draw(batch, pixel, 30, y,
                    "P" + p.PlayerId + " C" + slot.CharacterId +
                    " SRC:" + (slot.InputSourceId?.ToString() ?? "NONE") +
                    " SPAWN:" + slot.SpawnId +
                    " SUP:" + (p.MovingSupportId?.ToString() ?? "NONE"), tint, 1);
            }
            GymDebugText.Draw(batch, pixel, 30, 168,
                motionLab ? "F1 EXIT LAB  F2 AUTHORED  F4 EXIT"
                    : "F2 TOGGLE OLD/NEW    F4 EXIT    P PAUSE",
                new Color(200, 220, 236), 1);
            GymDebugText.Draw(batch, pixel, 30, 185,
                "R P1 RESPAWN  U P2 RESPAWN  T WORLD RESET",
                new Color(200, 220, 236), 1);
            GymDebugText.Draw(batch, pixel, 30, 202,
                motionLab ? "Q WIND LEFT/RIGHT/OFF   E KNOCKBACK"
                    : "N JOIN P3   M REMOVE P3   V P1 SPAWN",
                new Color(200, 220, 236), 1);
            GymDebugText.Draw(batch, pixel, 30, 219,
                "P1 A/D SPACE/F8 PAD    P2 J/L I    " + (paused ? "PAUSED" : "RUNNING"),
                paused ? new Color(255, 156, 156) : new Color(112, 218, 173), 1);
            GymDebugText.Draw(batch, pixel, 30, 238,
                "RESPAWNS P1:" + p1Respawns + " P2:" + p2Respawns,
                new Color(200, 220, 236), 1);
        }
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