using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Foundation.Presentation.Lighting;

namespace PlatformerSandbox.Gyms;

/// <summary>
/// Scene-owned textures for authored tile layers, tile-based scenery and
/// standalone transparent terrain PNGs. All images are prepared before the
/// scene commits; failure preserves the previous world and releases partial
/// GPU resources. Gameplay never owns Texture2D.
/// </summary>
internal sealed class LdtkVisualSceneAssets : IDisposable
{
    private readonly Dictionary<string, Texture2D> _images =
        new(StringComparer.OrdinalIgnoreCase);

    public LdtkVisualDefinition Definition { get; }
    public LdtkSceneryDefinition Scenery { get; }
    public LdtkTerrainArtworkDefinition? Terrain { get; }

    public LdtkVisualSceneAssets(GraphicsDevice graphics, string ldtkFile,
        LdtkVisualDefinition definition, LdtkSceneryDefinition scenery,
        LdtkTerrainArtworkDefinition? terrain = null)
    {
        ArgumentNullException.ThrowIfNull(graphics);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(scenery);
        Definition = definition;
        Scenery = scenery;
        Terrain = terrain;
        string root = Path.GetFullPath(Path.GetDirectoryName(ldtkFile) ?? ".");
        string rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        void LoadImage(string relative, int expectedWidth = 0, int expectedHeight = 0)
        {
            if (_images.TryGetValue(relative, out Texture2D? existing))
            {
                if (expectedWidth > 0 &&
                    (existing.Width != expectedWidth || existing.Height != expectedHeight))
                    throw new InvalidOperationException("Conflicting PNG dimensions for " + relative);
                return;
            }
            LdtkVisualImporter.ValidateRelativePath(relative);
            string path = Path.GetFullPath(Path.Combine(root,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("PNG path escapes LDtk project directory: " + relative);
            using FileStream stream = File.OpenRead(path);
            Texture2D texture = Texture2D.FromStream(graphics, stream);
            if (texture.Width <= 0 || texture.Height <= 0 ||
                texture.Width > 8192 || texture.Height > 8192 ||
                (expectedWidth > 0 &&
                 (texture.Width != expectedWidth || texture.Height != expectedHeight)))
            {
                texture.Dispose();
                throw new InvalidOperationException("Invalid authored PNG dimensions: " + relative);
            }
            _images.Add(relative, texture);
        }

        try
        {
            foreach (LdtkVisualLayer layer in Definition.Layers)
                LoadImage(layer.TilesetPath, layer.TilesetWidthPx, layer.TilesetHeightPx);
            foreach (LdtkScenerySprite sprite in Scenery.Sprites)
                LoadImage(sprite.TilesetPath, sprite.AtlasWidthPx, sprite.AtlasHeightPx);
            if (Terrain is not null)
                foreach (LdtkTerrainArtwork art in Terrain.Artworks)
                    LoadImage(art.ImagePath);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>
    /// Each plane retains tile layer order. Free scenery/artwork is then
    /// combined in stable Depth/IID order, without per-frame sorting or
    /// allocating arrays. Terrain PNGs are independent of collision cells.
    /// </summary>
    public void Draw(SpriteBatch batch, LdtkVisualPlane plane,
        double cameraLeftPx, double cameraTopPx, Viewport view,
        LdtkLightingScene? lighting = null, bool? onlyPostLighting = null,
        Func<string, Color>? lightTint = null, Texture2D? contactShadow = null,
        string? renderLayer = null, double parallaxMultiplier = 1)
    {
        // Ordinary drawing (null) preserves the legacy renderer. During lighting,
        // split light receiving surfaces from unlit and casting foreground props.
        // Tile layers are backdrop receivers; free sprite/PNG art can opt out.
        foreach (LdtkVisualLayer layer in Definition.Layers)
        {
            if (layer.Plane != plane) continue;
            if (renderLayer is not null && lighting?.Layers.ForTile(layer.Name).Id != renderLayer)
                continue;
            bool layerUnlit = lighting is not null &&
                lighting.Layers.ForTile(layer.Name).Mode == RenderLayerLightingMode.Unlit;
            if (onlyPostLighting is not null && onlyPostLighting.Value != layerUnlit)
                continue;
            Texture2D texture = _images[layer.TilesetPath];
            foreach (LdtkVisualTile tile in layer.Tiles)
            {
                (double x, double y, double w, double h) =
                    LdtkVisualTransform.Project(layer, tile,
                        cameraLeftPx * parallaxMultiplier, cameraTopPx * parallaxMultiplier,
                        view.Width, view.Height);
                if (w < .5 || h < .5) continue;
                var dest = new Rectangle((int)Math.Round(x), (int)Math.Round(y),
                    Math.Max(1, (int)Math.Round(w)), Math.Max(1, (int)Math.Round(h)));
                if (dest.Right < 0 || dest.Bottom < 0 ||
                    dest.Left > view.Width || dest.Top > view.Height) continue;
                var src = new Rectangle(tile.SourceX, tile.SourceY, tile.WidthPx, tile.HeightPx);
                SpriteEffects flip = SpriteEffects.None;
                if ((tile.FlipBits & 1) != 0) flip |= SpriteEffects.FlipHorizontally;
                if ((tile.FlipBits & 2) != 0) flip |= SpriteEffects.FlipVertically;
                batch.Draw(texture, dest, src,
                    Color.White * (layer.Opacity * tile.Opacity),
                    0f, Vector2.Zero, flip, 0f);
            }
        }

        IReadOnlyList<LdtkScenerySprite> scenery = Scenery.Sprites;
        IReadOnlyList<LdtkTerrainArtwork> artwork = Terrain?.Artworks ??
            Array.Empty<LdtkTerrainArtwork>();
        int i = 0, j = 0;
        while (i < scenery.Count && scenery[i].Plane < plane) i++;
        while (j < artwork.Count && artwork[j].Plane < plane) j++;
        while ((i < scenery.Count && scenery[i].Plane == plane) ||
               (j < artwork.Count && artwork[j].Plane == plane))
        {
            bool hasScenery = i < scenery.Count && scenery[i].Plane == plane;
            bool hasArt = j < artwork.Count && artwork[j].Plane == plane;
            bool drawScenery = hasScenery && (!hasArt ||
                scenery[i].Depth < artwork[j].Depth ||
                (scenery[i].Depth == artwork[j].Depth &&
                 string.CompareOrdinal(scenery[i].InstanceId,
                    artwork[j].InstanceId) <= 0));
            if (drawScenery)
            {
                LdtkScenerySprite sprite = scenery[i++];
                LightingSurfaceOptions settings = lighting?.GetSurface(sprite.InstanceId) ?? new();
                if (renderLayer is not null && settings.LayerId != renderLayer) continue;
                bool layerUnlit = lighting is not null &&
                    lighting.Layers.Get(settings.LayerId).Mode == RenderLayerLightingMode.Unlit;
                bool post = settings.NeedsIndependentPass || layerUnlit;
                if (onlyPostLighting is null || onlyPostLighting.Value == post)
                    DrawScenery(batch, sprite, cameraLeftPx * parallaxMultiplier,
                        cameraTopPx * parallaxMultiplier, view,
                        lightTint?.Invoke(sprite.InstanceId) ?? Color.White,
                        post && settings.ContactShadow ? contactShadow : null);
            }
            else
            {
                LdtkTerrainArtwork art = artwork[j++];
                LightingSurfaceOptions settings = lighting?.GetSurface(art.InstanceId) ?? new();
                if (renderLayer is not null && settings.LayerId != renderLayer) continue;
                bool layerUnlit = lighting is not null &&
                    lighting.Layers.Get(settings.LayerId).Mode == RenderLayerLightingMode.Unlit;
                bool post = settings.NeedsIndependentPass || layerUnlit;
                if (onlyPostLighting is null || onlyPostLighting.Value == post)
                    DrawArtwork(batch, art, cameraLeftPx * parallaxMultiplier,
                        cameraTopPx * parallaxMultiplier, view,
                        lightTint?.Invoke(art.InstanceId) ?? Color.White,
                        post && settings.ContactShadow ? contactShadow : null);
            }
        }
    }

    public bool HasLayerContent(LdtkLightingScene scene, string layerId)
    {
        foreach (LdtkVisualLayer tiles in Definition.Layers)
            if (tiles.Tiles.Count > 0 && scene.Layers.ForTile(tiles.Name).Id == layerId)
                return true;
        foreach (LdtkScenerySprite sprite in Scenery.Sprites)
            if (scene.GetSurface(sprite.InstanceId).LayerId == layerId) return true;
        if (Terrain is not null)
            foreach (LdtkTerrainArtwork art in Terrain.Artworks)
                if (scene.GetSurface(art.InstanceId).LayerId == layerId) return true;
        return false;
    }

    private void DrawScenery(SpriteBatch batch, LdtkScenerySprite sprite,
        double cameraLeftPx, double cameraTopPx, Viewport view, Color tint,
        Texture2D? contactShadow)
    {
        var (x, y, w, h) = LdtkSceneryTransform.Project(sprite, cameraLeftPx, cameraTopPx);
        double radius = Math.Sqrt(w * w + h * h);
        if (x + radius < 0 || y + radius < 0 ||
            x - radius > view.Width || y - radius > view.Height) return;
        if (contactShadow is not null)
        {
            int cx = (int)(x + (0.5 - sprite.PivotX) * w);
            int cy = (int)(y + (1 - sprite.PivotY) * h);
            batch.Draw(contactShadow, new Rectangle(cx - (int)(w * .43), cy - 3,
                Math.Max(1, (int)(w * .86)), Math.Max(2, (int)(h * .12))), Color.White);
        }
        var src = new Rectangle(sprite.SourceX, sprite.SourceY,
            sprite.SourceWidth, sprite.SourceHeight);
        SpriteEffects effects = SpriteEffects.None;
        if (sprite.FlipX) effects |= SpriteEffects.FlipHorizontally;
        if (sprite.FlipY) effects |= SpriteEffects.FlipVertically;
        batch.Draw(_images[sprite.TilesetPath], new Vector2((float)x, (float)y),
            src, Tint(tint, sprite.Opacity),
            MathHelper.ToRadians(sprite.RotationDegrees),
            new Vector2(sprite.SourceWidth * sprite.PivotX,
                sprite.SourceHeight * sprite.PivotY),
            new Vector2((float)(w / sprite.SourceWidth),
                (float)(h / sprite.SourceHeight)), effects, 0f);
    }

    private void DrawArtwork(SpriteBatch batch, LdtkTerrainArtwork art,
        double cameraLeftPx, double cameraTopPx, Viewport view, Color lightTint,
        Texture2D? contactShadow)
    {
        var (x, y, w, h) = LdtkTerrainArtworkTransform.Project(art,
            cameraLeftPx, cameraTopPx);
        double radius = Math.Sqrt(w * w + h * h);
        if (x + radius < 0 || y + radius < 0 ||
            x - radius > view.Width || y - radius > view.Height) return;
        if (contactShadow is not null)
        {
            int cx = (int)(x + (0.5 - art.PivotX) * w);
            int cy = (int)(y + (1 - art.PivotY) * h);
            batch.Draw(contactShadow, new Rectangle(cx - (int)(w * .43), cy - 3,
                Math.Max(1, (int)(w * .86)), Math.Max(2, (int)(h * .12))), Color.White);
        }
        Texture2D image = _images[art.ImagePath];
        SpriteEffects effects = SpriteEffects.None;
        if (art.FlipX) effects |= SpriteEffects.FlipHorizontally;
        if (art.FlipY) effects |= SpriteEffects.FlipVertically;
        Color tint = new(
            Convert.ToByte(art.TintHex.Substring(1, 2), 16),
            Convert.ToByte(art.TintHex.Substring(3, 2), 16),
            Convert.ToByte(art.TintHex.Substring(5, 2), 16));
        batch.Draw(image, new Vector2((float)x, (float)y),
            new Rectangle(0, 0, image.Width, image.Height), Tint(new Color(
                (byte)(tint.R * (lightTint.R / 255.0)),
                (byte)(tint.G * (lightTint.G / 255.0)),
                (byte)(tint.B * (lightTint.B / 255.0))), art.Opacity),
            MathHelper.ToRadians(art.RotationDegrees),
            new Vector2(image.Width * art.PivotX, image.Height * art.PivotY),
            new Vector2((float)(w / image.Width), (float)(h / image.Height)),
            effects, 0f);
    }

    private static Color Tint(Color color, float opacity) => new Color(color.R, color.G,
        color.B) * MathHelper.Clamp(opacity, 0, 1);

    public void Dispose()
    {
        foreach (Texture2D image in _images.Values) image.Dispose();
        _images.Clear();
    }
}
