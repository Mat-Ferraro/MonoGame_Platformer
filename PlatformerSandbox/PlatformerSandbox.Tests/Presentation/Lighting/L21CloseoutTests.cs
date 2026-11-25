using System;
using System.IO;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using PlatformerSandbox.Gameplay.Content;
using PlatformerSandbox.Gameplay.GameFlow;
using PlatformerSandbox.Gameplay.Presentation;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;
public sealed class L21CloseoutTests
{
    private static RadialLight Tube => new() { Id="area", Shape=LightShape.Tube, Direction=new(1,0),
        TubeLength=8, EmitterWidth=4, Radius=12, Intensity=2, CastShadows=true, VirtualZ=1 };
    [Theory]
    [InlineData(AreaEmitterFootprint.Line)]
    [InlineData(AreaEmitterFootprint.Disc)]
    [InlineData(AreaEmitterFootprint.Rectangle)]
    public void FiniteEmitterSamplesPreserveEnergyOwnerAndCentroid(AreaEmitterFootprint footprint)
    {
        var light=Tube with { EmitterFootprint=footprint, EmitterCasterId="own" };
        foreach(int n in new[]{1,3,5,9})
        {
            var samples=AreaEmitterSampling.Samples(light,n);
            Assert.Equal(n,samples.Count); Assert.Equal(light.Intensity,samples.Sum(s=>s.Intensity),10);
            Assert.Equal(light.Position.X,samples.Average(s=>s.Position.X),10);
            Assert.Equal(light.Position.Y,samples.Average(s=>s.Position.Y),10);
            Assert.All(samples,s=> { Assert.Equal(LightShape.Point,s.Shape); Assert.Equal(light.Id,s.Id);
                Assert.Equal(light.EmitterCasterId,s.EmitterCasterId); Assert.True(s.CastShadows); Assert.Equal(light.VirtualZ,s.VirtualZ); });
        }
    }
    [Fact]
    public void LineEmitterCanBePartiallyOccludedWhereMidpointIsBlocked()
    {
        var wall=new LightingOccluder {Id="wall",Vertices=new LightPoint[]{new(-.3,1),new(.3,1),new(.3,2),new(-.3,2)}};
        var sample=new LightPoint(0,4); var blockers=new[]{wall};
        var visible=AreaEmitterSampling.Samples(Tube).Count(l=>ShadowVisibility.CanIlluminate(l,sample,blockers));
        Assert.InRange(visible,1,8);
        Assert.False(ShadowVisibility.CanIlluminate(AreaEmitterSampling.Samples(Tube,1)[0],sample,blockers));
        var art=new LightingArtPreset {Id="test",Ambient=LightRgb.Black};
        var partial=LightingWorldSampler.Sample(sample,LightChannel.Hero,true,null,art,new[]{Tube},blockers);
        var clear=LightingWorldSampler.Sample(sample,LightChannel.Hero,false,null,art,new[]{Tube},blockers);
        Assert.True(partial.R>0 && partial.R<clear.R);
    }
    [Fact]
    public void ExpandedMaterialBatchesStayBoundedAndCoverEverySample()
    {
        int n=ProductionMaterialLightPlan.MaximumLights*AreaEmitterSampling.MaximumSamples;
        Assert.Equal(n,ProductionMaterialLightPlan.Batches(n,true).Sum(b=>b.Count));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ProductionMaterialLightPlan.Batches(n+1,true));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ProductionMaterialLightPlan.Batches(13));
    }
    [Fact]
    public void TangentVectorsReflectBeforeClockwiseRotationAndPulseIsDeterministic()
    {
        var v=TangentMapTransform.From(90,true,false).Apply(new(1,0));
        Assert.Equal(0.0,v.X,10);Assert.Equal(-1.0,v.Y,10);
        var l=Tube with {PulseDepth=.5,PulseHz=1,PulseTint=new LightRgb(0,0,1)};
        var a=PresentationLightPulse.At(l,.5);
        Assert.Equal(1.0,a.Intensity,10);Assert.Equal(new LightRgb(0,0,1),a.Color);
        Assert.Equal(a,PresentationLightPulse.At(l,1.5));
    }
    [Fact]
    public void ExplicitLitOverridePromotesOnAnOtherwiseUnlitLayer()
    {
        var layer=SceneLightingLayerSet.Standard11().Gameplay with {Mode=RenderLayerLightingMode.Unlit};
        var o=new LightingSurfaceOptions {LightingMode=LightingSurfaceMode.Lit};
        Assert.True(SpatialSurfaceLighting.CanPromote(o,layer));
        Assert.True(SpatialSurfaceLighting.Promote("override",o).ExplicitLitOverride);
        Assert.False(SpatialSurfaceLighting.CanPromote(o with {LightingMode=LightingSurfaceMode.Inherit},layer));
    }
    private static string ReadFixture()
    {
        string? p=AppContext.BaseDirectory;
        for(int i=0;i<14 && p is not null;i++,p=Directory.GetParent(p)?.FullName)
        {
            string f=Path.Combine(p,"Content","Levels","Phase21LightingLab.ldtk");if(File.Exists(f))return File.ReadAllText(f);
        }
        throw new FileNotFoundException("Phase21LightingLab.ldtk");
    }
    [Fact]
    public void DedicatedLabImportsEachModeMappedMotionAndGeneratedGeometry()
    {
        var scene=LdtkLightingImporter.FromJson(ReadFixture(),GameLevelIds.Phase21LightingLab);
        Assert.Equal(5,scene.Lights.Count);
        Assert.Single(scene.Lights.Where(l=>l.Tag=="AREA_POINT" && l.Shape==LightShape.Point));
        foreach(var footprint in Enum.GetValues<AreaEmitterFootprint>())
            Assert.Single(scene.Lights.Where(l=>l.Shape==LightShape.Tube && l.EmitterFootprint==footprint));
        Assert.Single(scene.Lights.Where(l=>l.PulseDepth>0));
        Assert.Contains(scene.Occluders,o=>o.Vertices.Length>4);
        Assert.Contains(scene.Surfaces,s=>s.Value.NormalMap is not null && scene.OccluderMotions.ContainsKey(s.Key));
        var presenter=new GameplayLightingPresenter {Quality=LightingQuality.High};presenter.EnterWorld(1,scene);
        var view=new LightingViewport(1280,720,new(20,20),32);
        Assert.Single(presenter.BuildVisible(view,filterTag:"AREA_LINE"));
        Assert.Equal(5,presenter.BuildVisible(view,filterTag:"AREA_RECT|SHOWCASE|EMISSIVE").Count);
    }
}
