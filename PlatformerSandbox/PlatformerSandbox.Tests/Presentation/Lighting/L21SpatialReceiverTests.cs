using System;
using System.Linq;
using PlatformerSandbox.Foundation.Presentation.Lighting;
using Xunit;

namespace PlatformerSandbox.Tests.Presentation.Lighting;

public sealed class L21SpatialReceiverTests
{
    [Fact]
    public void LocalLightCanReachOnlyTheUpperEdgeOfASprite()
    {
        var view = new LightingViewport(320, 320, new LightPoint(0,0),32);
        var region = new ProductionMaterialRegion(145,96,30,128);
        var light = new RadialLight { Id="head", Position=new LightPoint(0,2.5), Radius=1 };
        var layer = SceneLightingLayerSet.Standard11().Gameplay;
        Assert.Equal(0.0, RadialLighting.Attenuation(light,new LightPoint(0,0)));
        Assert.Single(SpatialSurfaceLighting.Candidates(new[]{light},layer,view,region,1));
        Assert.Empty(SpatialSurfaceLighting.Candidates(new[]{light with {Position=new LightPoint(20,20)}},layer,view,region,1));
    }

    [Fact]
    public void HorizontalDirectionalShadowSeparatesUpperAndLowerSamples()
    {
        var art = new LightingArtPreset { Id="test", Ambient=new LightRgb(.2,.2,.2) };
        var light = new RadialLight { Id="sun", Shape=LightShape.Directional, Direction=new LightPoint(1,0), CastShadows=true, DirectionalShadowLength=6 };
        var wall = new LightingOccluder { Id="wall", Vertices=new LightPoint[]{new(-2,-2),new(-1,-2),new(-1,0),new(-2,0)} };
        var layer = SceneLightingLayerSet.Standard11().Gameplay;
        var head = LightingWorldSampler.Sample(new(0,1),LightChannel.Hero,true,null,art,new[]{light},new[]{wall},layer);
        var feet = LightingWorldSampler.Sample(new(0,-1),LightChannel.Hero,true,null,art,new[]{light},new[]{wall},layer);
        Assert.True(head.R > feet.R);
        Assert.Equal(.2,feet.R,8);
        var sprite = SpatialSurfaceLighting.RuntimeSprite("item",layer,LightChannel.Hero,new(0,0));
        sprite.Validate(); Assert.True(sprite.ReceivesShadows); Assert.Null(sprite.CasterId);
    }

    [Fact]
    public void LegacyPromotionPreservesParticipationAndCasterIdentity()
    {
        var layer = SceneLightingLayerSet.Standard11().Gameplay;
        var options = new LightingSurfaceOptions { CastsShadows=true, ReceivesShadows=true, ContactShadow=true };
        Assert.True(SpatialSurfaceLighting.CanPromote(options,layer));
        var surface = SpatialSurfaceLighting.Promote("crate",options);
        Assert.Equal("crate",surface.CasterId); Assert.True(surface.ReceivesShadows);
        Assert.Equal(ProductionSurfaceShading.Flat,surface.Shading);
        Assert.False(SpatialSurfaceLighting.CanPromote(options with {ReceivesLight=false},layer));
        Assert.False(SpatialSurfaceLighting.CanPromote(options with {LightingMode=LightingSurfaceMode.Silhouette},layer));
        Assert.False(SpatialSurfaceLighting.CanPromote(options,layer with {Mode=RenderLayerLightingMode.Unlit}));
    }

    [Fact]
    public void EmissionAndWorldLightAreIndependentAndOnlyOwnCasterIsExempt()
    {
        var options=new LightingSurfaceOptions { MaterialShading=ProductionSurfaceShading.Flat,
            CastsShadows=true, EmissionColor=new LightRgb(.1,.6,1), EmissionIntensity=5 };
        Assert.Null(EmissiveWorldLighting.Create("crystal",new(0,0),options));
        var light=EmissiveWorldLighting.Create("crystal",new(2,3),options with { EmissiveLightRadius=5, EmissiveLightIntensity=.8 })!;
        Assert.Equal(options.EmissionColor,light.Color); Assert.Equal(.8,light.Intensity); Assert.True(light.CastShadows);
        Assert.False(LightLayerRouting.Occludes(light,new LightingOccluder { Id="piece",OwnerId="crystal" }));
        Assert.True(LightLayerRouting.Occludes(light,new LightingOccluder { Id="other" }));
        Assert.NotNull(EmissiveWorldLighting.Create("crystal",new(0,0),options with {EmissionIntensity=0,EmissiveLightRadius=3}));
    }

    [Fact]
    public void ShadowDependentEmissionDoesNotLeakWhenShadowBudgetIsExhausted()
    {
        var view=new LightingViewport(320,320,new(0,0),32);
        var level=new RadialLight {Id="level",Owner="level",CastShadows=true};
        var glow=new RadialLight {Id="glow",Owner="emissive",CastShadows=true};
        var selected=LightingFramePlanner.Select(new[]{level,glow},view,new LightingFrameBudget(4,1,64,1),false);
        Assert.Single(selected); Assert.Equal("level",selected[0].Id);
    }
}
