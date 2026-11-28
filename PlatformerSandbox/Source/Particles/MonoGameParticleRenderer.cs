using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Foundation.Presentation.Particles;
using PlatformerSandbox.Foundation.Performance;

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
    private readonly GpuParticleInstances _gpu;
    private GpuParticleInstances.Instance[]? _instances;
    private GpuParticleInstances.Instance[]? _stressSeeds;
    public int LastGpuInstances=>_gpu.Instances;
    public int LastGpuDraws=>_gpu.Draws;
    public int LastGpuAnalytic=>_gpu.AnalyticInstances;
    public int LastGpuFallbacks=>_gpu.Fallbacks;
    public long LastGpuUploadBytes=>_gpu.UploadedBytes;
    public string GpuStatus=>_gpu.Status;
    public int LastDrawCount { get; private set; }

    public MonoGameParticleRenderer(GraphicsDevice device,Effect? instanceEffect=null)
    {
        ArgumentNullException.ThrowIfNull(device);
        _gpu=new GpuParticleInstances(device,instanceEffect);
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
            _gpu.Dispose();
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
        if(EnginePerformanceOptions.GpuInstancedParticles)
            return DrawInstancedLayer(batch,viewport,sources,layer,onlyBlend,receivesSceneLight);
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

    private int DrawInstancedLayer(SpriteBatch batch,in ParticleViewport viewport,
        ReadOnlySpan<ParticleDrawSource> sources,ParticleVisualLayer layer,ParticleBlendMode? onlyBlend,bool? receivesSceneLight)
    {
        _instances??=new GpuParticleInstances.Instance[GpuParticleInstances.MaximumInstances];
        int drawn=0;
        for(int pass=0;pass<2;pass++)
        {
            var blend=(ParticleBlendMode)pass;
            if(onlyBlend.HasValue && blend!=onlyBlend.Value)continue;
            var state=blend==ParticleBlendMode.Alpha?BlendState.AlphaBlend:BlendState.Additive;
            foreach(var source in sources)
            {
                if(source.Layer!=layer || source.Blend!=blend || (receivesSceneLight.HasValue && source.ReceivesSceneLight!=receivesSceneLight.Value))continue;
                var texture=_sprites[(int)source.Sprite]; int n=0;
                for(int j=0;j<source.Pool.ActiveCount;j++)
                {
                    var p=source.Pool.GetAt(j);double d=viewport.SizePixels(p.Size,source.Space);
                    if(d<=.001)continue;
                    var center=viewport.ToScreen(p.Position,source.Space);
                    if(!viewport.IsVisible(center,d))continue;
                    var rgba=ParticlePremultipliedRgba.From(p.Color);
                    if(rgba.A==0)continue;
                    _instances[n++]=new GpuParticleInstances.Instance(new Vector4((float)center.X,(float)center.Y,(float)d,
                        (float)(source.Space==ParticleCoordinateSpace.World?-p.RotationRadians:p.RotationRadians)),new Color(rgba.R,rgba.G,rgba.B,rgba.A));
                    if(n==_instances.Length){DrawInstanceChunk(batch,texture,state,n);drawn+=n;n=0;}
                }
                if(n>0){DrawInstanceChunk(batch,texture,state,n);drawn+=n;}
            }
        }
        LastDrawCount+=drawn;return drawn;
    }
    private void DrawInstanceChunk(SpriteBatch batch,Texture2D texture,BlendState blend,int n)
    {
        if(_gpu.TryDraw(_instances!,n,texture,blend,false,0))return;
        batch.Begin(blendState:blend,samplerState:SamplerState.LinearClamp);
        try{for(int i=0;i<n;i++) DrawInstance(batch,texture,_instances![i]);}finally{batch.End();}
    }
    private static void DrawInstance(SpriteBatch batch,Texture2D texture,GpuParticleInstances.Instance p)
        =>batch.Draw(texture,new Vector2(p.Data.X,p.Data.Y),null,p.Tint,p.Data.W,new Vector2(SpriteSize*.5f),p.Data.Z/SpriteSize,SpriteEffects.None,0);
    // Explicit lab-only screen-space decorative workload. Immutable seed data;
    // GPU analytic mode computes motion at vertices without per-frame uploads.
    public void DrawStress(SpriteBatch batch,int count,int width,int height,float time,bool analytic)
    {
        if(count<=0)return;
        count=Math.Min(count,GpuParticleInstances.MaximumInstances);
        if(_stressSeeds is null)
        {
            _stressSeeds=new GpuParticleInstances.Instance[GpuParticleInstances.MaximumInstances];
            var rng=new Random(7051);
            for(int i=0;i<_stressSeeds.Length;i++)
                _stressSeeds[i]=new GpuParticleInstances.Instance(new Vector4((float)rng.NextDouble(),(float)rng.NextDouble(),4f+(float)rng.NextDouble()*7f,(float)rng.NextDouble()),new Color(75,56,32,96));
        }
        var texture=_sprites[(int)ParticleSpriteShape.SoftDisc];
        if(analytic && EnginePerformanceOptions.GpuInstancedParticles && _gpu.TryDraw(_stressSeeds,count,texture,BlendState.AlphaBlend,true,time))
        {LastDrawCount+=count;return;}
        _instances??=new GpuParticleInstances.Instance[GpuParticleInstances.MaximumInstances];
        for(int i=0;i<count;i++)
        {
            var seed=_stressSeeds[i]; float phase=seed.Data.W;
            float x=seed.Data.X+time*(.025f+phase*.035f),y=seed.Data.Y+time*(.04f+phase*.05f);
            seed.Data=new Vector4((x-MathF.Floor(x))*width,(y-MathF.Floor(y))*height,seed.Data.Z,phase*6.2831853f+time*.6f);
            _instances[i]=seed;
        }
        if(EnginePerformanceOptions.GpuInstancedParticles) DrawInstanceChunk(batch,texture,BlendState.AlphaBlend,count);
        else
        {batch.Begin(blendState:BlendState.AlphaBlend,samplerState:SamplerState.LinearClamp);try{for(int i=0;i<count;i++)DrawInstance(batch,texture,_instances[i]);}finally{batch.End();}}
        LastDrawCount+=count;
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

    public void BeginFrame() {LastDrawCount=0;_gpu.BeginFrame(EnginePerformanceOptions.GpuInstancedParticles);}

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _gpu.Dispose();
        foreach (Texture2D? texture in _sprites) texture?.Dispose();
    }
}
