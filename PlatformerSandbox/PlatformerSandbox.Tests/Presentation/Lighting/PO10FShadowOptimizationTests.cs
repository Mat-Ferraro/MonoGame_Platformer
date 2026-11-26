using System;
using System.Collections.Generic;
using System.Linq;
using PlatformerSandbox.Foundation.Performance;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class PO10FShadowOptimizationTests
{
    private static LightingOccluder Caster(string id,double x) => new() {
        Id=id,Vertices=new LightPoint[]{new(x,0),new(x+1,0),new(x+1,1),new(x,1)} };
    [Fact] public void ExactCasterSnapshotRejectsInPlaceEditsAndRoutingChanges()
    {
        var caster=Caster("wall",0); var original=new[]{caster};
        var snapshot=StaticShadowFieldSnapshot.Capture(original);
        Assert.True(StaticShadowFieldSnapshot.Matches(snapshot,original));
        Assert.False(StaticShadowFieldSnapshot.Matches(snapshot,new[]{caster with { LayerId="BG1" }}));
        Assert.False(StaticShadowFieldSnapshot.Matches(snapshot,new[]{caster with { OwnerId="other" }}));
        Assert.False(StaticShadowFieldSnapshot.Matches(snapshot,new[]{caster with { LitFaceDepthZ=1 }}));
        caster.Vertices[0]=new LightPoint(-1,0);
        Assert.False(StaticShadowFieldSnapshot.Matches(snapshot,original));
        Assert.Equal(new LightPoint(0,0),snapshot[0].Vertices[0]);
        Assert.False(StaticShadowFieldSnapshot.Matches(snapshot,Array.Empty<LightingOccluder>()));
    }
    [Fact] public void ReusedPruningBufferPreservesOrderAndClearsOldEntries()
    {
        var light=new RadialLight { Id="test",Radius=3,CastShadows=true };
        var casters=new[]{Caster("near",1),Caster("far",30),Caster("near2",-2)};
        var scratch=new List<LightingOccluder>{Caster("stale",2)};
        ShadowCasterPruning.FilterInto(light,casters,scratch);
        Assert.Equal(ShadowCasterPruning.Filter(light,casters).Select(x=>x.Id),scratch.Select(x=>x.Id));
        ShadowCasterPruning.FilterInto(light with { Position=new LightPoint(100,100) },casters,scratch);
        Assert.Empty(scratch);
        Assert.Throws<ArgumentException>(()=>ShadowCasterPruning.FilterInto(light,scratch,scratch));
    }
    [Fact] public void EnableAllNeverChangesQualityControls()
    {
        var p=LightingQualityLabProfile.For(3) with { LightScaleOverride=25,MaskScaleOverride=75,BloomScale=50,AreaSamples=5,OptimizationMask=0 };
        var a=p.EnableAllOptimizations();
        Assert.True(a.AllRuntimeOptimizations);
        Assert.Equal(p.Scale,a.Scale); Assert.Equal(p.LightScaleOverride,a.LightScaleOverride);
        Assert.Equal(p.MaskScaleOverride,a.MaskScaleOverride); Assert.Equal(p.BloomScale,a.BloomScale);
        Assert.Equal(p.AreaSamples,a.AreaSamples); Assert.Equal(p.Adaptive,a.Adaptive);
        var g=new GraphicsPreferences { LightScale=25,MaskScale=75,BloomScale=50,AreaSamples=5,VSync=false,FpsLimit=60 };
        var updated=g.EnableAllOptimizations();
        Assert.True(updated.AllEstablishedOptimizations); Assert.True(updated.IsValid);
        Assert.False(updated.StaticShadowFields);
        Assert.Equal(g with { Profile=2,StaticFieldsOverride=true },updated);
    }
    [Fact] public void HardFixtureContainsShadowCastingPointLights()
    {
        var lights=BenchmarkLightingFixtures.Create("static_shadow",new LightingViewport(1280,720,new LightPoint(0,0),32));
        Assert.Equal(5,lights.Count); Assert.All(lights,l=> { Assert.True(l.CastShadows); Assert.Equal(LightShape.Point,l.Shape); });
    }
    [Theory]
    [InlineData("static_shadow")]
    [InlineData("static")]
    [InlineData("adaptive")]
    public void FixtureLightsStayInWorldSpaceDuringCameraMovementAndResize(string fixture)
    {
        var set = new BenchmarkLightingFixtureSet(new LightPoint(20,15),40,40,32);
        var lights = set.Get(fixture);
        var initial = lights.Select(l=>l.Position).ToArray();
        var a = set.Anchor;
        var moved = a with { Focus = new LightPoint(a.Focus.X,a.Focus.Y+4) };
        var resized = moved with { Width=1920,Height=1080 };
        // Camera movement changes screen coordinates only. Fixture switching and
        // resizing do not recreate or move the world-space lights.
        foreach(var view in new[]{a,moved,resized})
        {
            _=set.Get("static"); _=set.Get("adaptive");
            Assert.Same(lights,set.Get(fixture));
            Assert.Equal(initial,set.Get(fixture).Select(l=>l.Position).ToArray());
            for(int i=0;i<lights.Count;i++)
            {
                var roundTrip=view.Unproject(view.Project(lights[i].Position));
                Assert.Equal(initial[i].X,roundTrip.X,9);
                Assert.Equal(initial[i].Y,roundTrip.Y,9);
            }
        }
        Assert.NotEqual(a.Project(lights[2].Position),moved.Project(lights[2].Position));
        Assert.Equal(a.Project(lights[2].Position).Y+4*32,moved.Project(lights[2].Position).Y);
        var nextScene = new BenchmarkLightingFixtureSet(new LightPoint(20,25),40,40,32);
        Assert.NotEqual(initial[2],nextScene.Get(fixture)[2].Position);
    }
}
