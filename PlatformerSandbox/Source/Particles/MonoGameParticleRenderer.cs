using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Foundation.Presentation.Particles;

namespace PlatformerSandbox.Particles;

/// <summary>
/// Immutable draw binding: a pool is attached to exactly one space, layer, blend
/// mode and shared sprite silhouette in the P02 playground. P03 adds multi-emitter
/// effects and configurable style metadata without changing particle physics.
/// </summary>
internal readonly record struct ParticleDrawSource(
    ParticleSystem Pool, ParticleVisualLayer Layer, ParticleCoordinateSpace Space,
    ParticleBlendMode Blend, ParticleSpriteShape Sprite,
    ParticleLightingMode Lighting = ParticleLightingMode.Scene)
{
    public bool ReceivesSceneLight => ParticleLightingPolicy.ReceivesSceneLight(Space, Lighting);
}

/// <summary>
/// SpriteBatch adapter. GPU resources are created once and shared by all
/// particles. Textures are premultiplied-white, as are interpolated tints.
/// Renderer never allocates a texture or other GPU resource during Draw.
/// </summary>
internal sealed class MonoGameParticleRenderer : IDisposable
{
    private const int SpriteSize = 32;
    private readonly Texture2D[] _sprites;
    private bool _disposed;
    public int LastDrawCount { get; private set; }

    public MonoGameParticleRenderer(GraphicsDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _sprites = new Texture2D[4];
        try
        {
            for (int shape = 0; shape < _sprites.Length; shape++)
            {
                var texture = new Texture2D(device, SpriteSize, SpriteSize, false, SurfaceFormat.Color);
                var pixels = new Color[SpriteSize * SpriteSize];
                for (int y = 0; y < SpriteSize; y++)
                for (int x = 0; x < SpriteSize; x++)
                {
                    double ux = (x + .5 - SpriteSize * .5) / (SpriteSize * .5);
                    double uy = (y + .5 - SpriteSize * .5) / (SpriteSize * .5);
                    double radius = Math.Sqrt(ux * ux + uy * uy);
                    double alpha = (ParticleSpriteShape)shape switch
                    {
                        ParticleSpriteShape.SoftDisc => Math.Clamp((1.0 - radius) * 2.0, 0, 1),
                        ParticleSpriteShape.SolidDisc => Math.Clamp((1.0 - radius) * 12.0, 0, 1),
                        ParticleSpriteShape.Spark => Math.Clamp(
                            Math.Max(1 - Math.Abs(ux) * 5, 1 - Math.Abs(uy) * 5) *
                            Math.Clamp((1.0 - radius) * 1.2, 0, 1), 0, 1),
                        _ => 1
                    };
                    // MonoGame AlphaBlend assumes premultiplied source pixels.
                    byte a = (byte)Math.Round(alpha * 255);
                    pixels[y * SpriteSize + x] = new Color(a, a, a, a);
                }
                _sprites[shape] = texture;
                texture.SetData(pixels);
            }
        }
        catch
        {
            foreach (Texture2D? sprite in _sprites) sprite?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Call at each explicit draw boundary (background/world/foreground/UI).
    /// A separate alpha pass precedes additive, with a stable order per source.
    /// No SpriteBatch is already begun when this method is called.
    /// </summary>
    public int DrawLayer(SpriteBatch batch, in ParticleViewport viewport,
        ReadOnlySpan<ParticleDrawSource> sources, ParticleVisualLayer layer,
        ParticleBlendMode? onlyBlend = null, bool? receivesSceneLight = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(batch);
        viewport.Validate();
        int drawn = 0;
        for (int pass = 0; pass < 2; pass++)
        {
            ParticleBlendMode blend = (ParticleBlendMode)pass;
            if (onlyBlend.HasValue && blend != onlyBlend.Value) continue;
            bool started = false;
            for (int i = 0; i < sources.Length; i++)
            {
                ParticleDrawSource source = sources[i];
                if (source.Layer != layer || source.Blend != blend ||
                    (receivesSceneLight.HasValue && source.ReceivesSceneLight != receivesSceneLight.Value)) continue;
                if (!started)
                {
                    batch.Begin(blendState: blend == ParticleBlendMode.Alpha
                        ? BlendState.AlphaBlend : BlendState.Additive,
                        samplerState: SamplerState.LinearClamp,
                        sortMode: SpriteSortMode.Deferred);
                    started = true;
                }
                ParticleSystem pool = source.Pool;
                Texture2D texture = _sprites[(int)source.Sprite];
                Vector2 origin = new(SpriteSize * .5f, SpriteSize * .5f);
                for (int j = 0; j < pool.ActiveCount; j++)
                {
                    ParticleState particle = pool.GetAt(j);
                    double diameter = viewport.SizePixels(particle.Size, source.Space);
                    if (diameter <= .001) continue;
                    ParticleScreenPoint center = viewport.ToScreen(particle.Position, source.Space);
                    if (!viewport.IsVisible(center, diameter)) continue;
                    // The source texture is already premultiplied white; tint
                    // RGB must also be premultiplied by requested opacity.
                    ParticlePremultipliedRgba rgba =
                        ParticlePremultipliedRgba.From(particle.Color);
                    Color tint = new(rgba.R, rgba.G, rgba.B, rgba.A);
                    if (tint.A == 0) continue;
                    float rotation = (float)(source.Space == ParticleCoordinateSpace.World
                        ? -particle.RotationRadians : particle.RotationRadians);
                    batch.Draw(texture, new Vector2((float)center.X, (float)center.Y),
                        null, tint, rotation, origin, (float)(diameter / SpriteSize),
                        SpriteEffects.None, 0f);
                    drawn++;
                }
            }
            if (started) batch.End();
        }
        LastDrawCount += drawn;
        return drawn;
    }

    // Avoid an empty full-viewport lighting pass when all eligible particles
    // are outside the viewport or have faded out.
    public static bool HasVisibleLit(in ParticleViewport viewport,
        ReadOnlySpan<ParticleDrawSource> sources, ParticleVisualLayer layer, ParticleBlendMode blend)
    {
        foreach (ParticleDrawSource source in sources)
        {
            if (source.Layer != layer || source.Blend != blend || !source.ReceivesSceneLight) continue;
            for (int j = 0; j < source.Pool.ActiveCount; j++)
            {
                ParticleState particle = source.Pool.GetAt(j);
                double diameter = viewport.SizePixels(particle.Size, source.Space);
                if (diameter > .001 && ParticlePremultipliedRgba.From(particle.Color).A != 0 &&
                    viewport.IsVisible(viewport.ToScreen(particle.Position, source.Space), diameter))
                    return true;
            }
        }
        return false;
    }

    public void BeginFrame() => LastDrawCount = 0;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (Texture2D? texture in _sprites) texture?.Dispose();
    }
}
