using System;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Foundation.Presentation;

namespace PlatformerSandbox.Presentation;

/// <summary>
/// MonoGame adapter for a data-authored sprite atlas. PNG is deployed as a
/// regular content file; an XNB is also accepted for backward compatibility.
/// Gameplay state, animation timeline and collision remain independent.
/// </summary>
internal sealed class LdtkCharacterSpriteAssets : IDisposable
{
    public SpriteSheetDefinition Definition { get; }
    private readonly Texture2D _texture;
    private readonly bool _ownsTexture;

    private LdtkCharacterSpriteAssets(Texture2D texture,
        SpriteSheetDefinition definition, bool ownsTexture)
    {
        _texture = texture;
        _ownsTexture = ownsTexture;
        Definition = definition;
    }

    public static LdtkCharacterSpriteAssets Load(ContentManager content,
        GraphicsDevice graphicsDevice)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(graphicsDevice);

        string manifestPath = Path.Combine(AppContext.BaseDirectory,
            "Content", "Characters", "C02_TestCharacter.anim.json");
        SpriteSheetDefinition sheet = SpriteSheetDefinition.FromJson(
            File.ReadAllText(manifestPath));

        // Validate required character clip names before allocating a texture.
        _ = new LdtkGrayboxAnimator(sheet);

        // This project's custom Content Builder does not always emit a new
        // XNB when a PNG is first added. Copying and loading the source PNG
        // directly makes the development/test sprite self-contained and also
        // lets artists replace its frames without adjusting the pipeline.
        string relativePng = sheet.TextureAsset.Replace('/',
            Path.DirectorySeparatorChar) + ".png";
        string rawPngPath = Path.Combine(AppContext.BaseDirectory,
            "Content", relativePng);

        Texture2D atlas;
        bool ownsTexture;
        if (File.Exists(rawPngPath))
        {
            using FileStream stream = File.OpenRead(rawPngPath);
            atlas = Texture2D.FromStream(graphicsDevice, stream);
            ownsTexture = true;
        }
        else
        {
            // Support existing installations using Content.Load and XNB.
            // If neither file exists, the game host retains its C01 fallback.
            atlas = content.Load<Texture2D>(sheet.TextureAsset);
            ownsTexture = false;
        }

        try
        {
            if (atlas.Width != sheet.TextureWidth ||
                atlas.Height != sheet.TextureHeight)
            {
                throw new InvalidDataException(
                    $"Character atlas is {atlas.Width}x{atlas.Height}, " +
                    $"expected {sheet.TextureWidth}x{sheet.TextureHeight} " +
                    $"from {Path.GetFileName(manifestPath)}.");
            }
            return new LdtkCharacterSpriteAssets(atlas, sheet, ownsTexture);
        }
        catch
        {
            if (ownsTexture) atlas.Dispose();
            throw;
        }
    }

    public void Draw(SpriteBatch batch, Rectangle collision,
        LdtkGrayboxPose pose, Color tint)
    {
        SpriteSheetRegion region = Definition.RegionForFrame(pose.Frame);
        var source = new Rectangle(region.X, region.Y,
            region.Width, region.Height);
        // Anchor the visual at the collider's feet, without feeding animation
        // offsets back into the authoritative character physics.
        var destination = Destination(collision);
        SpriteEffects flip = pose.Facing < 0
            ? SpriteEffects.FlipHorizontally : SpriteEffects.None;
        batch.Draw(_texture, destination, source, tint,
            0f, Vector2.Zero, flip, 0f);
    }

    public Rectangle Destination(Rectangle collision)
    {
        int height = Math.Max(1, collision.Height);
        int width = Math.Max(1, (int)Math.Round(height *
            (double)Definition.FrameWidth / Definition.FrameHeight));
        return new Rectangle(collision.Center.X - width / 2, collision.Bottom - height, width, height);
    }

    public void Dispose()
    {
        if (_ownsTexture) _texture.Dispose();
    }
}
