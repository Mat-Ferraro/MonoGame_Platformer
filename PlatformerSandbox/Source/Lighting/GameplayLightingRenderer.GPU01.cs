using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using PlatformerSandbox.Foundation.Performance;
using PlatformerSandbox.Foundation.Presentation.Lighting;

namespace PlatformerSandbox.Lighting;

internal sealed partial class GameplayLightingRenderer
{
    private const long LocalMaskBudget = GpuLocalMaskLayout.ByteBudget;
    private const int MaxLocalMasks=48;
    private sealed class LocalMask
    {
        public readonly RenderTarget2D Target;
        public readonly LightingViewport View;
        public readonly SharedVisibilityKey Key;
        public readonly int Fingerprint;
        public long Used;
        public LocalMask(RenderTarget2D target,LightingViewport view,SharedVisibilityKey key,int fingerprint,long used)
        { Target=target; View=view; Key=key; Fingerprint=fingerprint; Used=used; }
    }
    private readonly List<LocalMask> _localMasks=new();
    private readonly GpuMaskAdmission _localAdmission=new(MaxLocalMasks*2);
    private bool _localMaskUnavailable;
    public int LastGpuLocalBuilds {get;private set;}
    public int LastGpuLocalHits {get;private set;}
    public int LastGpuLocalPriorHits {get;private set;}
    public int LastGpuLocalBypasses {get;private set;}
    public int LastGpuSpecializedPasses {get;private set;}
    public int LastGpuFusedLayers {get;private set;}
    public int LastGpuDirectLights {get;private set;}
    public int LastGpuFastBloom {get;private set;}
    public int LastGpuSkippedBinds {get;private set;}
    public long LastGpuSkippedClearPixels {get;private set;}
    public long GpuLocalBytes {get;private set;}
    private bool HasGpuTechnique(string name)=>_basicSurfaceEffect?.Techniques[name] is not null;
    private bool TryGpuTechnique(string name)
    {
        var t=_basicSurfaceEffect?.Techniques[name];
        if(t is null) return false;
        _basicSurfaceEffect!.CurrentTechnique=t;
        return true;
    }
    private void BeginGpuExperiments()
    {
        LastGpuLocalBuilds=LastGpuLocalHits=LastGpuLocalPriorHits=LastGpuLocalBypasses=0;
        LastGpuSpecializedPasses=LastGpuFusedLayers=LastGpuDirectLights=LastGpuFastBloom=0;
        LastGpuSkippedBinds=0; LastGpuSkippedClearPixels=0;
        if (!EnginePerformanceOptions.GpuLocalMasks) ClearLocalMasks();
        else _localAdmission.BeginFrame();
    }
    private void ClearLocalMasks()
    {
        if (_localMasks.Count>0)
        {
            _basicSurfaceEffect?.Parameters["ProductionVisibility0"]?.SetValue(_whiteProductionVisibility);
            _basicSurfaceEffect?.Parameters["ProductionVisibility1"]?.SetValue(_whiteProductionVisibility);
        }
        foreach(var item in _localMasks) item.Target.Dispose();
        _localMasks.Clear(); _localAdmission.Clear(); GpuLocalBytes=0;
    }
    private LocalMask? FindLocal(Texture2D texture)
    {
        foreach(var entry in _localMasks) if(ReferenceEquals(entry.Target,texture)) return entry;
        return null;
    }
    private bool IsLocalMask(Texture2D texture)=>FindLocal(texture) is not null;
    private Rectangle LocalMaskDestination(Texture2D target,LightingViewport view)
    {
        var entry=FindLocal(target) ?? throw new InvalidOperationException("Missing local mask entry");
        var origin=GpuLocalMaskLayout.Origin(view,entry.View.Focus,target.Width);
        return new Rectangle(origin.X,origin.Y,target.Width,target.Height);
    }
    private Vector4 LocalMaskTransform(Texture2D target,LightingViewport view)
    {
        if(!IsLocalMask(target)) return new Vector4(1,1,0,0);
        Rectangle r=LocalMaskDestination(target,view);
        return new Vector4(view.Width/(float)r.Width,view.Height/(float)r.Height,-r.X/(float)r.Width,-r.Y/(float)r.Height);
    }
    // World-centered, full-pixel-resolution hard point fans only. The 2px guard
    // covers rounding and the original 1.02 radius. Subpixel translation is
    // quantized to a pixel, deliberately an opt-in quality experiment.
    private bool TryLocalMask(SpriteBatch batch,LightingViewport view,RadialLight light,
        IReadOnlyList<LightingOccluder> blockers,int samples,bool respectLitFaces,out RenderTarget2D mask)
    {
        mask=null!;
        if(_localMaskUnavailable || light.Shape!=LightShape.Point || !GpuLocalMaskLayout.TrySize(light.Radius,view.PixelsPerUnit,out int size))
        { LastGpuLocalBypasses++; return false; }
        var local=new LightingViewport(size,size,light.Position,view.PixelsPerUnit);
        var receiver=new SurfaceReceiverProjection();
        int fingerprint=SharedVisibilityKey.Fingerprint(local,light,receiver,samples,respectLitFaces,0,false,0,blockers);
        bool stable=_localAdmission.Observe(fingerprint);
        foreach(var entry in _localMasks)
        {
            if(entry.Fingerprint!=fingerprint || entry.Target.IsDisposed || !entry.Key.Matches(local,light,receiver,samples,respectLitFaces,0,false,0,blockers)) continue;
            if(entry.Used!=_sharedFrame) LastGpuLocalPriorHits++;
            entry.Used=_sharedFrame; mask=entry.Target; LastGpuLocalHits++; return true;
        }
        // A changing light or caster must not allocate a texture on its first appearance.
        // A consecutive-frame stability streak is required for allocation; exact key matching is mandatory for reuse.
        if(!stable) { LastGpuLocalBypasses++; return false; }
        long bytes=(long)size*size*4;
        // Never evict a current-frame texture: a material pair may reference it.
        while(_localMasks.Count>=MaxLocalMasks || GpuLocalBytes+bytes>LocalMaskBudget)
        {
            LocalMask? victim=null;
            foreach(var entry in _localMasks)
                if(entry.Used!=_sharedFrame && (victim is null || entry.Used<victim.Used)) victim=entry;
            if(victim is null) {LastGpuLocalBypasses++; return false;}
            GpuLocalBytes-=(long)victim.Target.Width*victim.Target.Height*4;
            victim.Target.Dispose(); _localMasks.Remove(victim);
        }
        RenderTarget2D target;
        try { target=NewL19Target(size,size,SurfaceFormat.Color); }
        catch(Exception ex) when(ex is NotSupportedException or InvalidOperationException)
        { _localMaskUnavailable=true; LastGpuLocalBypasses++; return false; }
        var prev=_geometryEffect.Projection;
        var region=_materialMaskRegion;
        try
        {
            _geometryEffect.Projection=Matrix.CreateOrthographicOffCenter(0,size,size,0,0,1);
            _materialMaskRegion=null;
            DrawVisibilityFan(batch,local,light,blockers,samples,target,respectLitFaces:respectLitFaces);
            _localMasks.Add(new LocalMask(target,local,new SharedVisibilityKey(local,light,receiver,samples,respectLitFaces,0,false,0,blockers),fingerprint,_sharedFrame));
            GpuLocalBytes+=bytes; LastGpuLocalBuilds++; mask=target; return true;
        }
        catch { target.Dispose(); throw; } // never replay an uncertain draw
        finally { _geometryEffect.Projection=prev; _materialMaskRegion=region; }
    }
}
