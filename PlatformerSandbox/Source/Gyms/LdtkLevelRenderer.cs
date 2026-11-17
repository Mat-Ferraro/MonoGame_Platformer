using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Foundation.Geometry;
using PlatformerSandbox.Foundation.Presentation;
using PlatformerSandbox.Foundation.Presentation.Particles;
using PlatformerSandbox.Presentation;
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
        LdtkConnectedLevel? connected = null,
        Camera2DView? camera = null,
        LdtkGrayboxAnimator? playerAnimations = null,
        LdtkCharacterSpriteAssets? characterSprites = null,
        Action<ParticleVisualLayer>? drawParticleLayer = null)
    {
        double scale = LdtkLevelImporter.PixelsPerWorldUnit;
        WorldPlayerSnapshot player = world.GetPlayer(1);
        // The optional projection lets the renderer remain usable by existing
        // fixtures while the game host owns the persistent follow controller.
        Camera2DView view = camera ?? Camera2DView.FromFocus(
            player.Bounds.Center, level.WidthPx / scale, level.HeightPx / scale,
            viewport.Width, viewport.Height, scale);
        double cameraLeftPx = view.CameraLeftPx;
        double cameraTopPx = view.CameraTopPx;

        // The caller owns the outer SpriteBatch. This hook respects existing
        // BG/terrain/player/FG layering without changing visual content logic.
        void DrawParticleLayer(ParticleVisualLayer layer)
        {
            if (drawParticleLayer is null) return;
            batch.End();
            drawParticleLayer(layer);
            batch.Begin(samplerState: SamplerState.PointClamp);
        }

        Rectangle WorldRect(Aabb box)
        {
            CameraScreenRect rect = view.WorldRect(box);
            return new Rectangle(rect.X, rect.Y, rect.Width, rect.Height);
        }

        bool hasVisuals = visuals is not null && (visuals.Definition.TileCount > 0 ||
            visuals.Scenery.SpriteCount > 0 || (visuals.Terrain?.ArtworkCount ?? 0) > 0);
        bool showSolidBacking = collisionBackdrop ?? visuals?.Terrain?.DrawCollisionBackdrop ?? true;
        batch.Draw(pixel, new Rectangle(0, 0, viewport.Width, viewport.Height),
            hasVisuals ? new Color(15, 22, 35) : new Color(30, 43, 57));
        visuals?.Draw(batch, LdtkVisualPlane.Background,
            cameraLeftPx, cameraTopPx, viewport);
        DrawParticleLayer(ParticleVisualLayer.Background);

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

        DrawParticleLayer(ParticleVisualLayer.World);

        foreach (WorldPlayerSnapshot entity in world.Snapshot().Players)
        {
            Rectangle rect = WorldRect(entity.Bounds);
            // Existing graybox stays collider-aligned. Animation is cosmetic:
            // a small face/foot cue shows frame and facing without changing
            // player bounds or introducing authored art requirements.
            LdtkGrayboxPose pose = playerAnimations?.GetPose(entity.PlayerId)
                ?? new LdtkGrayboxPose("Idle", 0, 1);
            Color body = pose.Clip switch
            {
                "Dash" => new Color(243, 204, 104),
                "Rising" => new Color(117, 198, 246),
                "Falling" => new Color(127, 176, 230),
                _ => new Color(68, 214, 177)
            };
            if (characterSprites is not null && playerAnimations is not null)
            {
                // Draw the actual atlas frame, flipped by facing direction.
                // The authoritative collider remains unchanged; a F9 outline
                // provides a useful sprite-vs-collision visual check.
                characterSprites.Draw(batch, rect, pose,
                    entity.PlayerId == 1 ? Color.White : new Color(228, 204, 255));
                if (showCollisionDebug)
                {
                    batch.Draw(pixel, new Rectangle(rect.Left, rect.Top,
                        Math.Max(1, rect.Width), 1), Color.Orange);
                    batch.Draw(pixel, new Rectangle(rect.Left, rect.Bottom - 1,
                        Math.Max(1, rect.Width), 1), Color.Orange);
                    batch.Draw(pixel, new Rectangle(rect.Left, rect.Top,
                        1, Math.Max(1, rect.Height)), Color.Orange);
                    batch.Draw(pixel, new Rectangle(rect.Right - 1, rect.Top,
                        1, Math.Max(1, rect.Height)), Color.Orange);
                }
            }
            else
            {
                // Original C01 graybox fallback; remains available even when
                // optional PNG/manifest files are missing or malformed.
                batch.Draw(pixel, rect, body);
                batch.Draw(pixel, new Rectangle(rect.Left, rect.Top,
                    Math.Max(1, rect.Width), 2), Color.White);
                if (playerAnimations is not null)
                {
                    int eyeSize = Math.Max(2, (int)Math.Round(3 * scale / 32.0));
                    int eyeX = pose.Facing >= 0 ? rect.Right - eyeSize - 4 : rect.Left + 4;
                    int eyeY = rect.Top + Math.Max(4, rect.Height / 4);
                    if (pose.Clip != "Idle" || pose.Frame != 1)
                        batch.Draw(pixel, new Rectangle(eyeX, eyeY, eyeSize, eyeSize),
                            new Color(20, 43, 56));
                    if (pose.Clip is "Run" or "Dash")
                    {
                        int footX = rect.Left + ((pose.Frame % 2 == 0) ? 4 : rect.Width - 7);
                        batch.Draw(pixel, new Rectangle(footX, rect.Bottom - 4, 3, 3),
                            new Color(16, 61, 69));
                    }
                }
            }
            GymDebugText.Draw(batch, pixel, rect.Left + 4, rect.Top + 5,
                entity.PlayerId.ToString(), new Color(18, 23, 35));
        }
        DrawParticleLayer(ParticleVisualLayer.Foreground);
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

}
