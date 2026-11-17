using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.World;

namespace PlatformerSandbox.Gyms;

/// <summary>
/// Shared camera for LDtk-authored geometry and independent visual layers.
/// BG/Terrain draw before players; FG draws after. Collision debug is a
/// presentation option and never changes authoritative WorldSession state.
/// </summary>
internal static class LdtkLevelRenderer
{
    public static void Draw(SpriteBatch batch, Texture2D pixel, Viewport viewport,
        WorldSession world, LdtkImportedLevel level, bool paused,
        LdtkVisualSceneAssets? visuals = null, bool showCollisionDebug = false,
        bool showDebugHud = true, bool? collisionBackdrop = null,
        LdtkConnectedLevel? connected = null)
    {
        double scale = LdtkLevelImporter.PixelsPerWorldUnit;
        double width = level.WidthPx / scale;
        double height = level.HeightPx / scale;
        WorldPlayerSnapshot player = world.GetPlayer(1);
        double visibleWidth = viewport.Width / scale;
        double visibleHeight = viewport.Height / scale;
        double left = ViewOrigin(player.Bounds.Center.X, width, visibleWidth);
        double bottom = ViewOrigin(player.Bounds.Center.Y, height, visibleHeight);
        double cameraLeftPx = left * scale;
        double cameraTopPx = level.HeightPx - (bottom + visibleHeight) * scale;

        Rectangle WorldRect(Aabb box)
        {
            int x = (int)Math.Round((box.Left - left) * scale);
            int y = (int)Math.Round(viewport.Height - (box.Top - bottom) * scale);
            int w = Math.Max(1, (int)Math.Round(box.Width * scale));
            int h = Math.Max(1, (int)Math.Round(box.Height * scale));
            return new Rectangle(x, y, w, h);
        }

        bool hasVisuals = visuals is not null && (visuals.Definition.TileCount > 0 ||
            visuals.Scenery.SpriteCount > 0 || (visuals.Terrain?.ArtworkCount ?? 0) > 0);
        bool showSolidBacking = collisionBackdrop ?? visuals?.Terrain?.DrawCollisionBackdrop ?? true;
        batch.Draw(pixel, new Rectangle(0, 0, viewport.Width, viewport.Height),
            hasVisuals ? new Color(15, 22, 35) : new Color(30, 43, 57));
        visuals?.Draw(batch, LdtkVisualPlane.Background,
            cameraLeftPx, cameraTopPx, viewport);

        // Graybox fallback makes collision terrain recognizable until actual
        // independent terrain artwork is authored. It is NOT a physics query
        // fed back into the simulator; all effects are presentation-only.
        if (showSolidBacking)
        {
            foreach (var collider in world.StaticColliders)
            {
                Rectangle rect = WorldRect(collider.Bounds);
                batch.Draw(pixel, rect, hasVisuals ? new Color(35, 56, 63) :
                    new Color(97, 119, 141));
                if (!hasVisuals)
                    batch.Draw(pixel, new Rectangle(rect.Left, rect.Top,
                        Math.Max(1, rect.Width), 2), new Color(170, 191, 205));
            }
        }
        visuals?.Draw(batch, LdtkVisualPlane.Terrain,
            cameraLeftPx, cameraTopPx, viewport);

        // Moving platforms are authoritative solids. Show their live bounds
        // in this development scene even if the gray collision backing is off.
        foreach (var platform in world.MovingPlatforms)
        {
            Rectangle rect = WorldRect(platform.Bounds);
            batch.Draw(pixel, rect, new Color(93, 163, 184) * 0.85f);
            batch.Draw(pixel, new Rectangle(rect.Left, rect.Top,
                Math.Max(1, rect.Width), 2), new Color(189, 231, 235));
        }

        foreach (WorldPlayerSnapshot entity in world.Snapshot().Players)
        {
            Rectangle rect = WorldRect(entity.Bounds);
            batch.Draw(pixel, rect, new Color(68, 214, 177));
            batch.Draw(pixel, new Rectangle(rect.Left, rect.Top,
                Math.Max(1, rect.Width), 2), Color.White);
            GymDebugText.Draw(batch, pixel, rect.Left + 4, rect.Top + 5,
                entity.PlayerId.ToString(), new Color(18, 23, 35));
        }
        visuals?.Draw(batch, LdtkVisualPlane.Foreground,
            cameraLeftPx, cameraTopPx, viewport);

        // F9 explicitly reveals invisible collision while testing art alignment.
        if (showCollisionDebug)
        {
            foreach (var collider in world.StaticColliders)
            {
                Rectangle rect = WorldRect(collider.Bounds);
                batch.Draw(pixel, new Rectangle(rect.Left, rect.Top, rect.Width, 2),
                    new Color(255, 210, 92));
                batch.Draw(pixel, new Rectangle(rect.Left, rect.Bottom - 2, rect.Width, 2),
                    new Color(255, 210, 92));
                batch.Draw(pixel, new Rectangle(rect.Left, rect.Top, 2, rect.Height),
                    new Color(255, 210, 92));
                batch.Draw(pixel, new Rectangle(rect.Right - 2, rect.Top, 2, rect.Height),
                    new Color(255, 210, 92));
            }
        }

        if (showCollisionDebug && connected is not null)
        {
            // Colored interaction-volume outlines make authored routing,
            // checkpoints and wind easy to validate independently of artwork.
            foreach (var trigger in connected.Imported.World.Triggers)
            {
                Rectangle r = WorldRect(trigger.Bounds);
                Color color = connected.Exits.ContainsKey(trigger.Id)
                    ? new Color(255, 185, 82)
                    : connected.Checkpoints.ContainsKey(trigger.Id)
                        ? new Color(93, 230, 165)
                        : new Color(138, 172, 255);
                batch.Draw(pixel, new Rectangle(r.Left, r.Top, Math.Max(1,r.Width), 2), color);
                batch.Draw(pixel, new Rectangle(r.Left, r.Bottom-2, Math.Max(1,r.Width), 2), color);
                batch.Draw(pixel, new Rectangle(r.Left, r.Top, 2, Math.Max(1,r.Height)), color);
                batch.Draw(pixel, new Rectangle(r.Right-2, r.Top, 2, Math.Max(1,r.Height)), color);
            }
        }

        if (showDebugHud)
        {
            batch.Draw(pixel, new Rectangle(14, 40, 610, 137),
                new Color(16, 25, 37) * 0.94f);
            batch.Draw(pixel, new Rectangle(14, 40, 4, 137),
                new Color(245, 203, 113));
            GymDebugText.Draw(batch, pixel, 27, 49,
                hasVisuals ? "LDTK VISUAL / PARALLAX TEST" :
                    "LDTK AUTHORING TEST  (F7 FROM TITLE)", new Color(245, 203, 113), 1);
            GymDebugText.Draw(batch, pixel, 27, 67,
                "LEVEL: " + level.World.LevelId + "  TICK: " + world.WorldTick,
                new Color(209, 220, 235), 1);
            GymDebugText.Draw(batch, pixel, 27, 85,
                "SOLID: " + level.SolidCellCount + "  COLLIDERS: " + level.ColliderCount +
                "  TILES: " + (visuals?.Definition.TileCount ?? 0) +
                "  SCENERY: " + (visuals?.Scenery.SpriteCount ?? 0) +
                "  PNG ART: " + (visuals?.Terrain?.ArtworkCount ?? 0),
                new Color(209, 220, 235), 1);
            GymDebugText.Draw(batch, pixel, 27, 103,
                "BG/TERRAIN/FG LAYERS: " + (visuals?.Definition.Layers.Count ?? 0) +
                "  BACKING F10: " + (showSolidBacking ? "ON" : "OFF") +
                "  OUTLINES F9: " + (showCollisionDebug ? "ON" : "OFF"),
                new Color(209, 220, 235), 1);
            GymDebugText.Draw(batch, pixel, 27, 121,
                "A/D SPACE MOVE  R RESPAWN  T RELOAD  P PAUSE",
                new Color(209, 220, 235), 1);
            GymDebugText.Draw(batch, pixel, 27, 139,
                paused ? "PAUSED" : "EDIT LDTK > SAVE > T TO RELOAD LATEST FILE",
                new Color(108, 224, 178), 1);
            GymDebugText.Draw(batch, pixel, 27, 157,
                "F7 ORIGINAL F8 PARALLAX F9 SCENERY F10 TERRAIN AT TITLE",
                new Color(161, 184, 201), 1);
        }
    }

    private static double ViewOrigin(double focus, double levelLength,
        double viewportLength)
    {
        if (levelLength <= viewportLength)
            return (levelLength - viewportLength) * 0.5;
        return Math.Clamp(focus - viewportLength * 0.5,
            0, levelLength - viewportLength);
    }
}
